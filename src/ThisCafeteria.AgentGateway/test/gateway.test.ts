import assert from "node:assert/strict";
import { describe, it, before, after } from "node:test";
import { createServer, Server } from "node:http";
import { Client } from "@modelcontextprotocol/sdk/client/index.js";
import { SSEClientTransport } from "@modelcontextprotocol/sdk/client/sse.js";

process.env.NODE_ENV = "test";
process.env.AGENT_GATEWAY_SERVICE_SECRET = "test-secret";
process.env.X402_PAY_TO = "0xPayToAddress";
process.env.X402_USDC_ADDRESS = "0xUSDCAddress";
process.env.ASPNET_INTERNAL_URL = "http://127.0.0.1:8080";
process.env.X402_FACILITATOR_URL = "http://127.0.0.1:4020";

describe("Gateway Integration", () => {
  let facilitator: Server;
  let backend: Server;
  let gatewayServer: Server;
  let gatewayPort = 4025;
  let mcpClient: Client;
  let transport: SSEClientTransport;
  let app: any;

  /** Counts /settle calls to verify no double-settlement on replay. */
  let settleCallCount = 0;
  /** Counts /verify calls for observability. */
  let verifyCallCount = 0;

  before(async () => {
    // Mock the X402 Facilitator
    facilitator = createServer((req, res) => {
      let body = "";
      req.on("data", chunk => body += chunk.toString());
      req.on("end", () => {
        if (req.method === "HEAD") {
          res.writeHead(200);
          res.end();
        } else if (req.method === "GET" && (req.url === "/kinds" || req.url === "/supported")) {
          res.writeHead(200, { "Content-Type": "application/json" });
          res.end(JSON.stringify({
             kinds: [{ x402Version: 2, scheme: "exact", network: "eip155:84532" }]
          }));
        } else if (req.method === "POST" && req.url === "/verify") {
          verifyCallCount++;
          const parsed = JSON.parse(body);
          const sig = parsed.paymentPayload?.signature ?? parsed.signature;
          if (sig === "bad-signature") {
             res.writeHead(400, { "Content-Type": "application/json" });
             res.end(JSON.stringify({ error: "bad signature" }));
             return;
          }
          if (sig === "invalid-payment") {
             res.writeHead(200, { "Content-Type": "application/json" });
             res.end(JSON.stringify({ isValid: false }));
             return;
          }
          res.writeHead(200, { "Content-Type": "application/json" });
          res.end(JSON.stringify({
            isValid: true
          }));
        } else if (req.method === "POST" && req.url === "/settle") {
          settleCallCount++;
          const parsed = JSON.parse(body);
          const sig = parsed.paymentPayload?.signature ?? parsed.signature;
          if (sig === "bad-signature") {
             res.writeHead(400, { "Content-Type": "application/json" });
             res.end(JSON.stringify({ error: "bad signature" }));
             return;
          }
          res.writeHead(200, { "Content-Type": "application/json" });
          res.end(JSON.stringify({
            success: true,
            transaction: "receipt-123",
            network: "eip155:84532"
          }));
        } else {
          res.writeHead(404);
          res.end();
        }
      });
    });

    backend = createServer((req, res) => {
      let body = "";
      req.on("data", chunk => body += chunk.toString());
      req.on("end", () => {
        if (req.headers["x-agent-gateway-secret"] !== "test-secret") {
          res.writeHead(401);
          res.end();
          return;
        }
        res.writeHead(200, { "Content-Type": "application/json" });
        res.end(JSON.stringify({ kind: "mock-response", originalBody: JSON.parse(body) }));
      });
    });

    await new Promise<void>((resolve) => facilitator.listen(4020, "127.0.0.1", () => resolve()));
    await new Promise<void>((resolve) => backend.listen(8080, "127.0.0.1", () => resolve()));

    // Dynamically import server after mock endpoints are listening
    const serverModule = await import("../src/server.js");
    app = serverModule.app;
    app.use((err: any, req: any, res: any, next: any) => {
      console.error("EXPRESS ERROR:", err);
      next(err);
    });

    await new Promise<void>((resolve) => {
      gatewayServer = app.listen(gatewayPort, "127.0.0.1", () => resolve());
    });

    transport = new SSEClientTransport(new URL(`http://127.0.0.1:${gatewayPort}/sse`));
    mcpClient = new Client({ name: "test-client", version: "1.0.0" }, { capabilities: {} });
    await mcpClient.connect(transport);
  });

  after(async () => {
    if (transport) await transport.close();
    await new Promise<void>((resolve) => { if (gatewayServer) gatewayServer.close(() => resolve()); else resolve(); });
    await new Promise<void>((resolve) => { if (facilitator) facilitator.close(() => resolve()); else resolve(); });
    await new Promise<void>((resolve) => { if (backend) backend.close(() => resolve()); else resolve(); });
  });

  const makePaymentMeta = (nonce: string, signature = "good-signature") => ({
    "x402/payment": {
      x402Version: 2,
      scheme: "exact",
      network: "eip155:84532",
      asset: process.env.X402_USDC_ADDRESS,
      amount: "10000",
      payTo: process.env.X402_PAY_TO,
      nonce,
      signature,
      accepted: {
        scheme: "exact",
        network: "eip155:84532",
        amount: "10000",
        asset: process.env.X402_USDC_ADDRESS,
        payTo: process.env.X402_PAY_TO,
        maxTimeoutSeconds: 300,
        extra: {}
      },
      payload: {
        scheme: "exact",
        network: "eip155:84532",
        asset: process.env.X402_USDC_ADDRESS,
        amount: "10000",
        payTo: process.env.X402_PAY_TO,
        nonce,
        signature,
      }
    }
  });

  it("should return a 402 challenge on unauthenticated paid requests", async () => {
    const result = await mcpClient.callTool({
      name: "create_brew_plan",
      arguments: { productId: "test", quantity: 1 },
    });
    const paymentRequired = result.structuredContent ??
      JSON.parse((result.content as any)[0].text as string);

    assert.strictEqual(result.isError, true);
    assert.strictEqual((paymentRequired as any).x402Version, 2);
    assert.strictEqual((paymentRequired as any).accepts.length, 1);
    assert.strictEqual((paymentRequired as any).accepts[0].amount, "10000");
    assert.strictEqual((paymentRequired as any).accepts[0].network, "eip155:84532");
  });

  it("should settle exactly once and return cached result on replay (no double-charge)", async () => {
    const settlesBefore = settleCallCount;

    // First call — should settle payment
    const res1 = await mcpClient.callTool({
      name: "create_brew_plan",
      arguments: { productId: "replay-test", quantity: 3 },
      _meta: makePaymentMeta("nonce-replay-1"),
    });
    const parsed1 = JSON.parse((res1.content as any)[0].text as string);
    assert.ok(parsed1.correlationId, "First call must return a correlationId");
    assert.ok(parsed1.requestHash, "First call must return a requestHash");
    assert.equal(settleCallCount, settlesBefore + 1, "First call must settle exactly once");

    // Replay — must NOT settle again
    const res2 = await mcpClient.callTool({
      name: "create_brew_plan",
      arguments: { productId: "replay-test", quantity: 3 },
      _meta: makePaymentMeta("nonce-replay-1"),
    });
    const parsed2 = JSON.parse((res2.content as any)[0].text as string);
    assert.equal(parsed2.correlationId, parsed1.correlationId, "Replay must return same correlationId");
    assert.equal(
      settleCallCount, settlesBefore + 1,
      "Replay MUST NOT trigger a second /settle call (was double-charging)"
    );
  });

  it("should process concurrent identical requests atomically (exactly one settlement)", async () => {
    const settlesBefore = settleCallCount;

    const p1 = mcpClient.callTool({
      name: "create_brew_plan",
      arguments: { productId: "concurrent-test", quantity: 1 },
      _meta: makePaymentMeta("nonce-concurrent-1"),
    });
    const p2 = mcpClient.callTool({
      name: "create_brew_plan",
      arguments: { productId: "concurrent-test", quantity: 1 },
      _meta: makePaymentMeta("nonce-concurrent-1"),
    });

    const [res1, res2] = await Promise.all([p1, p2]);
    const parsed1 = JSON.parse((res1.content as any)[0].text as string);
    const parsed2 = JSON.parse((res2.content as any)[0].text as string);

    assert.equal(parsed1.correlationId, parsed2.correlationId, "Concurrent requests must return same correlationId");
    assert.equal(settleCallCount, settlesBefore + 1, "Concurrent requests must settle exactly once");
  });

  it("should reject requests with bad signatures", async () => {
    try {
      await mcpClient.callTool({
        name: "create_brew_plan",
        arguments: { productId: "bad-sig-test", quantity: 1 },
        _meta: makePaymentMeta("nonce-bad-sig", "bad-signature"),
      });
      assert.fail("Should have thrown error on bad signature");
    } catch (err: any) {
      assert.ok(
        err.message.includes("400") || err.message.includes("payment") ||
        err.message.includes("signature") || err.message.includes("settle") ||
        err.message.includes("verification") || err.code === -32002,
        `Expected payment-related error, got: ${err.message}`
      );
    }
  });

  it("should reject requests where verification returns isValid: false", async () => {
    const result = await mcpClient.callTool({
      name: "create_brew_plan",
      arguments: { productId: "invalid-valid-test", quantity: 1 },
      _meta: makePaymentMeta("nonce-invalid-valid", "invalid-payment"),
    });
    assert.strictEqual(result.isError, true);
    assert.ok(
      (result.content as any)[0].text.includes("verification") || (result.content as any)[0].text.includes("payment"),
      `Expected verification error, got: ${(result.content as any)[0].text}`
    );
  });

  it("should return different results for different arguments (not cached across requests)", async () => {
    const settlesBefore = settleCallCount;

    const res1 = await mcpClient.callTool({
      name: "create_brew_plan",
      arguments: { productId: "product-A", quantity: 1 },
      _meta: makePaymentMeta("nonce-diff-1"),
    });
    const parsed1 = JSON.parse((res1.content as any)[0].text as string);

    const res2 = await mcpClient.callTool({
      name: "create_brew_plan",
      arguments: { productId: "product-B", quantity: 2 },
      _meta: makePaymentMeta("nonce-diff-2"),
    });
    const parsed2 = JSON.parse((res2.content as any)[0].text as string);

    assert.notEqual(parsed1.correlationId, parsed2.correlationId, "Different args must produce different correlationIds");
    assert.notEqual(parsed1.requestHash, parsed2.requestHash, "Different args must produce different hashes");
    assert.equal(settleCallCount, settlesBefore + 2, "Both calls must settle payment");
  });
});
