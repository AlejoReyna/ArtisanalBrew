import assert from "node:assert/strict";
import { describe, it } from "node:test";
import { IdempotencyStore, requestHash } from "../src/requestBinding.js";

describe("gateway request binding", () => {
  it("hashes object key order canonically", () => {
    assert.equal(requestHash("post", "/quote", { quantity: 2, productId: "coffee" }), requestHash("POST", "/quote", { productId: "coffee", quantity: 2 }));
  });

  it("returns the original result and bounds retained entries", () => {
    const store = new IdempotencyStore<string>(60_000, 1);
    store.set("first", "original");
    assert.equal(store.get("first"), "original");
    store.set("second", "second");
    assert.equal(store.get("first"), undefined);
    assert.equal(store.get("second"), "second");
  });
});
