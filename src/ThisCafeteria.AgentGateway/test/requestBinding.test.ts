import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { IdempotencyStore, requestHash } from "../src/requestBinding.js";

describe("gateway request binding", () => {
  it("hashes object key order canonically", () => {
    const payment = { payer: "a" };
    assert.equal(requestHash("post", "/quote", { quantity: 2, productId: "coffee" }, payment), requestHash("POST", "/quote", { productId: "coffee", quantity: 2 }, payment));
  });

  it("returns the original result and bounds retained entries", async () => {
    const store = new IdempotencyStore<{ result: string }>(60_000, 1);
    await store.executeAtomic("first", async () => ({ result: "original" }));
    
    const secondCall = await store.executeAtomic("first", async () => ({ result: "second" }));
    assert.equal(secondCall.result, "original");
    assert.equal(secondCall.replay, true);

    await store.executeAtomic("second", async () => ({ result: "second" }));
    const val = await store.executeAtomic("first", async () => ({ result: "third" }));
    assert.equal(val.result, "third");
    assert.equal(val.replay, undefined);
  });

  it("releasing key on failed request", async () => {
    const store = new IdempotencyStore<{ result: string }>(60_000, 2);
    let called = 0;
    try {
      await store.executeAtomic("fail", async () => {
        called++;
        throw new Error("Failed");
      });
    } catch (e) {}
    
    const retry = await store.executeAtomic("fail", async () => {
      called++;
      return { result: "success" };
    });
    
    assert.equal(retry.result, "success");
    assert.equal(retry.replay, undefined);
    assert.equal(called, 2);
  });

  it("concurrent callers share one promise", async () => {
    const store = new IdempotencyStore<{ result: string }>(60_000, 2);
    let executions = 0;
    const task = async () => {
      await new Promise(resolve => setTimeout(resolve, 10));
      executions++;
      return { result: "done" };
    };

    const results = await Promise.all([
      store.executeAtomic("concurrent", task),
      store.executeAtomic("concurrent", task),
      store.executeAtomic("concurrent", task)
    ]);

    assert.equal(executions, 1);
    assert.equal(results[0].result, "done");
    assert.equal(results[1].result, "done");
    assert.equal(results[1].replay, true);
    assert.equal(results[2].replay, true);
  });

  it("produces different hashes when payment metadata changes", () => {
    const basePayment = { payer: "a", nonce: "1", signature: "s", network: "n", asset: "usd", amount: "10", recipient: "b", expiry: "100" };
    const baseHash = requestHash("POST", "/test", {}, basePayment);
    
    assert.notEqual(requestHash("POST", "/test", {}, { ...basePayment, payer: "b" }), baseHash);
    assert.notEqual(requestHash("POST", "/test", {}, { ...basePayment, nonce: "2" }), baseHash);
    assert.notEqual(requestHash("POST", "/test", {}, { ...basePayment, signature: "t" }), baseHash);
    assert.notEqual(requestHash("POST", "/test", {}, { ...basePayment, network: "m" }), baseHash);
    assert.notEqual(requestHash("POST", "/test", {}, { ...basePayment, asset: "eur" }), baseHash);
    assert.notEqual(requestHash("POST", "/test", {}, { ...basePayment, amount: "11" }), baseHash);
    assert.notEqual(requestHash("POST", "/test", {}, { ...basePayment, recipient: "c" }), baseHash);
    assert.notEqual(requestHash("POST", "/test", {}, { ...basePayment, expiry: "200" }), baseHash);
  });
});
