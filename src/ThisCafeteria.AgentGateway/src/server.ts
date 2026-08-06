import { randomUUID } from "node:crypto";
import express from "express";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { SSEServerTransport } from "@modelcontextprotocol/sdk/server/sse.js";
import { HTTPFacilitatorClient } from "@x402/core/server";
import { ExactEvmScheme } from "@x402/evm/exact/server";
import { CallToolRequestSchema } from "@modelcontextprotocol/sdk/types.js";
import { createPaymentWrapper, x402ResourceServer } from "@x402/mcp";
import { privateKeyToAccount } from "viem/accounts";
import { z } from "zod";
import {
  createAgenticPaymentHttpApp,
  createAgenticPaymentRedeemer,
  type AgenticPaymentChain,
} from "./agenticPaymentRedemption.js";
import { IdempotencyStore, requestHash } from "./requestBinding.js";

const port = Number(process.env.PORT ?? 4022);
const internalBaseUrl = process.env.ASPNET_INTERNAL_URL ?? "http://127.0.0.1:8080";
const gatewaySecret = process.env.AGENT_GATEWAY_SERVICE_SECRET;
const payTo = process.env.X402_PAY_TO;
const usdc = process.env.X402_USDC_ADDRESS;
const facilitatorUrl = process.env.X402_FACILITATOR_URL ?? "http://127.0.0.1:4020";
const maxSessions = 32;
const sessionTtlMs = 15 * 60_000;
const rateWindowMs = 60_000;
const rateLimit = 60;
const sessions = new Map<string, { transport: SSEServerTransport; expiresAt: number }>();
const fulfilled = new IdempotencyStore<{ correlationId: string; result: unknown; settleResponse: unknown; replay?: boolean }>(15 * 60_000, 10_000);
const requests = new Map<string, { count: number; resetAt: number }>();

/** Monotonic counter of facilitator /settle calls. Exported for test observability. */
let settleCallCount = 0;
export { settleCallCount };

if (!gatewaySecret || !payTo || !usdc) {
  throw new Error("AGENT_GATEWAY_SERVICE_SECRET, X402_PAY_TO, and X402_USDC_ADDRESS are required");
}

if (process.env.NODE_ENV === "production") {
  throw new Error("FATAL: The current idempotency store is process-local. Running in production requires a durable store to prevent double-spending across restarts.");
}

const facilitator = new HTTPFacilitatorClient({ url: facilitatorUrl });
const resourceServer = new x402ResourceServer(facilitator);
resourceServer.register("eip155:84532", new ExactEvmScheme());
await resourceServer.initialize();

const paymentRequirements = await resourceServer.buildPaymentRequirements({
  scheme: "exact", network: "eip155:84532", payTo, price: { asset: usdc, amount: "0" },
});

const allowRequest = (key: string) => {
  const now = Date.now();
  const current = requests.get(key);
  if (!current || current.resetAt <= now) { requests.set(key, { count: 1, resetAt: now + rateWindowMs }); return true; }
  if (current.count >= rateLimit) return false;
  current.count += 1;
  return true;
};

const callInternal = async (route: string, body: unknown, correlationId: string) => {
  const response = await fetch(`${internalBaseUrl}${route}`, {
    method: "POST",
    headers: { "content-type": "application/json", "x-agent-gateway-secret": gatewaySecret, "x-correlation-id": correlationId },
    body: JSON.stringify({ ...(body as Record<string, unknown>), correlationId }),
    signal: AbortSignal.timeout(10_000),
  });
  if (!response.ok) throw new Error(`ASP.NET resource request failed with ${response.status}`);
  return response.json();
};

/**
 * Extract the x402 payment metadata from the MCP extra context.
 * Returns undefined if no payment is present.
 */
const extractPayment = (extra: any): any | undefined => {
  return extra?._meta?.["x402/payment"] ?? extra?.meta?.["x402/payment"];
};

const createMcpServer = async () => {
const mcpServer = new McpServer({ name: "ArtisanalBrew Agent Gateway", version: "0.1.0" });

mcpServer.tool("search_products", "Search the ArtisanalBrew catalog (free).", { query: z.string().max(120) }, async ({ query }) => ({
  content: [{ type: "text", text: JSON.stringify(await callInternal("/internal/agent/resources/search-products", { query }, randomUUID())) }],
}));

// Patch request handler to pass _meta to tool callbacks (workaround for SDK limitation)
const originalCallTool = (mcpServer.server as any)._requestHandlers.get(CallToolRequestSchema.shape.method.value);
if (originalCallTool) {
  mcpServer.server.setRequestHandler(CallToolRequestSchema, async (request, extra) => {
    const extendedExtra = { ...extra, _meta: request.params?._meta };
    return originalCallTool(request, extendedExtra);
  });
}

/**
 * Register a paid tool that checks idempotency BEFORE settling x402 payment.
 *
 * Flow:
 * 1. Check for x402 payment metadata — return 402 challenge if absent.
 * 2. Compute idempotency key from (route, args) hash.
 * 3. If the idempotency store has a cached result, return it WITHOUT settling again.
 * 4. Verify the payment with the facilitator.
 * 5. Settle the payment with the facilitator.
 * 6. Execute the backend call and cache the result.
 *
 * This prevents the double-settle bug where replaying a paid request with
 * the same nonce would charge the user twice while returning cached output.
 */
const paidTool = async (name: string, description: string, price: string, route: string, schema: z.ZodRawShape) => {
  const accepts = await resourceServer.buildPaymentRequirements({
    scheme: "exact", network: "eip155:84532", payTo, price: { asset: usdc, amount: price },
  });

  mcpServer.tool(name, description, schema, async (args: any, extra: any) => {
    const paymentMeta = extractPayment(extra);

    // No payment attached — return 402 challenge
    if (!paymentMeta) {
      const error = new Error("402 Payment Required") as any;
      error.code = -32002;
      error.data = { accepts };
      throw error;
    }

    // Idempotency check BEFORE settlement — prevents double-charge
    const hash = requestHash("POST", route, args, paymentMeta.payload ?? paymentMeta);
    
    try {
      const cachedResponse = await fulfilled.executeAtomic(hash, async () => {
        // Verify payment with facilitator
        const verifyResponse = await facilitator.verify(paymentMeta.payload ?? paymentMeta, accepts[0]);
        if (!verifyResponse || typeof verifyResponse !== "object" || "error" in verifyResponse || (verifyResponse as any).isValid !== true) {
          const verifyError = new Error(`Payment verification failed: ${JSON.stringify(verifyResponse)}`) as any;
          verifyError.code = -32002;
          throw verifyError;
        }

        // Settle payment — only happens for new (non-replay) requests
        const settleResponse = await facilitator.settle(paymentMeta.payload ?? paymentMeta, accepts[0]);
        settleCallCount++;

        if (settleResponse && typeof settleResponse === "object" && "error" in settleResponse) {
          const settleError = new Error(`Payment settlement failed: ${JSON.stringify(settleResponse)}`) as any;
          settleError.code = -32002;
          throw settleError;
        }

        // Execute backend call
        const correlationId = randomUUID();
        const result = await callInternal(route, { ...args, requestHash: hash }, correlationId);
        
        return { correlationId, result, settleResponse, replay: false };
      });

      const paymentReceipt = cachedResponse.settleResponse && typeof cachedResponse.settleResponse === "object"
        ? cachedResponse.settleResponse : { settled: true };

      return {
        _meta: { "x402/payment-response": paymentReceipt },
        content: [{ type: "text", text: JSON.stringify({ 
          correlationId: cachedResponse.correlationId, 
          requestHash: hash, 
          result: cachedResponse.result,
          replay: cachedResponse.replay
        }) }],
      };
    } catch (error) {
      throw error;
    }
  });
};

await paidTool("create_brew_plan", "Create a structured brew plan; 0.01 test USDC.", "10000", "/internal/agent/resources/brew-plan", { productId: z.string().max(64), quantity: z.number().int().positive().max(1000) });
await paidTool("get_provenance_report", "Get a provenance report; 0.02 test USDC.", "20000", "/internal/agent/resources/provenance", { productId: z.string().max(64) });
await paidTool("request_wholesale_quote", "Request a wholesale quote; 0.02 test USDC.", "20000", "/internal/agent/resources/wholesale-quote", { productId: z.string().max(64), quantity: z.number().int().positive().max(10000) });
return mcpServer;
};

const app = express();

const agenticChainsJson = process.env.AGENTIC_PAYMENT_CHAINS_JSON;
const agentPrivateKey = process.env.AGENT_SESSION_PRIVATE_KEY;
const agentDeploySalt = process.env.AGENT_SMART_ACCOUNT_SALT;
const agentRedemptionToken = process.env.AGENT_REDEMPTION_API_TOKEN;
if (agenticChainsJson || agentPrivateKey || agentDeploySalt || agentRedemptionToken) {
  if (!agenticChainsJson || !agentPrivateKey || !agentDeploySalt || !agentRedemptionToken) {
    throw new Error(
      "AGENTIC_PAYMENT_CHAINS_JSON, AGENT_SESSION_PRIVATE_KEY, AGENT_SMART_ACCOUNT_SALT, and AGENT_REDEMPTION_API_TOKEN must be configured together",
    );
  }
  const chains = JSON.parse(agenticChainsJson) as AgenticPaymentChain[];
  const redeemer = createAgenticPaymentRedeemer({
    chains,
    signer: privateKeyToAccount(agentPrivateKey as `0x${string}`),
    deploySalt: agentDeploySalt as `0x${string}`,
  });
  app.use(createAgenticPaymentHttpApp({ redeemer, apiToken: agentRedemptionToken }));
}

app.get("/health/live", (_req, res) => res.json({ status: "ok" }));
app.get("/health/ready", async (_req, res) => {
  try { await fetch(facilitatorUrl, { method: "HEAD", signal: AbortSignal.timeout(2_000) }); res.json({ status: "ready", network: "eip155:84532", facilitator: facilitatorUrl }); }
  catch { res.status(503).json({ status: "not_ready", facilitator: facilitatorUrl }); }
});
app.get("/.well-known/agent-card.json", (_req, res) => res.json({ name: "ArtisanalBrew Supplier", protocol: "MCP", x402Support: true, network: "eip155:84532", endpoint: "/sse" }));
app.get("/bazaar", (_req, res) => res.json({ version: "1", resources: [
  { name: "create_brew_plan", method: "POST", price: "10000", asset: usdc, network: "eip155:84532" },
  { name: "get_provenance_report", method: "POST", price: "20000", asset: usdc, network: "eip155:84532" },
  { name: "request_wholesale_quote", method: "POST", price: "20000", asset: usdc, network: "eip155:84532" }
] }));
app.get("/sse", async (req, res) => {
  const ip = req.ip ?? "unknown";
  if (!allowRequest(`sse:${ip}`)) { res.status(429).json({ error: "rate limit exceeded" }); return; }
  if (sessions.size >= maxSessions) { res.status(503).json({ error: "session capacity reached" }); return; }
  const transport = new SSEServerTransport("/messages", res);
  sessions.set(transport.sessionId, { transport, expiresAt: Date.now() + sessionTtlMs });
  transport.onclose = () => sessions.delete(transport.sessionId);
  const server = await createMcpServer();
  await server.connect(transport);
});
app.post("/messages", express.json({ limit: "64kb" }), async (req, res) => {
  const sessionId = typeof req.query.sessionId === "string" ? req.query.sessionId : "";
  const session = sessions.get(sessionId);
  if (!session || session.expiresAt <= Date.now()) { sessions.delete(sessionId); res.status(404).json({ error: "unknown MCP session" }); return; }
  await session.transport.handlePostMessage(req, res, req.body);
});
setInterval(() => { for (const [id, session] of sessions) if (session.expiresAt <= Date.now()) { void session.transport.close(); sessions.delete(id); } }, 60_000).unref();
export { app, fulfilled };
if (process.env.NODE_ENV !== "test") {
  app.listen(port, () => console.log(JSON.stringify({ event: "gateway_started", port, network: "eip155:84532" })));
}
