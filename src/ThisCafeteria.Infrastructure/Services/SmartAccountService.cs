using Microsoft.Extensions.Logging;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Util;
using Nethereum.Web3;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;

namespace ThisCafeteria.Infrastructure.Services;

/// <summary>
/// ERC-4337 smart-account support backed by the pinned canonical stack
/// (account-abstraction v0.7.0 EntryPoint + reference SimpleAccountFactory).
///
/// Implemented:
///   - configuration discovery (EntryPoint + account factory present on an EVM chain);
///   - counterfactual account address derivation via the factory's getAddress(owner, salt).
///
///   - sponsorship quota checks and revocation, delegated to ISponsorshipPolicyService.
///
/// Deliberately still fail-closed (Phase 4 work in progress):
///   - session-key permissions require an audited permissions module;
///   - actual on-chain deployment happens when the first UserOperation carrying initCode is
///     submitted through a bundler. No bundler is configured, so nothing here submits UserOps.
///
/// Under ERC-4337 an account address is deterministic and usable before deployment, so returning
/// the counterfactual address is correct and not a stand-in for a deployment we cannot perform.
/// </summary>
public class SmartAccountService(
    IChainRegistry chains,
    ISponsorshipPolicyService sponsorship,
    ILogger<SmartAccountService> logger) : ISmartAccountService
{
    /// <summary>Salt used for account derivation. One account per owner for now.</summary>
    private const int AccountSalt = 0;

    [Function("getAddress", "address")]
    private sealed class GetAddressFunction : FunctionMessage
    {
        [Parameter("address", "owner", 1)]
        public string Owner { get; set; } = string.Empty;

        [Parameter("uint256", "salt", 2)]
        public System.Numerics.BigInteger Salt { get; set; }
    }

    public Task<bool> IsConfiguredAsync(string chainKey)
    {
        return Task.FromResult(TryGetConfiguredChain(chainKey, out _, out _));
    }

    public async Task<string> GetOrDeployAccountAsync(string chainKey, string ownerAddress)
    {
        if (!TryGetConfiguredChain(chainKey, out var chain, out var factoryAddress))
        {
            logger.LogWarning("GetOrDeployAccountAsync called for {ChainKey} but no ERC-4337 EntryPoint/account factory is configured.", chainKey);
            throw new NotSupportedException($"Smart account deployment is not configured for chain '{chainKey}'.");
        }

        if (string.IsNullOrWhiteSpace(ownerAddress) || !ownerAddress.IsValidEthereumAddressHexFormat())
        {
            throw new ArgumentException($"'{ownerAddress}' is not a valid Ethereum address.", nameof(ownerAddress));
        }

        var web3 = new Web3(chain.EffectiveServerRpcUrl);
        var handler = web3.Eth.GetContractQueryHandler<GetAddressFunction>();

        var accountAddress = await handler.QueryAsync<string>(
            factoryAddress,
            new GetAddressFunction { Owner = ownerAddress, Salt = AccountSalt }).ConfigureAwait(false);

        // Distinguish an already-deployed account from a purely counterfactual one. Both are
        // valid to return; the difference matters to callers deciding whether initCode is needed.
        var code = await web3.Eth.GetCode.SendRequestAsync(accountAddress).ConfigureAwait(false);
        var isDeployed = !string.IsNullOrEmpty(code) && code != "0x";

        logger.LogInformation(
            "Smart account for {OwnerAddress} on {ChainKey} is {AccountAddress} (deployed: {IsDeployed}).",
            ownerAddress, chainKey, accountAddress, isDeployed);

        return accountAddress;
    }

    /// <summary>
    /// Coarse budget check. This overload carries no target or selector, so it can only evaluate
    /// grant validity and budget. Callers that actually produce a paymaster signature must go
    /// through <see cref="ISponsorshipPolicyService.EvaluateAsync"/> with the target and selector
    /// populated, otherwise wrong-target/wrong-selector operations would go unchecked.
    /// </summary>
    public async Task<bool> HasSufficientSponsorshipQuotaAsync(string chainKey, string ownerAddress, decimal estimatedCostUsd)
    {
        var decision = await sponsorship.EvaluateAsync(new SponsorshipRequest
        {
            ChainKey = chainKey,
            OwnerAddress = ownerAddress,
            EstimatedCostUsd = estimatedCostUsd
        }).ConfigureAwait(false);

        if (!decision.Approved)
        {
            logger.LogDebug(
                "Sponsorship denied for {OwnerAddress} on {ChainKey}: {Reason} - {Detail}",
                ownerAddress, chainKey, decision.Reason, decision.Detail);
        }

        return decision.Approved;
    }

    public Task RecordSponsorshipUsageAsync(string chainKey, string ownerAddress, decimal costUsd)
    {
        return sponsorship.RecordUsageAsync(new SponsorshipRequest
        {
            ChainKey = chainKey,
            OwnerAddress = ownerAddress,
            EstimatedCostUsd = costUsd
        });
    }

    /// <summary>
    /// Revokes the owner's sponsorship grant. Session-key permissions proper still require an
    /// audited permissions module and remain unimplemented; revoking sponsorship is the part that
    /// exists today, and it is permanent.
    /// </summary>
    public Task RevokeSessionPermissionsAsync(string chainKey, string ownerAddress)
    {
        return sponsorship.RevokeAsync(chainKey, ownerAddress);
    }

    /// <summary>
    /// A chain supports smart accounts when it is an EVM chain that has both a deployed EntryPoint
    /// and an account factory in its manifest, plus a usable RPC endpoint.
    /// </summary>
    private bool TryGetConfiguredChain(string chainKey, out ChainDefinition chain, out string factoryAddress)
    {
        chain = default!;
        factoryAddress = string.Empty;

        var match = chains.All.FirstOrDefault(c => string.Equals(c.Key, chainKey, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            logger.LogDebug("Chain {ChainKey} is not registered.", chainKey);
            return false;
        }

        if (match.EvmChainId is null
            || string.IsNullOrWhiteSpace(match.Deployment.EntryPoint)
            || string.IsNullOrWhiteSpace(match.Deployment.AccountFactory)
            || string.IsNullOrWhiteSpace(match.EffectiveServerRpcUrl))
        {
            logger.LogDebug("Chain {ChainKey} has no usable ERC-4337 configuration.", chainKey);
            return false;
        }

        chain = match;
        factoryAddress = match.Deployment.AccountFactory;
        return true;
    }
}
