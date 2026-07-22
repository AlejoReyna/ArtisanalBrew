using System.ComponentModel.DataAnnotations;

namespace ThisCafeteria.Domain.Entities;

/// <summary>
/// Distinguishes the reference ERC-4337 SimpleAccount path (unchanged, existing users) from the
/// modular MetaMask Delegation Framework HybridDeleGator path used for agent session keys.
/// </summary>
public enum SmartAccountType
{
    SimpleAccount = 0,
    ModularHybridDeleGator = 1
}

/// <summary>
/// A discovered or deployed smart-account address for an owner on a chain. One row per
/// (ChainKey, OwnerAddress, AccountType) — an owner can hold both a legacy SimpleAccount and a
/// modular HybridDeleGator account at once, which is why AccountType is part of identity rather
/// than a single "the" account per owner.
///
/// This table is an index, not an authority: it never grants anything by itself. Legacy
/// SimpleAccount addresses are derived directly from the unmodified factory's own
/// <c>getAddress(owner, salt)</c> view function. Modular account addresses are supplied by the
/// caller (which computes them using the audited @metamask/delegation-toolkit SDK — deriving a
/// CREATE2 proxy address by hand here would duplicate SDK-internal bytecode/immutable-args
/// knowledge, which is exactly the kind of bespoke reconstruction the provenance rules forbid)
/// and are independently verified on-chain once deployed via the ERC-1967 implementation slot.
/// </summary>
public class SmartAccountRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(64)]
    public string ChainKey { get; set; } = string.Empty;

    /// <summary>The EOA that ultimately controls this account, stored lowercased.</summary>
    [MaxLength(128)]
    public string OwnerAddress { get; set; } = string.Empty;

    public SmartAccountType AccountType { get; set; }

    /// <summary>The smart account's own address, stored lowercased.</summary>
    [MaxLength(128)]
    public string AccountAddress { get; set; } = string.Empty;

    /// <summary>Decimal-string uint256 salt used for counterfactual derivation.</summary>
    [MaxLength(80)]
    public string Salt { get; set; } = "0";

    [MaxLength(128)]
    public string FactoryAddress { get; set; } = string.Empty;

    /// <summary>True once <c>eth_getCode</c> observed non-empty bytecode at AccountAddress.</summary>
    public bool IsDeployed { get; set; }

    /// <summary>
    /// True once the deployed bytecode was independently confirmed to be the expected
    /// implementation (ERC-1967 implementation slot for modular accounts). Meaningless (left
    /// false) for legacy SimpleAccount rows, which do not use a proxy pattern.
    /// </summary>
    public bool ImplementationVerified { get; set; }

    public DateTime DiscoveredAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DeployedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
