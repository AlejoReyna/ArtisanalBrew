import { randomUUID } from "node:crypto";
import express from "express";
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { SSEServerTransport } from "@modelcontextprotocol/sdk/server/sse.js";
import { HTTPFacilitatorClient } from "@x402/core/server";
import { ExactEvmScheme } from "@x402/evm/exact/server";
import { createPaymentWrapper, x402ResourceServer } from "@x402/mcp";
import { z } from "zod";
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
const fulfilled = new IdempotencyStore<{ correlationId: string; result: unknown }>(15 * 60_000, 10_000);
const requests = new Map<string, { count: number; resetAt: number }>();

if (!gatewaySecret || !payTo || !usdc) {
  throw new Error("AGENT_GATEWAY_SERVICE_SECRET, X402_PAY_TO, and X402_USDC_ADDRESS are required");
}

const facilitator = new HTTPFacilitatorClient({ url: facilitatorUrl });
const resourceServer = new x402ResourceServer(facilitator);
resourceServer.register("eip155:84532", new ExactEvmScheme());
await resourceServer.initialize();

const payment = async (price: string) => createPaymentWrapper(resourceServer, {
  accepts: await resourceServer.buildPaymentRequirements({ scheme: "exact", network: "eip155:84532", payTo, price: { asset: usdc, amount: price } }),
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

const createMcpServer = async () => {
const mcpServer = new McpServer({ name: "ArtisanalBrew Agent Gateway", version: "0.1.0" });
mcpServer.tool("search_products", "Search the ArtisanalBrew catalog (free).", { query: z.string().max(120) }, async ({ query }) => ({
  content: [{ type: "text", text: JSON.stringify(await callInternal("/internal/agent/resources/search-products", { query }, randomUUID())) }],
}));

const paidTool = async (name: string, description: string, price: string, route: string, schema: z.ZodRawShape) => {
  const paid = await payment(price);
  mcpServer.tool(name, description, schema, paid(async (args) => {
    const correlationId = randomUUID();
    const hash = requestHash("POST", route, args);
    const cached = fulfilled.get(hash);
    if (cached) return { content: [{ type: "text", text: JSON.stringify({ correlationId: cached.correlationId, requestHash: hash, result: cached.result, replay: true }) }] };
    const result = await callInternal(route, { ...args, requestHash: hash }, correlationId);
    fulfilled.set(hash, { correlationId, result });
    return { content: [{ type: "text", text: JSON.stringify({ correlationId, requestHash: hash, result }) }] };
  }));
};

await paidTool("create_brew_plan", "Create a structured brew plan; 0.01 test USDC.", "10000", "/internal/agent/resources/brew-plan", { productId: z.string().max(64), quantity: z.number().int().positive().max(1000) });
await paidTool("get_provenance_report", "Get a provenance report; 0.02 test USDC.", "20000", "/internal/agent/resources/provenance", { productId: z.string().max(64) });
await paidTool("request_wholesale_quote", "Request a wholesale quote; 0.02 test USDC.", "20000", "/internal/agent/resources/wholesale-quote", { productId: z.string().max(64), quantity: z.number().int().positive().max(10000) });
return mcpServer;
};

const app = express();
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
  await transport.start();
});
app.post("/messages", express.json({ limit: "64kb" }), async (req, res) => {
  const sessionId = typeof req.query.sessionId === "string" ? req.query.sessionId : "";
  const session = sessions.get(sessionId);
  if (!session || session.expiresAt <= Date.now()) { sessions.delete(sessionId); res.status(404).json({ error: "unknown MCP session" }); return; }
  await session.transport.handlePostMessage(req, res, req.body);
});
setInterval(() => { for (const [id, session] of sessions) if (session.expiresAt <= Date.now()) { void session.transport.close(); sessions.delete(id); } }, 60_000).unref();
app.listen(port, () => console.log(JSON.stringify({ event: "gateway_started", port, network: "eip155:84532" })));
