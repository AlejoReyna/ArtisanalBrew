/**
 * Signs a scoped agent session-key delegation and the owner's activation/revocation
 * UserOperation in the browser, against the connected wallet. Mirrors, exactly,
 * contracts/evm/scripts/metamask-session-key-e2e.ts (unsignedPermission/signPermission,
 * roughly lines 173-201) and src/ThisCafeteria.AgentGateway/src/agenticPayments.ts
 * (encodePermissionEpochChange): same enforcer set (nonce/timestamp/one-shot
 * limitedCalls), same NonceEnforcer.currentNonce epoch binding read live through the
 * public RPC, same incrementNonce activation/revocation call. Depends on
 * window.MetaMaskDelegationToolkit (see js/delegation-toolkit.iife.min.js) exactly
 * like smartAccountRegistration.js.
 *
 * This never broadcasts anything itself - the caller (SmartAccountPanel.razor) submits
 * the signed UserOperation this returns via ISmartAccountService.SubmitOwnerUserOperationAsync
 * only after explicit user confirmation. Gas figures come from the bundler's own
 * eth_estimateUserOperationGas (relayed through a .NET callback, since the browser never
 * has the bundler URL - see ISmartAccountService.EstimateUserOperationGasAsync), never a
 * client-guessed number.
 */
import { ESCROW_ABI, ERC20_ABI } from "./agenticCommerce.js";

const NONCE_ENFORCER_ABI = [
    {
        inputs: [
            { internalType: "address", name: "delegationManager", type: "address" },
            { internalType: "address", name: "delegator", type: "address" }
        ],
        name: "currentNonce",
        outputs: [{ internalType: "uint256", name: "", type: "uint256" }],
        stateMutability: "view",
        type: "function"
    }
];

/** One-time delegation salts, matching metamask-session-key-e2e.ts's own arbitrary choice. */
const APPROVE_DELEGATION_SALT = 11n;
const FUND_DELEGATION_SALT = 12n;

export async function buildActivationPayload({
    ownerAddress,
    chainIdHex,
    entryPoint,
    delegationManager,
    factory,
    hybridImplementation,
    allowedTargetsEnforcer,
    allowedMethodsEnforcer,
    exactCalldataEnforcer,
    limitedCallsEnforcer,
    nonceEnforcer,
    timestampEnforcer,
    deploySalt,
    agentAddress,
    escrowAddress,
    tokenAddress,
    jobId,
    amountDecimal,
    validForSeconds,
    dotNetRef
}) {
    const toolkit = requireToolkit();
    const { toMetaMaskSmartAccount, Implementation, createPublicClient, createWalletClient, custom, toHex, createDelegation, contracts, encodeFunctionData, parseUnits, getDelegationHashOffchain } = toolkit;

    const { account, publicClient, environment, delegatorAddress } = await buildAccount({
        toMetaMaskSmartAccount, Implementation, createPublicClient, createWalletClient, custom, toHex,
        ownerAddress, chainIdHex, entryPoint, delegationManager, factory, hybridImplementation,
        allowedTargetsEnforcer, allowedMethodsEnforcer, exactCalldataEnforcer, limitedCallsEnforcer,
        nonceEnforcer, timestampEnforcer, deploySalt
    });

    let currentNonce;
    try {
        currentNonce = await publicClient.readContract({
            address: nonceEnforcer,
            abi: NONCE_ENFORCER_ABI,
            functionName: "currentNonce",
            args: [delegationManager, delegatorAddress]
        });
    } catch (error) {
        throw new Error(`Could not read the current permission epoch on-chain: ${normalizeProviderError(error)}`);
    }
    const permissionEpoch = currentNonce + 1n;

    const block = await publicClient.getBlock();
    const validAfterUnix = Number(block.timestamp) - 1;
    const validBeforeUnix = validAfterUnix + 1 + Number(validForSeconds);

    const amountWei = parseUnits(String(amountDecimal), 18);
    const jobIdBig = BigInt(jobId);

    const approveData = encodeFunctionData({ abi: ERC20_ABI, functionName: "approve", args: [escrowAddress, amountWei] });
    const fundData = encodeFunctionData({ abi: ESCROW_ABI, functionName: "fund", args: [jobIdBig, amountWei, "0x"] });

    function unsignedPermission(target, callData, salt) {
        return createDelegation({
            environment,
            to: agentAddress,
            from: delegatorAddress,
            salt: toHex(salt),
            scope: {
                type: "functionCall",
                targets: [target],
                selectors: [callData.slice(0, 10)],
                exactCalldata: { calldata: callData }
            },
            caveats: [
                { type: "nonce", nonce: toHex(permissionEpoch, { size: 32 }) },
                { type: "timestamp", afterThreshold: validAfterUnix, beforeThreshold: validBeforeUnix },
                { type: "limitedCalls", limit: 1 }
            ]
        });
    }

    async function signPermission(permission) {
        const { signature: _signature, ...signable } = permission;
        const signature = await account.signDelegation({ delegation: signable });
        return { ...permission, signature };
    }

    let approvePermission;
    let fundPermission;
    try {
        approvePermission = await signPermission(unsignedPermission(tokenAddress, approveData, APPROVE_DELEGATION_SALT));
        fundPermission = await signPermission(unsignedPermission(escrowAddress, fundData, FUND_DELEGATION_SALT));
    } catch (error) {
        throw new Error(`The owner declined to sign a delegation: ${normalizeProviderError(error)}`);
    }

    const incrementNonceData = contracts.NonceEnforcer.encode.incrementNonce(delegationManager);
    const operation = await buildAndSignUserOperation(
        account,
        publicClient,
        [{ to: nonceEnforcer, data: incrementNonceData }],
        dotNetRef
    );

    return {
        operation,
        epoch: permissionEpoch.toString(),
        validAfterUnix,
        validBeforeUnix,
        delegatorAddress,
        agentAddress,
        grants: [
            {
                targetAddress: tokenAddress,
                selector: approveData.slice(0, 10),
                tokenAddress: null,
                amountWei: amountWei.toString(),
                delegationHash: getDelegationHashOffchain(approvePermission),
                description: "approve"
            },
            {
                targetAddress: escrowAddress,
                selector: fundData.slice(0, 10),
                tokenAddress: null,
                amountWei: amountWei.toString(),
                delegationHash: getDelegationHashOffchain(fundPermission),
                description: "fund"
            }
        ],
        // For owner inspection before broadcast (safety rule: nothing broadcasts without
        // explicit user action) - not persisted server-side. Delivering these to the agent
        // for redemption is a separate, out-of-scope concern (agenticPayments.ts wiring).
        signedDelegations: [approvePermission, fundPermission]
    };
}

export async function buildRevocationPayload({
    ownerAddress,
    chainIdHex,
    entryPoint,
    delegationManager,
    factory,
    hybridImplementation,
    allowedTargetsEnforcer,
    allowedMethodsEnforcer,
    exactCalldataEnforcer,
    limitedCallsEnforcer,
    nonceEnforcer,
    timestampEnforcer,
    deploySalt,
    dotNetRef
}) {
    const toolkit = requireToolkit();
    const { toMetaMaskSmartAccount, Implementation, createPublicClient, createWalletClient, custom, toHex, contracts } = toolkit;

    const { account, publicClient, delegatorAddress } = await buildAccount({
        toMetaMaskSmartAccount, Implementation, createPublicClient, createWalletClient, custom, toHex,
        ownerAddress, chainIdHex, entryPoint, delegationManager, factory, hybridImplementation,
        allowedTargetsEnforcer, allowedMethodsEnforcer, exactCalldataEnforcer, limitedCallsEnforcer,
        nonceEnforcer, timestampEnforcer, deploySalt
    });

    const incrementNonceData = contracts.NonceEnforcer.encode.incrementNonce(delegationManager);
    const operation = await buildAndSignUserOperation(
        account,
        publicClient,
        [{ to: nonceEnforcer, data: incrementNonceData }],
        dotNetRef
    );

    return { operation, delegatorAddress };
}

function requireToolkit() {
    const toolkit = window.MetaMaskDelegationToolkit;
    if (!toolkit) {
        throw new Error("The delegation toolkit bundle (js/delegation-toolkit.iife.min.js) did not load.");
    }
    if (!window.ethereum) {
        throw new Error("No injected EVM wallet was found.");
    }
    return toolkit;
}

async function buildAccount({
    toMetaMaskSmartAccount, Implementation, createPublicClient, createWalletClient, custom, toHex,
    ownerAddress, chainIdHex, entryPoint, delegationManager, factory, hybridImplementation,
    allowedTargetsEnforcer, allowedMethodsEnforcer, exactCalldataEnforcer, limitedCallsEnforcer,
    nonceEnforcer, timestampEnforcer, deploySalt
}) {
    const chainId = parseInt(chainIdHex, 16);
    const chain = {
        id: chainId,
        name: "configured-chain",
        nativeCurrency: { name: "ETH", symbol: "ETH", decimals: 18 },
        rpcUrls: { default: { http: [] } }
    };
    const transport = custom(window.ethereum);
    const publicClient = createPublicClient({ chain, transport });
    const walletClient = createWalletClient({ account: ownerAddress, chain, transport });

    const environment = {
        DelegationManager: delegationManager,
        EntryPoint: entryPoint,
        SimpleFactory: factory,
        implementations: { HybridDeleGatorImpl: hybridImplementation },
        caveatEnforcers: {
            AllowedTargetsEnforcer: allowedTargetsEnforcer,
            AllowedMethodsEnforcer: allowedMethodsEnforcer,
            ExactCalldataEnforcer: exactCalldataEnforcer,
            LimitedCallsEnforcer: limitedCallsEnforcer,
            NonceEnforcer: nonceEnforcer,
            TimestampEnforcer: timestampEnforcer
        }
    };

    const account = await toMetaMaskSmartAccount({
        client: publicClient,
        implementation: Implementation.Hybrid,
        deployParams: [ownerAddress, [], [], []],
        deploySalt: toHex(BigInt(deploySalt ?? "0"), { size: 32 }),
        signer: { walletClient },
        environment
    });

    return { account, publicClient, environment, delegatorAddress: await account.getAddress(), chainId };
}

/**
 * Builds the callData/nonce/initCode side of a UserOperation from the account itself (the
 * MetaMaskSmartAccount is a viem SmartAccount - getNonce/getFactoryArgs/encodeCalls/
 * getStubSignature/signUserOperation are its own published methods, not re-derived here),
 * asks the server for the bundler's own gas estimate (the browser has no bundler URL - see
 * ISmartAccountService.EstimateUserOperationGasAsync), then signs the final operation.
 * Nothing here broadcasts; it only produces a signed BundlerUserOperation-shaped payload for
 * the caller to submit after explicit user confirmation.
 */
async function buildAndSignUserOperation(account, publicClient, calls, dotNetRef) {
    const [sender, nonce, factoryArgs, callData, stubSignature, fees] = await Promise.all([
        account.getAddress(),
        account.getNonce(),
        account.getFactoryArgs(),
        account.encodeCalls(calls),
        account.getStubSignature(),
        publicClient.estimateFeesPerGas()
    ]);

    const maxFeePerGas = fees.maxFeePerGas;
    const maxPriorityFeePerGas = fees.maxPriorityFeePerGas;
    const initCode = factoryArgs?.factory ? `${factoryArgs.factory}${factoryArgs.factoryData.slice(2)}` : "0x";
    const nonceHex = toHexQuantity(nonce);

    const partialOperation = {
        sender,
        nonce: nonceHex,
        initCode,
        callData,
        accountGasLimits: packHiLo(0n, 0n),
        preVerificationGas: "0x0",
        gasFees: packHiLo(maxPriorityFeePerGas, maxFeePerGas),
        paymasterAndData: "0x",
        signature: stubSignature
    };

    let estimate;
    try {
        estimate = await dotNetRef.invokeMethodAsync("EstimateActivationGasAsync", partialOperation);
    } catch (error) {
        throw new Error(`Gas estimation failed: ${normalizeProviderError(error)}`);
    }

    const verificationGasLimit = BigInt(estimate.verificationGasLimit);
    const callGasLimit = BigInt(estimate.callGasLimit);
    const preVerificationGas = BigInt(estimate.preVerificationGas);

    let signature;
    try {
        signature = await account.signUserOperation({
            chainId: (await publicClient.getChainId()),
            sender,
            nonce,
            factory: factoryArgs?.factory,
            factoryData: factoryArgs?.factoryData,
            callData,
            callGasLimit,
            verificationGasLimit,
            preVerificationGas,
            maxFeePerGas,
            maxPriorityFeePerGas,
            signature: "0x"
        });
    } catch (error) {
        throw new Error(`The owner declined to sign the UserOperation: ${normalizeProviderError(error)}`);
    }

    return {
        sender,
        nonce: nonceHex,
        initCode,
        callData,
        accountGasLimits: packHiLo(verificationGasLimit, callGasLimit),
        preVerificationGas: toHexQuantity(preVerificationGas),
        gasFees: partialOperation.gasFees,
        paymasterAndData: "0x",
        signature
    };
}

/** bytes32: hi (16 bytes) | lo (16 bytes) - same packing v0.7 uses for accountGasLimits/gasFees. */
function packHiLo(hi, lo) {
    return `0x${hi.toString(16).padStart(32, "0")}${lo.toString(16).padStart(32, "0")}`;
}

function toHexQuantity(value) {
    return `0x${value.toString(16)}`;
}

/**
 * Wallet/provider rejections are frequently plain objects (RPC error shapes) rather than
 * Error instances - letting one propagate unwrapped renders as the unhelpful "[object Object]"
 * once it crosses into a .NET exception message. Always extract a real string here before
 * throwing across the JS interop boundary.
 */
function normalizeProviderError(error) {
    if (error instanceof Error) {
        return error.message;
    }
    if (typeof error === "string") {
        return error;
    }
    if (error && typeof error === "object") {
        const message = error.shortMessage ?? error.details ?? error.message ?? error.error?.message ?? error.data?.message;
        if (typeof message === "string" && message.trim().length > 0) {
            return message;
        }
        try {
            return JSON.stringify(error);
        } catch {
            return String(error);
        }
    }
    return String(error);
}
