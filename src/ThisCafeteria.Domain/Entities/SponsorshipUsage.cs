using System.ComponentModel.DataAnnotations;

namespace ThisCafeteria.Domain.Entities;

/// <summary>
/// Audit record of a single sponsored operation debited against a <see cref="SponsorshipGrant"/>.
///
/// Kept separate from the grant so that spend is reconstructable and auditable rather than being
/// only a running total that could drift.
/// </summary>
public class SponsorshipUsage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid GrantId { get; set; }

    [MaxLength(64)]
    public string ChainKey { get; set; } = string.Empty;

    [MaxLength(128)]
    public string OwnerAddress { get; set; } = string.Empty;

    /// <summary>Cost debited from the grant, in USD.</summary>
    public decimal CostUsd { get; set; }

    /// <summary>Target contract the sponsored operation called, if known (lowercased).</summary>
    [MaxLength(128)]
    public string TargetAddress { get; set; } = string.Empty;

    /// <summary>4-byte function selector invoked, if known (e.g. "0xb61d27f6").</summary>
    [MaxLength(10)]
    public string Selector { get; set; } = string.Empty;

    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;
}
