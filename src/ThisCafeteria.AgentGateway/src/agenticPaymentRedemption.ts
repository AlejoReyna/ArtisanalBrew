import { timingSafeEqual } from "node:crypto";
import express, { type Express } from "express";
import { NonceEnforcer } from "@metamask/delegation-abis";
import {
  Implementation,
  toMetaMaskSmartAccount,
  type Delegation,
  type DeleGatorEnvironment,
} from "@metamask/delegation-toolkit";
import {
  createPublicClient,
  defineChain,
  getAddress,
  http,
  keccak256,
  type Account,
  type Address,
  type Hex,
} from "viem";
import { createBundlerClient } from "viem/account-abstraction";
import { z } from "zod";
import {
  ENTRY_POINT_VERSION,
  FRAMEWORK_REVISION,
  MODULAR_ACCOUNT_TYPE,
  assertExactOneShotPermission,
  encodeRedemption,
  requireCompatibleBundler,
} from "./agenticPayments.js";
import {
  IdempotencyStore,
  requestHash,
  type AtomicStore,
} from "./requestBinding.js";

const addressSchema = z.string().regex(/^0x[0-9a-fA-F]{40}$/);
const hexSchema = z.string().regex(/^0x(?:[0-9a-fA-F]{2})*$/);
const quantitySchema = z.string().regex(/^(?:0|[1-9][0-9]*)$/);

const caveatSchema = z.object({
  enforcer: addressSchema,
  terms: hexSchema,
  args: hexSchema,
}).strict();

const delegationSchema = z.object({
  delegate: addressSchema,
  delegator: addressSchema,
  authority: z.string().regex(/^0x[0-9a-fA-F]{64}$/),
  caveats: z.array(caveatSchema).length(6),
  salt: hexSchema,
  signature: hexSchema,
}).strict();

export const redemptionRequestSchema = z.object({
  chainKey: z.enum(["ethereum-sepolia", "bsc-testnet"]),
  delegatorAddress: addressSchema,
  agentAddress: addressSchema,
  epoch: quantitySchema,
  validAfterUnix: z.number().int().nonnegative(),
  validBeforeUnix: z.number().int().positive(),
  permissions: z.array(z.object({
    delegation: delegationSchema,
    targetAddress: addressSchema,
    calldata: hexSchema.refine((value) => value.length >= 10, "calldata must include a selector"),
  }).strict()).min(1).max(8),
}).strict();

export type AgenticRedemptionRequest = z.infer<typeof redemptionRequestSchema>;

export type AgenticRedemptionResult = {
  chainKey: string;
  agentAddress: Address;
  userOperationHash: Hex;
  transactionHash: Hex;
  blockNumber: string;
};

export type AgenticPaymentChain = {
  chainKey: "ethereum-sepolia" | "bsc-testnet";
  chainId: number;
  displayName: string;
  nativeCurrency: { name: string; symbol: string; decimals: number };
  rpcUrl: string;
  bundlerUrl: string;
  bundlerMode: "safe" | "unsafe-local";
  environment: DeleGatorEnvironment;
  expectedCodeHashes?: Partial<Record<VerifiedContractName, Hex>>;
};

export type AgenticPaymentRedeemer = {
  preflight(): Promise<void>;
  redeem(request: AgenticRedemptionRequest): Promise<AgenticRedemptionResult>;
};

export type VerifiedContractName =
  | "EntryPoint"
  | "SimpleFactory"
  | "HybridDeleGatorImpl"
  | "DelegationManager"
  | "AllowedTargetsEnforcer"
  | "AllowedMethodsEnforcer"
  | "ExactCalldataEnforcer"
  | "LimitedCallsEnforcer"
  | "NonceEnforcer"
  | "TimestampEnforcer";

const normalize = (value: string) => value.toLowerCase();

const verifiedAddresses = (environment: DeleGatorEnvironment): Record<VerifiedContractName, Address> => ({
  EntryPoint: environment.EntryPoint as Address,
  SimpleFactory: environment.SimpleFactory as Address,
  HybridDeleGatorImpl: environment.implementations.HybridDeleGatorImpl as Address,
  DelegationManager: environment.DelegationManager as Address,
  AllowedTargetsEnforcer: environment.caveatEnforcers.AllowedTargetsEnforcer as Address,
  AllowedMethodsEnforcer: environment.caveatEnforcers.AllowedMethodsEnforcer as Address,
  ExactCalldataEnforcer: environment.caveatEnforcers.ExactCalldataEnforcer as Address,
  LimitedCallsEnforcer: environment.caveatEnforcers.LimitedCallsEnforcer as Address,
  NonceEnforcer: environment.caveatEnforcers.NonceEnforcer as Address,
  TimestampEnforcer: environment.caveatEnforcers.TimestampEnforcer as Address,
});

export async function verifyRuntimeBytecode(
  chain: AgenticPaymentChain,
  getCode: (address: Address) => Promise<Hex | undefined>,
): Promise<void> {
  const expected = chain.expectedCodeHashes;
  if (!expected) throw new Error(`Trusted runtime bytecode hashes are missing for '${chain.chainKey}'`);
  for (const [name, address] of Object.entries(verifiedAddresses(chain.environment)) as [VerifiedContractName, Address][]) {
    const expectedHash = expected[name];
    if (!expectedHash) throw new Error(`Trusted runtime bytecode hash '${name}' is missing for '${chain.chainKey}'`);
    const code = await getCode(address);
    if (!code || code === "0x") throw new Error(`${name} is not deployed at ${address} on '${chain.chainKey}'`);
    const actualHash = keccak256(code);
    if (normalize(actualHash) !== normalize(expectedHash)) {
      throw new Error(`${name} runtime bytecode hash mismatch on '${chain.chainKey}'`);
    }
  }
}

export function createAgenticPaymentRedeemer(options: {
  chains: readonly AgenticPaymentChain[];
  signer: Account;
  deploySalt: Hex;
  now?: () => number;
  allowUnsafeBundlerForLocalE2e?: boolean;
  skipBytecodeVerificationForLocalE2e?: boolean;
}): AgenticPaymentRedeemer {
  const chains = new Map(options.chains.map((chain) => [chain.chainKey, chain]));
  const now = options.now ?? (() => Math.floor(Date.now() / 1_000));
  const preflightChecks = new Map<string, Promise<void>>();

  const preflightChain = (chainConfig: AgenticPaymentChain) => {
    let check = preflightChecks.get(chainConfig.chainKey);
    if (check) return check;
    check = (async () => {
      const expectedChainId = chainConfig.chainKey === "ethereum-sepolia" ? 11_155_111 : 97;
      if (chainConfig.chainId !== expectedChainId && !options.allowUnsafeBundlerForLocalE2e) {
        throw new Error(`Configured chain ID does not match '${chainConfig.chainKey}'`);
      }
      if (chainConfig.bundlerMode !== "safe" && !options.allowUnsafeBundlerForLocalE2e) {
        throw new Error("Agentic redemption requires a safe-mode bundler");
      }
      const chain = defineChain({
        id: chainConfig.chainId,
        name: chainConfig.displayName,
        nativeCurrency: chainConfig.nativeCurrency,
        rpcUrls: { default: { http: [chainConfig.rpcUrl] } },
      });
      const publicClient = createPublicClient({ chain, transport: http(chainConfig.rpcUrl) });
      if (!options.skipBytecodeVerificationForLocalE2e) {
        await verifyRuntimeBytecode(
          chainConfig,
          (address) => publicClient.getCode({ address }),
        );
      }
      await requireCompatibleBundler({
        accountType: MODULAR_ACCOUNT_TYPE,
        frameworkRevision: FRAMEWORK_REVISION,
        entryPointVersion: ENTRY_POINT_VERSION,
        entryPoint: chainConfig.environment.EntryPoint as Address,
        bundlerUrl: chainConfig.bundlerUrl,
        environment: chainConfig.environment,
      });
    })();
    preflightChecks.set(chainConfig.chainKey, check);
    return check;
  };

  return {
    async preflight() {
      await Promise.all(options.chains.map(preflightChain));
    },
    async redeem(rawRequest) {
      const request = redemptionRequestSchema.parse(rawRequest);
      const chainConfig = chains.get(request.chainKey);
      if (!chainConfig) throw new Error(`Agentic redemption is not configured for '${request.chainKey}'`);
      await preflightChain(chainConfig);
      if (request.validAfterUnix >= request.validBeforeUnix) throw new Error("Permission validity window is invalid");

      const currentTime = now();
      if (currentTime < request.validAfterUnix || currentTime >= request.validBeforeUnix) {
        throw new Error("Permission is outside its active timestamp window");
      }

      const chain = defineChain({
        id: chainConfig.chainId,
        name: chainConfig.displayName,
        nativeCurrency: chainConfig.nativeCurrency,
        rpcUrls: { default: { http: [chainConfig.rpcUrl] } },
      });
      const publicClient = createPublicClient({ chain, transport: http(chainConfig.rpcUrl) });

      const agentAccount = await toMetaMaskSmartAccount({
        client: publicClient,
        implementation: Implementation.Hybrid,
        deployParams: [options.signer.address, [], [], []],
        deploySalt: options.deploySalt,
        signer: { account: options.signer },
        environment: chainConfig.environment,
      });
      const agentAddress = await agentAccount.getAddress();
      if (normalize(agentAddress) !== normalize(request.agentAddress)) {
        throw new Error("Permission delegate does not match the configured agent account");
      }

      const epoch = BigInt(request.epoch);
      const delegatorAddress = getAddress(request.delegatorAddress);
      const activeEpoch = await publicClient.readContract({
        address: chainConfig.environment.caveatEnforcers.NonceEnforcer as Address,
        abi: NonceEnforcer.abi,
        functionName: "currentNonce",
        args: [chainConfig.environment.DelegationManager as Address, delegatorAddress],
      });
      if (activeEpoch !== epoch) {
        throw new Error(`Permission epoch ${request.epoch} is not active on-chain`);
      }

      const permissions = request.permissions.map((item) => {
        const exactPermission = {
          delegator: delegatorAddress,
          agent: agentAddress,
          target: getAddress(item.targetAddress),
          calldata: item.calldata as Hex,
          epoch,
          validAfter: request.validAfterUnix,
          validBefore: request.validBeforeUnix,
          salt: item.delegation.salt as Hex,
        };
        assertExactOneShotPermission(
          chainConfig.environment,
          exactPermission,
          item.delegation as Delegation,
        );
        return {
          delegation: item.delegation as Delegation,
          target: exactPermission.target,
          calldata: exactPermission.calldata,
        };
      });

      const redemption = encodeRedemption(chainConfig.environment, permissions);
      const bundler = createBundlerClient({
        client: publicClient,
        transport: http(chainConfig.bundlerUrl),
      });
      const userOperationHash = await bundler.sendUserOperation({
        account: agentAccount,
        calls: [redemption],
      });
      const receipt = await bundler.waitForUserOperationReceipt({
        hash: userOperationHash,
        timeout: 60_000,
      });
      if (!receipt.success || normalize(receipt.sender) !== normalize(agentAddress)) {
        throw new Error(`Agent UserOperation ${userOperationHash} failed`);
      }

      const transaction = await publicClient.getTransactionReceipt({
        hash: receipt.receipt.transactionHash,
      });
      if (transaction.status !== "success") {
        throw new Error(`Agent UserOperation transaction ${transaction.transactionHash} reverted`);
      }

      return {
        chainKey: request.chainKey,
        agentAddress,
        userOperationHash,
        transactionHash: transaction.transactionHash,
        blockNumber: transaction.blockNumber.toString(),
      };
    },
  };
}

function tokenMatches(provided: string | undefined, expected: string): boolean {
  if (!provided) return false;
  const providedBytes = Buffer.from(provided);
  const expectedBytes = Buffer.from(expected);
  return providedBytes.length === expectedBytes.length && timingSafeEqual(providedBytes, expectedBytes);
}

export function createAgenticPaymentHttpApp(options: {
  redeemer: AgenticPaymentRedeemer;
  apiToken: string;
  receipts?: AtomicStore<{
    requestHash: string;
    request: AgenticRedemptionRequest;
    result: AgenticRedemptionResult;
  }>;
}): Express {
  if (!options.apiToken) throw new Error("Agentic redemption API token is required");
  const receipts = options.receipts ?? new IdempotencyStore(24 * 60 * 60_000, 10_000);
  const app = express();
  app.post("/agentic-payments/redeem", express.json({ limit: "128kb" }), async (request, response) => {
    const authorization = request.header("authorization");
    const bearer = authorization?.startsWith("Bearer ") ? authorization.slice(7) : undefined;
    if (!tokenMatches(bearer, options.apiToken)) {
      response.status(401).json({ error: "unauthorized" });
      return;
    }

    const idempotencyKey = request.header("idempotency-key");
    if (!idempotencyKey || !/^[A-Za-z0-9._:-]{16,128}$/.test(idempotencyKey)) {
      response.status(400).json({ error: "invalid_idempotency_key" });
      return;
    }

    const parsed = redemptionRequestSchema.safeParse(request.body);
    if (!parsed.success) {
      response.status(400).json({ error: "invalid_request", issues: parsed.error.issues });
      return;
    }

    try {
      const fingerprint = requestHash(
        "POST",
        "/agentic-payments/redeem",
        parsed.data,
        { authority: "signed-delegation" },
      );
      const stored = await receipts.executeAtomic(`redemption:${idempotencyKey}`, async () => ({
        requestHash: fingerprint,
        request: parsed.data,
        result: await options.redeemer.redeem(parsed.data),
      }));
      if (stored.requestHash !== fingerprint) {
        response.status(409).json({ error: "idempotency_key_reused" });
        return;
      }
      response.status(200).json({ ...stored.result, replay: stored.replay === true });
    } catch (error) {
      const message = error instanceof Error ? error.message : "Agentic redemption failed";
      response.status(422).json({ error: "redemption_rejected", message });
    }
  });
  return app;
}
