import assert from "node:assert/strict";
import test from "node:test";
import type { Hex } from "viem";
import { createRemoteSignerAccount } from "../src/remoteSigner.js";

test("remote signer delegates typed-data signing without exposing key material", async () => {
  const calls: Array<{ url: string; authorization: string | null; body: any }> = [];
  const signature = `0x${"11".repeat(65)}` as Hex;
  const account = createRemoteSignerAccount({
    address: "0x0000000000000000000000000000000000000042",
    signerUrl: "http://signer.internal",
    bearerToken: "signer-test-token",
    fetchImpl: async (input, init) => {
      calls.push({
        url: String(input),
        authorization: new Headers(init?.headers).get("authorization"),
        body: JSON.parse(String(init?.body)),
      });
      return new Response(JSON.stringify({ signature }), {
        status: 200,
        headers: { "content-type": "application/json" },
      });
    },
  });

  const result = await (account as any).signTypedData({
    domain: {
      name: "Agent account",
      version: "1",
      chainId: 11_155_111n,
      verifyingContract: "0x0000000000000000000000000000000000000043",
    },
    types: {
      Message: [{ name: "amount", type: "uint256" }],
    },
    primaryType: "Message",
    message: { amount: 123n },
  });

  assert.equal(result, signature);
  assert.equal(calls.length, 1);
  assert.equal(calls[0].url, "http://signer.internal/v1/signatures");
  assert.equal(calls[0].authorization, "Bearer signer-test-token");
  assert.equal(calls[0].body.operation, "signTypedData");
  assert.equal(calls[0].body.payload.domain.chainId, "11155111");
  assert.equal(calls[0].body.payload.message.amount, "123");
  assert.equal(JSON.stringify(calls[0].body).includes("private"), false);
});

test("remote signer rejects malformed signatures and raw transaction signing", async () => {
  const account = createRemoteSignerAccount({
    address: "0x0000000000000000000000000000000000000042",
    signerUrl: "http://signer.internal",
    bearerToken: "signer-test-token",
    fetchImpl: async () => new Response(JSON.stringify({ signature: "not-hex" }), {
      status: 200,
      headers: { "content-type": "application/json" },
    }),
  });

  await assert.rejects(
    (account as any).signMessage({ message: "hello" }),
    /invalid signature/,
  );
  await assert.rejects(
    (account as any).signTransaction({ to: "0x0000000000000000000000000000000000000043" }),
    /not authorized to sign raw transactions/,
  );
});

