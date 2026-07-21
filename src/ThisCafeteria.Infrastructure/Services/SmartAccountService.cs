using System.Numerics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nethereum.ABI.FunctionEncoding.Attributes;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.Util;
using Nethereum.Web3;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.Services;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Persistence;

namespace ThisCafeteria.Infrastructure.Services;

/// <summary>
/// ERC-4337 smart-account support backed by two pinned, unmodified stacks:
///   - account-abstraction v0.7.0 EntryPoint + reference SimpleAccountFactory (legacy, unchanged);
///   - MetaMask Delegation Framework v1.3.0 modular accounts, for agent session-key permissions.
///     See docs/erc4337-session-key-provenance.md for the provenance matrix.
///
/// This service never constructs UserOperations, delegations, or signatures itself — that stays
/// inside the audited @metamask/delegation-toolkit SDK boundary (used by the Node AgentGateway and
/// the Hardhat scripts). What it does:
///   - derives/verifies account addresses via unmodified contracts' own view functions and, for
///     modular accounts, the standard ERC-1967 implementation slot;
///   - reads NonceEnforcer.currentNonce live to confirm permission-epoch state before persisting
///     anything, so the database can never assert a permission is active that the chain disagrees
///     with;
///   - persists an index of accounts and permission epochs for discovery/UI, and sponsorship
///     quota checks/revocation, delegated to ISponsorshipPolicyService.
/// </summary>
public class SmartAccountService(
    IChainRegistry chains,
    ISponsorshipPolicyService sponsorship,
    AppDbContext db,
    TimeProvider timeProvider,
    ILogger<SmartAccountService> logger) : ISmartAccountService
{
    /// <summary>Salt used for legacy SimpleAccount derivation. One account per owner for now.</summary>
    private const int AccountSalt = 0;

    /// <summary>EIP-1967 implementation storage slot: bytes32(uint256(keccak256("eip1967.proxy.implementation")) - 1).</summary>
    private const string Erc1967ImplementationSlot = "0x360894a13ba1a3210667c828492db98dca3e2076cc3735a920a3ca505d382bb";

    [Function("getAddress", "address")]
    private sealed class GetAddressFunction : FunctionMessage
    {
        [Parameter("address", "owner", 1)]
        public string Owner { get; set; } = string.Empty;

        [Parameter("uint256", "salt", 2)]
        public BigInteger Salt { get; set; }
    }

    [Function("currentNonce", "uint256")]
    private sealed class CurrentNonceFunction : FunctionMessage
    {
        [Parameter("address", "delegationManager", 1)]
        public string DelegationManager { get; set; } = string.Empty;

        [Parameter("address", "delegator", 2)]
        public string Delegator { get; set; } = string.Empty;
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

        await UpsertAccountRecordAsync(chainKey, ownerAddress, SmartAccountType.SimpleAccount, accountAddress, AccountSalt.ToString(), factoryAddress, isDeployed, implementationVerified: false).ConfigureAwait(false);

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
    /// Revokes the owner's sponsorship grant (permanent), and, if a modular account and an active
    /// permission epoch exist for this owner, revokes that epoch too — but only once NonceEnforcer
    /// confirms the owner's account has actually moved past it on-chain. If the chain still reports
    /// the epoch as current, this leaves the epoch record alone rather than mark it revoked ahead
    /// of the chain, since that would let the record disagree with what can still be redeemed.
    /// </summary>
    public async Task RevokeSessionPermissionsAsync(string chainKey, string ownerAddress)
    {
        await sponsorship.RevokeAsync(chainKey, ownerAddress).ConfigureAwait(false);

        if (!TryGetConfiguredModularChain(chainKey, out var chain))
        {
            return;
        }

        var owner = Normalize(ownerAddress);
        var account = await db.SmartAccountRecords.FirstOrDefaultAsync(a =>
            a.ChainKey == chainKey && a.OwnerAddress == owner && a.AccountType == SmartAccountType.ModularHybridDeleGator)
            .ConfigureAwait(false);
        if (account is null)
        {
            return;
        }

        var epoch = await db.AgentPermissionEpochs
            .Where(e => e.ChainKey == chainKey && e.SmartAccountRecordId == account.Id && e.Status == AgentPermissionEpochStatus.Active)
            .OrderByDescending(e => e.CreatedAtUtc)
            .FirstOrDefaultAsync().ConfigureAwait(false);
        if (epoch is null)
        {
            return;
        }

        var web3 = new Web3(chain.EffectiveServerRpcUrl);
        var currentNonce = await ReadCurrentNonceAsync(web3, chain.Deployment.NonceEnforcer, chain.Deployment.DelegationManager, account.AccountAddress).ConfigureAwait(false);

        if (currentNonce.ToString() == epoch.Epoch)
        {
            logger.LogInformation(
                "RevokeSessionPermissionsAsync: on-chain nonce for {AccountAddress} on {ChainKey} still equals epoch {Epoch}; leaving the permission record active until the owner actually revokes it on-chain.",
                account.AccountAddress, chainKey, epoch.Epoch);
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        epoch.Status = AgentPermissionEpochStatus.Revoked;
        epoch.RevokedAtUtc = now;
        epoch.UpdatedAtUtc = now;
        epoch.ConcurrencyToken++;
        await db.SaveChangesAsync().ConfigureAwait(false);

        logger.LogWarning("Revoked permission epoch {Epoch} for {AccountAddress} on {ChainKey} (on-chain nonce advanced to {CurrentNonce}).", epoch.Epoch, account.AccountAddress, chainKey, currentNonce);
    }

    public async Task<IReadOnlyList<SmartAccountInfo>> DiscoverAccountsAsync(string chainKey, string ownerAddress)
    {
        if (string.IsNullOrWhiteSpace(ownerAddress) || !ownerAddress.IsValidEthereumAddressHexFormat())
        {
            throw new ArgumentException($"'{ownerAddress}' is not a valid Ethereum address.", nameof(ownerAddress));
        }

        var results = new List<SmartAccountInfo>();

        if (TryGetConfiguredChain(chainKey, out _, out _))
        {
            var legacyAddress = await GetOrDeployAccountAsync(chainKey, ownerAddress).ConfigureAwait(false);
            results.Add(await ToInfoAsync(chainKey, ownerAddress, SmartAccountType.SimpleAccount).ConfigureAwait(false)
                ?? new SmartAccountInfo { ChainKey = chainKey, OwnerAddress = Normalize(ownerAddress), AccountType = SmartAccountType.SimpleAccount, AccountAddress = Normalize(legacyAddress) });
        }

        var owner = Normalize(ownerAddress);
        var modularRecord = await db.SmartAccountRecords.FirstOrDefaultAsync(a =>
            a.ChainKey == chainKey && a.OwnerAddress == owner && a.AccountType == SmartAccountType.ModularHybridDeleGator)
            .ConfigureAwait(false);
        if (modularRecord is not null && TryGetConfiguredModularChain(chainKey, out var chain))
        {
            await RefreshDeploymentStatusAsync(chain, modularRecord).ConfigureAwait(false);
            results.Add(ToInfo(modularRecord));
        }

        return results;
    }

    public async Task<SmartAccountInfo> RegisterModularAccountAsync(string chainKey, string ownerAddress, string accountAddress, string salt)
    {
        if (!TryGetConfiguredModularChain(chainKey, out var chain))
        {
            logger.LogWarning("RegisterModularAccountAsync called for {ChainKey} but the modular account stack is not fully configured.", chainKey);
            throw new NotSupportedException($"Modular smart accounts are not configured for chain '{chainKey}'.");
        }

        if (string.IsNullOrWhiteSpace(ownerAddress) || !ownerAddress.IsValidEthereumAddressHexFormat())
        {
            throw new ArgumentException($"'{ownerAddress}' is not a valid Ethereum address.", nameof(ownerAddress));
        }

        if (string.IsNullOrWhiteSpace(accountAddress) || !accountAddress.IsValidEthereumAddressHexFormat())
        {
            throw new ArgumentException($"'{accountAddress}' is not a valid Ethereum address.", nameof(accountAddress));
        }

        var owner = Normalize(ownerAddress);
        var account = Normalize(accountAddress);

        var record = await db.SmartAccountRecords.FirstOrDefaultAsync(a =>
            a.ChainKey == chainKey && a.OwnerAddress == owner && a.AccountType == SmartAccountType.ModularHybridDeleGator)
            .ConfigureAwait(false);

        if (record is not null && record.AccountAddress != account)
        {
            throw new InvalidOperationException(
                $"Owner '{owner}' already has a different registered modular account ({record.AccountAddress}) on '{chainKey}'.");
        }

        if (record is null)
        {
            record = new SmartAccountRecord
            {
                ChainKey = chainKey,
                OwnerAddress = owner,
                AccountType = SmartAccountType.ModularHybridDeleGator,
                AccountAddress = account,
                Salt = string.IsNullOrWhiteSpace(salt) ? "0" : salt,
                FactoryAddress = Normalize(chain.Deployment.ModularAccountFactory)
            };
            db.SmartAccountRecords.Add(record);
        }

        await RefreshDeploymentStatusAsync(chain, record).ConfigureAwait(false);
        await db.SaveChangesAsync().ConfigureAwait(false);

        return ToInfo(record);
    }

    public async Task<AgentPermissionEpochInfo?> GetActivePermissionEpochAsync(string chainKey, string delegatorAddress)
    {
        if (!TryGetConfiguredModularChain(chainKey, out var chain))
        {
            return null;
        }

        var delegator = Normalize(delegatorAddress);
        var epoch = await db.AgentPermissionEpochs
            .Where(e => e.ChainKey == chainKey && e.DelegatorAddress == delegator)
            .OrderByDescending(e => e.CreatedAtUtc)
            .FirstOrDefaultAsync().ConfigureAwait(false);
        if (epoch is null)
        {
            return null;
        }

        var web3 = new Web3(chain.EffectiveServerRpcUrl);
        var currentNonce = await ReadCurrentNonceAsync(web3, chain.Deployment.NonceEnforcer, chain.Deployment.DelegationManager, delegator).ConfigureAwait(false);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var onChainActive = currentNonce.ToString() == epoch.Epoch;
        var withinWindow = now >= epoch.ValidAfterUtc && now < epoch.ValidBeforeUtc;

        AgentPermissionEpochStatus resolvedStatus = epoch.Status switch
        {
            _ when !onChainActive => AgentPermissionEpochStatus.Revoked,
            _ when !withinWindow && now >= epoch.ValidBeforeUtc => AgentPermissionEpochStatus.Expired,
            _ => AgentPermissionEpochStatus.Active
        };

        if (resolvedStatus != epoch.Status)
        {
            epoch.Status = resolvedStatus;
            epoch.UpdatedAtUtc = now;
            epoch.ConcurrencyToken++;
            await db.SaveChangesAsync().ConfigureAwait(false);
        }

        if (resolvedStatus != AgentPermissionEpochStatus.Active)
        {
            return null;
        }

        var grants = await db.AgentPermissionGrants.Where(g => g.EpochId == epoch.Id).ToListAsync().ConfigureAwait(false);
        return ToInfo(epoch, grants);
    }

    public async Task<AgentPermissionEpochInfo> RecordPermissionEpochInstalledAsync(
        string chainKey,
        string delegatorAddress,
        string agentAddress,
        string epoch,
        DateTime validAfterUtc,
        DateTime validBeforeUtc,
        string installedTxHash,
        IReadOnlyList<AgentPermissionGrantInput> grants)
    {
        if (!TryGetConfiguredModularChain(chainKey, out var chain))
        {
            throw new NotSupportedException($"Modular smart accounts are not configured for chain '{chainKey}'.");
        }

        if (string.IsNullOrWhiteSpace(delegatorAddress) || !delegatorAddress.IsValidEthereumAddressHexFormat())
        {
            throw new ArgumentException($"'{delegatorAddress}' is not a valid Ethereum address.", nameof(delegatorAddress));
        }

        if (string.IsNullOrWhiteSpace(agentAddress) || !agentAddress.IsValidEthereumAddressHexFormat())
        {
            throw new ArgumentException($"'{agentAddress}' is not a valid Ethereum address.", nameof(agentAddress));
        }

        if (grants.Count == 0)
        {
            throw new ArgumentException("At least one permission grant is required.", nameof(grants));
        }

        var delegator = Normalize(delegatorAddress);
        var account = await db.SmartAccountRecords.FirstOrDefaultAsync(a =>
            a.ChainKey == chainKey && a.AccountAddress == delegator && a.AccountType == SmartAccountType.ModularHybridDeleGator)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"No registered modular account '{delegator}' on '{chainKey}'. Call RegisterModularAccountAsync first.");

        var web3 = new Web3(chain.EffectiveServerRpcUrl);
        var currentNonce = await ReadCurrentNonceAsync(web3, chain.Deployment.NonceEnforcer, chain.Deployment.DelegationManager, delegator).ConfigureAwait(false);

        if (currentNonce.ToString() != epoch)
        {
            throw new InvalidOperationException(
                $"NonceEnforcer.currentNonce for '{delegator}' on '{chainKey}' is {currentNonce}, not the claimed epoch {epoch}. Refusing to record an epoch the chain does not agree is active.");
        }

        var existing = await db.AgentPermissionEpochs.FirstOrDefaultAsync(e =>
            e.ChainKey == chainKey && e.SmartAccountRecordId == account.Id && e.Epoch == epoch)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            throw new InvalidOperationException($"Epoch {epoch} for '{delegator}' on '{chainKey}' is already recorded.");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var record = new AgentPermissionEpoch
        {
            ChainKey = chainKey,
            SmartAccountRecordId = account.Id,
            DelegatorAddress = delegator,
            OwnerAddress = account.OwnerAddress,
            AgentAddress = Normalize(agentAddress),
            Epoch = epoch,
            ValidAfterUtc = validAfterUtc,
            ValidBeforeUtc = validBeforeUtc,
            Status = AgentPermissionEpochStatus.Active,
            InstalledAtUtc = now,
            InstalledTxHash = installedTxHash,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.AgentPermissionEpochs.Add(record);

        foreach (var grant in grants)
        {
            db.AgentPermissionGrants.Add(new AgentPermissionGrant
            {
                EpochId = record.Id,
                TargetAddress = Normalize(grant.TargetAddress),
                Selector = grant.Selector,
                TokenAddress = grant.TokenAddress is null ? null : Normalize(grant.TokenAddress),
                AmountWei = grant.AmountWei,
                DelegationHash = grant.DelegationHash,
                Description = grant.Description,
                CreatedAtUtc = now
            });
        }

        await db.SaveChangesAsync().ConfigureAwait(false);

        logger.LogInformation("Recorded permission epoch {Epoch} for delegator {Delegator} / agent {Agent} on {ChainKey}.", epoch, delegator, agentAddress, chainKey);

        var savedGrants = await db.AgentPermissionGrants.Where(g => g.EpochId == record.Id).ToListAsync().ConfigureAwait(false);
        return ToInfo(record, savedGrants);
    }

    private async Task RefreshDeploymentStatusAsync(ChainDefinition chain, SmartAccountRecord record)
    {
        var web3 = new Web3(chain.EffectiveServerRpcUrl);
        var code = await web3.Eth.GetCode.SendRequestAsync(record.AccountAddress).ConfigureAwait(false);
        var isDeployed = !string.IsNullOrEmpty(code) && code != "0x";

        var now = timeProvider.GetUtcNow().UtcDateTime;
        record.IsDeployed = isDeployed;
        record.UpdatedAtUtc = now;
        if (isDeployed && record.DeployedAtUtc is null)
        {
            record.DeployedAtUtc = now;
        }

        if (isDeployed && record.AccountType == SmartAccountType.ModularHybridDeleGator && !record.ImplementationVerified)
        {
            var implementation = await ReadErc1967ImplementationAsync(web3, record.AccountAddress).ConfigureAwait(false);
            var expected = Normalize(chain.Deployment.HybridDeleGatorImplementation);
            if (Normalize(implementation) != expected)
            {
                throw new InvalidOperationException(
                    $"Deployed bytecode at '{record.AccountAddress}' on '{chain.Key}' does not proxy to the expected HybridDeleGator implementation ({expected}); got '{implementation}'.");
            }

            record.ImplementationVerified = true;
        }
    }

    private async Task UpsertAccountRecordAsync(string chainKey, string ownerAddress, SmartAccountType type, string accountAddress, string salt, string factoryAddress, bool isDeployed, bool implementationVerified)
    {
        var owner = Normalize(ownerAddress);
        var record = await db.SmartAccountRecords.FirstOrDefaultAsync(a =>
            a.ChainKey == chainKey && a.OwnerAddress == owner && a.AccountType == type)
            .ConfigureAwait(false);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (record is null)
        {
            db.SmartAccountRecords.Add(new SmartAccountRecord
            {
                ChainKey = chainKey,
                OwnerAddress = owner,
                AccountType = type,
                AccountAddress = Normalize(accountAddress),
                Salt = salt,
                FactoryAddress = Normalize(factoryAddress),
                IsDeployed = isDeployed,
                ImplementationVerified = implementationVerified,
                DeployedAtUtc = isDeployed ? now : null,
                UpdatedAtUtc = now
            });
        }
        else
        {
            record.IsDeployed = isDeployed;
            record.UpdatedAtUtc = now;
            if (isDeployed && record.DeployedAtUtc is null)
            {
                record.DeployedAtUtc = now;
            }
        }

        await db.SaveChangesAsync().ConfigureAwait(false);
    }

    private async Task<SmartAccountInfo?> ToInfoAsync(string chainKey, string ownerAddress, SmartAccountType type)
    {
        var owner = Normalize(ownerAddress);
        var record = await db.SmartAccountRecords.FirstOrDefaultAsync(a =>
            a.ChainKey == chainKey && a.OwnerAddress == owner && a.AccountType == type)
            .ConfigureAwait(false);
        return record is null ? null : ToInfo(record);
    }

    private static SmartAccountInfo ToInfo(SmartAccountRecord record) => new()
    {
        ChainKey = record.ChainKey,
        OwnerAddress = record.OwnerAddress,
        AccountType = record.AccountType,
        AccountAddress = record.AccountAddress,
        FactoryAddress = record.FactoryAddress,
        IsDeployed = record.IsDeployed,
        ImplementationVerified = record.ImplementationVerified
    };

    private static AgentPermissionEpochInfo ToInfo(AgentPermissionEpoch epoch, IReadOnlyList<AgentPermissionGrant> grants) => new()
    {
        ChainKey = epoch.ChainKey,
        DelegatorAddress = epoch.DelegatorAddress,
        OwnerAddress = epoch.OwnerAddress,
        AgentAddress = epoch.AgentAddress,
        Epoch = epoch.Epoch,
        ValidAfterUtc = epoch.ValidAfterUtc,
        ValidBeforeUtc = epoch.ValidBeforeUtc,
        Status = epoch.Status,
        InstalledAtUtc = epoch.InstalledAtUtc,
        InstalledTxHash = epoch.InstalledTxHash,
        RevokedAtUtc = epoch.RevokedAtUtc,
        RevokedTxHash = epoch.RevokedTxHash,
        Grants = grants.Select(g => new AgentPermissionGrantInfo
        {
            TargetAddress = g.TargetAddress,
            Selector = g.Selector,
            TokenAddress = g.TokenAddress,
            AmountWei = g.AmountWei,
            DelegationHash = g.DelegationHash,
            Description = g.Description
        }).ToList()
    };

    private static async Task<BigInteger> ReadCurrentNonceAsync(Web3 web3, string nonceEnforcer, string delegationManager, string delegator)
    {
        var handler = web3.Eth.GetContractQueryHandler<CurrentNonceFunction>();
        return await handler.QueryAsync<BigInteger>(
            nonceEnforcer,
            new CurrentNonceFunction { DelegationManager = delegationManager, Delegator = delegator }).ConfigureAwait(false);
    }

    private static async Task<string> ReadErc1967ImplementationAsync(Web3 web3, string accountAddress)
    {
        var raw = await web3.Eth.GetStorageAt.SendRequestAsync(accountAddress, new HexBigInteger(BigInteger.Parse(Erc1967ImplementationSlot[2..], System.Globalization.NumberStyles.HexNumber))).ConfigureAwait(false);
        var hex = raw.Length >= 40 ? raw[^40..] : raw.PadLeft(40, '0');
        return "0x" + hex;
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

    /// <summary>
    /// A chain supports modular accounts only when every audited component is present: EntryPoint,
    /// the modular factory, DelegationManager, the HybridDeleGator implementation, and all six
    /// caveat enforcers this architecture relies on. Missing any one of them fails closed.
    /// </summary>
    private bool TryGetConfiguredModularChain(string chainKey, out ChainDefinition chain)
    {
        chain = default!;

        var match = chains.All.FirstOrDefault(c => string.Equals(c.Key, chainKey, StringComparison.OrdinalIgnoreCase));
        if (match is null || match.EvmChainId is null || string.IsNullOrWhiteSpace(match.EffectiveServerRpcUrl))
        {
            return false;
        }

        var d = match.Deployment;
        if (string.IsNullOrWhiteSpace(d.EntryPoint)
            || string.IsNullOrWhiteSpace(d.ModularAccountFactory)
            || string.IsNullOrWhiteSpace(d.DelegationManager)
            || string.IsNullOrWhiteSpace(d.HybridDeleGatorImplementation)
            || string.IsNullOrWhiteSpace(d.AllowedTargetsEnforcer)
            || string.IsNullOrWhiteSpace(d.AllowedMethodsEnforcer)
            || string.IsNullOrWhiteSpace(d.ExactCalldataEnforcer)
            || string.IsNullOrWhiteSpace(d.LimitedCallsEnforcer)
            || string.IsNullOrWhiteSpace(d.NonceEnforcer)
            || string.IsNullOrWhiteSpace(d.TimestampEnforcer))
        {
            logger.LogDebug("Chain {ChainKey} does not have a fully configured modular account stack.", chainKey);
            return false;
        }

        chain = match;
        return true;
    }

    private static string Normalize(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}
