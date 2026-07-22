/**
 * Proves the browser-bundled delegation toolkit (js/delegation-toolkit.iife.min.js,
 * consumed by wwwroot/js/smartAccountRegistration.js) computes the exact same
 * counterfactual HybridDeleGator address as the untouched, directly-imported
 * @metamask/delegation-toolkit package - against a live local Hardhat node, not a
 * stub. It also runs the *actual* smartAccountRegistration.js file shipped to
 * the browser, not a reimplementation, so a regression in that file fails this
 * check.
 *
 * Requires a running node with the modular stack deployed:
 *   npx hardhat node --chain-id 31337 &
 *   npm run deploy:local
 *   npm run build:browser-delegation-bundle
 *   npx tsx scripts/browser-bundle-address-check.ts
 */
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import {
  Implementation,
  toMetaMaskSmartAccount,
  type DeleGatorEnvironment
} from "@metamask/delegation-toolkit";
import { createPublicClient, createWalletClient, http, toHex, type Address } from "viem";
import manifest from "../deployments/evm-local.json" with { type: "json" };

const RPC_URL = process.env.RPC_URL ?? "http://127.0.0.1:8545";
const OWNER = "0x70997970C51812dc3A010C7d01b50e0d17dc79C8" as Address; // Hardhat's well-known account #1
const DEPLOY_SALT = 0n;

const environment: DeleGatorEnvironment = {
  DelegationManager: manifest.addresses.delegationManager as Address,
  EntryPoint: manifest.addresses.entryPoint as Address,
  SimpleFactory: manifest.addresses.modularSimpleFactory as Address,
  implementations: { HybridDeleGatorImpl: manifest.addresses.hybridDeleGatorImplementation as Address },
  caveatEnforcers: {
    AllowedTargetsEnforcer: manifest.addresses.allowedTargetsEnforcer as Address,
    AllowedMethodsEnforcer: manifest.addresses.allowedMethodsEnforcer as Address,
    ExactCalldataEnforcer: manifest.addresses.exactCalldataEnforcer as Address,
    LimitedCallsEnforcer: manifest.addresses.limitedCallsEnforcer as Address,
    NonceEnforcer: manifest.addresses.nonceEnforcer as Address,
    TimestampEnforcer: manifest.addresses.timestampEnforcer as Address
  }
};

// --- Reference: the untouched, directly-imported SDK, exactly as the Hardhat
// acceptance scripts already use it. ---
const chain = {
  id: manifest.chainId,
  name: "evm-local",
  nativeCurrency: { name: "Local Ether", symbol: "ETH", decimals: 18 },
  rpcUrls: { default: { http: [RPC_URL] } }
};
const referenceClient = createPublicClient({ chain, transport: http(RPC_URL) });
const referenceWalletClient = createWalletClient({ account: OWNER, chain, transport: http(RPC_URL) });
const referenceAccount = await toMetaMaskSmartAccount({
  client: referenceClient,
  implementation: Implementation.Hybrid,
  deployParams: [OWNER, [], [], []],
  deploySalt: toHex(DEPLOY_SALT, { size: 32 }),
  signer: { walletClient: referenceWalletClient },
  environment
});
const referenceAddress = await referenceAccount.getAddress();
console.log(`REFERENCE_ADDRESS=${referenceAddress}`);

// --- Under test: the real, shipped browser bundle + the real, shipped
// smartAccountRegistration.js, run inside a Node vm context standing in for a
// browser tab (window.ethereum proxies raw JSON-RPC to the same live node -
// exactly what MetaMask itself does). ---
const bundleCode = readFileSync(
  new URL("../../../src/ThisCafeteria.Web/wwwroot/js/delegation-toolkit.iife.min.js", import.meta.url),
  "utf8"
);
const moduleCode = readFileSync(
  new URL("../../../src/ThisCafeteria.Web/wwwroot/js/smartAccountRegistration.js", import.meta.url),
  "utf8"
).replace("export async function deriveModularAccountAddress", "globalThis.deriveModularAccountAddress = async function deriveModularAccountAddress");

let requestId = 0;
const fakeEthereum = {
  request: async ({ method, params }: { method: string; params?: unknown[] }) => {
    requestId += 1;
    const response = await fetch(RPC_URL, {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ jsonrpc: "2.0", id: requestId, method, params: params ?? [] })
    });
    const body = await response.json() as { result?: unknown; error?: { message: string } };
    if (body.error) throw new Error(`RPC ${method} failed: ${body.error.message}`);
    return body.result;
  }
};

const context = vm.createContext({ fetch, TextEncoder, TextDecoder, console, BigInt, URL });
(context as Record<string, unknown>).window = context;
(context as Record<string, unknown>).ethereum = fakeEthereum;

vm.runInContext(bundleCode, context, { filename: "delegation-toolkit.iife.min.js" });
assert.ok((context as Record<string, unknown>).MetaMaskDelegationToolkit, "bundle must attach window.MetaMaskDelegationToolkit");

vm.runInContext(moduleCode, context, { filename: "smartAccountRegistration.js" });
const deriveModularAccountAddress = (context as Record<string, unknown>).deriveModularAccountAddress as (
  args: Record<string, unknown>
) => Promise<{ address: string; deploySalt: string }>;

const derived = await deriveModularAccountAddress({
  ownerAddress: OWNER,
  chainIdHex: toHex(manifest.chainId),
  entryPoint: environment.EntryPoint,
  delegationManager: environment.DelegationManager,
  factory: environment.SimpleFactory,
  hybridImplementation: environment.implementations.HybridDeleGatorImpl,
  allowedTargetsEnforcer: environment.caveatEnforcers.AllowedTargetsEnforcer,
  allowedMethodsEnforcer: environment.caveatEnforcers.AllowedMethodsEnforcer,
  exactCalldataEnforcer: environment.caveatEnforcers.ExactCalldataEnforcer,
  limitedCallsEnforcer: environment.caveatEnforcers.LimitedCallsEnforcer,
  nonceEnforcer: environment.caveatEnforcers.NonceEnforcer,
  timestampEnforcer: environment.caveatEnforcers.TimestampEnforcer,
  deploySalt: DEPLOY_SALT.toString()
});
console.log(`BUNDLE_DERIVED_ADDRESS=${derived.address}`);

assert.equal(derived.address.toLowerCase(), referenceAddress.toLowerCase(),
  "the shipped browser bundle + smartAccountRegistration.js must derive the identical counterfactual address as the untouched SDK");
assert.equal(derived.deploySalt, DEPLOY_SALT.toString());

console.log("BROWSER_BUNDLE_ADDRESS_CHECK=PASS");
