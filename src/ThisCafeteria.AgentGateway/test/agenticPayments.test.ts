import assert from "node:assert/strict";
import test from "node:test";
import type { Address } from "viem";
import type { DeleGatorEnvironment } from "@metamask/delegation-toolkit";
import {
  ENTRY_POINT_VERSION,
  FRAMEWORK_REVISION,
  MODULAR_ACCOUNT_TYPE,
  encodeRedemption,
  requireCompatibleBundler,
} from "../src/agenticPayments.js";

const entryPoint = "0x0000000071727De22E5E9d8BAf0edAc6f37da032" as Address;
const otherEntryPoint = "0x0000000000000000000000000000000000000001" as Address;
const environment = {
  EntryPoint: entryPoint,
  DelegationManager: "0x0000000000000000000000000000000000000002",
  caveatEnforcers: { NonceEnforcer: "0x0000000000000000000000000000000000000003" },
} as unknown as DeleGatorEnvironment;

test("modular client rejects an account bound to a different EntryPoint before network use", async () => {
  await assert.rejects(requireCompatibleBundler({
    accountType: MODULAR_ACCOUNT_TYPE,
    frameworkRevision: FRAMEWORK_REVISION,
    entryPointVersion: ENTRY_POINT_VERSION,
    entryPoint: otherEntryPoint,
    bundlerUrl: "http://127.0.0.1:1",
    environment,
  }), /different EntryPoint/);
});

test("modular client fails closed for an unknown account type", async () => {
  await assert.rejects(requireCompatibleBundler({
    accountType: "unknown-account" as typeof MODULAR_ACCOUNT_TYPE,
    frameworkRevision: FRAMEWORK_REVISION,
    entryPointVersion: ENTRY_POINT_VERSION,
    entryPoint,
    bundlerUrl: "http://127.0.0.1:1",
    environment,
  }), /Unknown modular account type/);
});

test("redemption builder rejects an empty permission set", () => {
  assert.throws(() => encodeRedemption(environment, []), /At least one exact permission/);
});
