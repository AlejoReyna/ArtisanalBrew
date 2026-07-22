/**
 * Derives a HybridDeleGator's counterfactual address in the browser, using the
 * connected wallet as both the owner signer and the read/write RPC transport
 * (via viem's `custom` transport over window.ethereum - no separate RPC key
 * needed client-side). Depends on window.MetaMaskDelegationToolkit, the
 * pinned @metamask/delegation-toolkit + viem bundle built by
 * contracts/evm/scripts/build-browser-delegation-bundle.ts
 * (see js/delegation-toolkit.iife.min.js).
 *
 * This never deploys the account or signs a delegation - it only computes the
 * address the server will independently verify (implementation slot, once
 * deployed) when it is registered via ISmartAccountService.RegisterModularAccountAsync.
 */
export async function deriveModularAccountAddress({
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
    deploySalt
}) {
    const toolkit = window.MetaMaskDelegationToolkit;
    if (!toolkit) {
        throw new Error("The delegation toolkit bundle (js/delegation-toolkit.iife.min.js) did not load.");
    }
    if (!window.ethereum) {
        throw new Error("No injected EVM wallet was found.");
    }

    const { toMetaMaskSmartAccount, Implementation, createPublicClient, createWalletClient, custom, toHex } = toolkit;
    const chainId = parseInt(chainIdHex, 16);
    const chain = {
        id: chainId,
        name: "configured-chain",
        nativeCurrency: { name: "ETH", symbol: "ETH", decimals: 18 },
        rpcUrls: { default: { http: [] } }
    };
    const transport = custom(window.ethereum);
    const client = createPublicClient({ chain, transport });
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

    const saltValue = BigInt(deploySalt ?? "0");
    const account = await toMetaMaskSmartAccount({
        client,
        implementation: Implementation.Hybrid,
        deployParams: [ownerAddress, [], [], []],
        deploySalt: toHex(saltValue, { size: 32 }),
        signer: { walletClient },
        environment
    });

    return {
        address: await account.getAddress(),
        deploySalt: saltValue.toString()
    };
}
