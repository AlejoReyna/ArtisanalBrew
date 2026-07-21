namespace ThisCafeteria.Application.Configuration;

/// <summary>
/// Sponsorship policy configuration. Fail-closed by construction: sponsorship is disabled unless
/// explicitly enabled, and empty allowlists deny everything rather than allowing everything.
/// </summary>
public sealed record SponsorshipPolicyOptions
{
    public const string SectionName = "Sponsorship";

    /// <summary>Master switch. Sponsorship is refused entirely while false.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Contract addresses a sponsored operation may call. An empty list denies every target —
    /// an allowlist that silently means "allow all" when unset is how gas budgets get drained.
    /// </summary>
    public IReadOnlyList<string> AllowedTargets { get; init; } = Array.Empty<string>();

    /// <summary>4-byte selectors a sponsored operation may invoke. Empty denies every selector.</summary>
    public IReadOnlyList<string> AllowedSelectors { get; init; } = Array.Empty<string>();

    /// <summary>Budget assigned to a newly created grant, in USD.</summary>
    public decimal DefaultBudgetUsd { get; init; } = 10m;

    /// <summary>Per-operation cap applied to new grants, in USD. Zero means no per-operation cap.</summary>
    public decimal DefaultMaxOperationCostUsd { get; init; } = 1m;

    /// <summary>Validity window applied to new grants.</summary>
    public TimeSpan DefaultValidity { get; init; } = TimeSpan.FromDays(30);

    public bool IsTargetAllowed(string? target) =>
        !string.IsNullOrWhiteSpace(target)
        && AllowedTargets.Any(t => string.Equals(t, target, StringComparison.OrdinalIgnoreCase));

    public bool IsSelectorAllowed(string? selector) =>
        !string.IsNullOrWhiteSpace(selector)
        && AllowedSelectors.Any(s => string.Equals(s, selector, StringComparison.OrdinalIgnoreCase));
}
