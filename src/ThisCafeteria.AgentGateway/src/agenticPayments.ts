import {
  ExecutionMode,
  ROOT_AUTHORITY,
  contracts,
  createDelegation,
  createExecution,
  type Delegation,
  type DeleGatorEnvironment,
  type MetaMaskSmartAccount,
} from "@metamask/delegation-toolkit";
import { toHex, type Address, type Hex } from "viem";

export const MODULAR_ACCOUNT_TYPE = "metamask-hybrid-delegator-v1.3.0" as const;
export const FRAMEWORK_REVISION = "bfbdf9795a976833ed2fa000baf42fbb83958b03" as const;
export const ENTRY_POINT_VERSION = "0.7" as const;

export type ModularAccountManifest = {
  accountType: typeof MODULAR_ACCOUNT_TYPE;
  frameworkRevision: typeof FRAMEWORK_REVISION;
  entryPointVersion: typeof ENTRY_POINT_VERSION;
  entryPoint: Address;
  bundlerUrl: string;
  environment: DeleGatorEnvironment;
};

/**
 * Fail closed before constructing a UserOperation. Contract caveats remain the
 * authorization boundary; this check only prevents sending to a wrong service.
 */
export async function requireCompatibleBundler(manifest: ModularAccountManifest): Promise<void> {
  if (manifest.accountType !== MODULAR_ACCOUNT_TYPE) throw new Error("Unknown modular account type");
  if (manifest.frameworkRevision !== FRAMEWORK_REVISION) throw new Error("Unsupported modular framework revision");
  if (manifest.entryPointVersion !== ENTRY_POINT_VERSION) throw new Error("Unsupported EntryPoint version");
  if (manifest.environment.EntryPoint.toLowerCase() !== manifest.entryPoint.toLowerCase()) {
    throw new Error("Configured modular account is bound to a different EntryPoint");
  }
  if (!manifest.bundlerUrl) throw new Error("A v0.7 bundler is required");

  const response = await fetch(manifest.bundlerUrl, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ jsonrpc: "2.0", id: 1, method: "eth_supportedEntryPoints", params: [] }),
    signal: AbortSignal.timeout(5_000),
  });
  if (!response.ok) throw new Error(`Bundler discovery failed with HTTP ${response.status}`);
  const body = await response.json() as { result?: string[]; error?: unknown };
  if (body.error || !body.result?.some((address) => address.toLowerCase() === manifest.entryPoint.toLowerCase())) {
    throw new Error("Bundler does not support the configured EntryPoint");
  }
}

export type ExactPermission = {
  delegator: Address;
  agent: Address;
  target: Address;
  calldata: Hex;
  epoch: bigint;
  validAfter: number;
  validBefore: number;
  salt: Hex;
};

function buildExactOneShotPermission(
  environment: DeleGatorEnvironment,
  permission: ExactPermission,
): Delegation {
  return createDelegation({
    environment,
    to: permission.agent,
    from: permission.delegator,
    salt: permission.salt,
    scope: {
      type: "functionCall",
      targets: [permission.target],
      selectors: [permission.calldata.slice(0, 10) as Hex],
      exactCalldata: { calldata: permission.calldata },
    },
    caveats: [
      { type: "nonce", nonce: toHex(permission.epoch, { size: 32 }) },
      { type: "timestamp", afterThreshold: permission.validAfter, beforeThreshold: permission.validBefore },
      { type: "limitedCalls", limit: 1 },
    ],
  });
}

/**
 * Compose only published Delegation Framework scopes/caveats. Exact calldata
 * binds target, selector, token/recipient arguments and amount; one permitted
 * call makes that amount the delegation's cumulative quota.
 */
export async function signExactOneShotPermission(
  ownerAccount: MetaMaskSmartAccount,
  environment: DeleGatorEnvironment,
  permission: ExactPermission,
): Promise<Delegation> {
  const unsigned = buildExactOneShotPermission(environment, permission);
  const { signature: _signature, ...signable } = unsigned;
  return { ...unsigned, signature: await ownerAccount.signDelegation({ delegation: signable }) };
}

const normalizeDelegation = (delegation: Delegation): Delegation => ({
  ...delegation,
  delegate: delegation.delegate.toLowerCase() as Hex,
  delegator: delegation.delegator.toLowerCase() as Hex,
  authority: delegation.authority.toLowerCase() as Hex,
  caveats: delegation.caveats.map((caveat) => ({
    enforcer: caveat.enforcer.toLowerCase() as Hex,
    terms: caveat.terms.toLowerCase() as Hex,
    args: caveat.args.toLowerCase() as Hex,
  })),
  salt: delegation.salt.toLowerCase() as Hex,
  signature: delegation.signature.toLowerCase() as Hex,
});

/**
 * Rebuild the only delegation shape this gateway is willing to redeem and
 * compare every signed field. The on-chain DelegationManager remains the
 * signature/caveat authority; this is an additional fail-closed gateway gate.
 */
export function assertExactOneShotPermission(
  environment: DeleGatorEnvironment,
  permission: ExactPermission,
  signedDelegation: Delegation,
): void {
  if (!signedDelegation.signature || signedDelegation.signature === "0x") {
    throw new Error("Permission delegation is not signed");
  }

  const expected = normalizeDelegation(buildExactOneShotPermission(environment, permission));
  const received = normalizeDelegation(signedDelegation);
  const expectedUnsigned = { ...expected, signature: "0x" as Hex };
  const receivedUnsigned = { ...received, signature: "0x" as Hex };

  if (received.authority !== ROOT_AUTHORITY.toLowerCase()) {
    throw new Error("Permission must use the root delegation authority");
  }
  if (JSON.stringify(receivedUnsigned) !== JSON.stringify(expectedUnsigned)) {
    throw new Error("Signed delegation does not match the exact one-shot permission");
  }
}

export function encodePermissionEpochChange(environment: DeleGatorEnvironment): { to: Address; data: Hex } {
  return {
    to: environment.caveatEnforcers.NonceEnforcer,
    data: contracts.NonceEnforcer.encode.incrementNonce(environment.DelegationManager),
  };
}

export function encodeRedemption(
  environment: DeleGatorEnvironment,
  permissions: readonly { delegation: Delegation; target: Address; calldata: Hex }[],
): { to: Address; data: Hex } {
  if (permissions.length === 0) throw new Error("At least one exact permission is required");
  return {
    to: environment.DelegationManager,
    data: contracts.DelegationManager.encode.redeemDelegations({
      delegations: permissions.map(({ delegation }) => [delegation]),
      modes: permissions.map(() => ExecutionMode.SingleDefault),
      executions: permissions.map(({ target, calldata }) => [createExecution({ target, callData: calldata })]),
    }),
  };
}
