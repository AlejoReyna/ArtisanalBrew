using System.ComponentModel.DataAnnotations;

namespace ThisCafeteria.Domain.Entities;

/// <summary>
/// A per-owner allowance to have ERC-4337 UserOperations sponsored on a given chain.
///
/// The paymaster itself (canonical VerifyingPaymaster) enforces nothing beyond "the verifying
/// signer approved this operation" — it will sponsor anything that signer signs. This entity is
/// the policy that decides whether to sign at all, and is therefore the only thing standing
/// between a sponsorship key and an open-ended gas drain.
///
/// Identity is ChainKey + OwnerAddress (lowercased): one grant per owner per chain.
/// </summary>
public class SponsorshipGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Chain registry key (e.g. "evm-local").</summary>
    [MaxLength(64)]
    public string ChainKey { get; set; } = string.Empty;

    /// <summary>Owner EOA that controls the smart account, stored lowercased.</summary>
    [MaxLength(128)]
    public string OwnerAddress { get; set; } = string.Empty;

    /// <summary>Total sponsorship budget in USD. Operations are denied once SpentUsd would exceed it.</summary>
    public decimal BudgetUsd { get; set; }

    /// <summary>Sponsorship consumed so far, in USD.</summary>
    public decimal SpentUsd { get; set; }

    /// <summary>Largest single operation this grant will sponsor, in USD. Zero means "no per-op cap".</summary>
    public decimal MaxOperationCostUsd { get; set; }

    /// <summary>Grant is not valid before this instant.</summary>
    public DateTime ValidFromUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Grant is not valid at or after this instant.</summary>
    public DateTime ValidUntilUtc { get; set; }

    /// <summary>Set when the grant is explicitly revoked. A revoked grant is never reinstated.</summary>
    public DateTime? RevokedAtUtc { get; set; }

    /// <summary>
    /// Sponsored operations that were mined but whose inner call reverted.
    ///
    /// These cost the paymaster real gas while debiting nothing from <see cref="SpentUsd"/> — the
    /// EntryPoint charges the paymaster for a mined operation regardless of whether the inner call
    /// succeeded, but there is no successful operation to price against the budget. Left unmetered,
    /// a holder of a *valid* grant could drain the paymaster's deposit indefinitely without ever
    /// exhausting its own budget, so the count is a spend control in its own right rather than a
    /// diagnostic: see <c>SponsorshipPolicyOptions.MaxRevertedOperations</c>.
    /// </summary>
    public int RevertedOperationCount { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Guards against concurrent debits racing past the budget.</summary>
    [ConcurrencyCheck]
    public int ConcurrencyToken { get; set; }

    public decimal RemainingUsd => BudgetUsd - SpentUsd;
}
