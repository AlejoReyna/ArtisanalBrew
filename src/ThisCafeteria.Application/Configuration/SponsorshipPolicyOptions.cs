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

    /// <summary>
    /// Private key of the paymaster's verifying signer. Sponsorship signing is disabled when this
    /// is empty, so the system fails closed rather than signing with some default.
    ///
    /// This key authorises spending gas. It is intended for local development only, must never be
    /// logged or committed, and a real deployment should source it from a secret store or KMS
    /// rather than from configuration.
    /// </summary>
    public string VerifyingSignerPrivateKey { get; init; } = string.Empty;

    /// <summary>
    /// USD price of one unit of the chain's native currency, used to price gas. Zero disables
    /// signing rather than silently treating gas as free — an unpriced budget is not a budget.
    /// </summary>
    public decimal NativeCurrencyUsdRate { get; init; }

    /// <summary>Gas limit written into paymasterAndData for paymaster validation.</summary>
    public ulong PaymasterVerificationGasLimit { get; init; } = 500_000;

    /// <summary>Gas limit written into paymasterAndData for the postOp call.</summary>
    public ulong PaymasterPostOpGasLimit { get; init; } = 200_000;

    /// <summary>How long a produced sponsorship signature remains valid.</summary>
    public TimeSpan SignatureValidity { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How many mined-but-reverted sponsored operations a grant may accumulate before it is
    /// automatically revoked. Zero disables the check.
    ///
    /// A reverted operation still costs the paymaster gas but debits nothing from the USD budget,
    /// so without this the budget is only a spend control against an honest grant-holder. The
    /// default is deliberately low: a handful of reverts is an integration bug worth interrupting,
    /// and revocation is recoverable by issuing a new grant, whereas a drained paymaster deposit
    /// stops sponsorship for everyone.
    /// </summary>
    public int MaxRevertedOperations { get; init; } = 5;

    /// <summary>Signing requires the policy enabled, a signer key, and a usable gas price.</summary>
    public bool CanSign => Enabled
        && !string.IsNullOrWhiteSpace(VerifyingSignerPrivateKey)
        && NativeCurrencyUsdRate > 0m;

    public bool IsTargetAllowed(string? target) =>
        !string.IsNullOrWhiteSpace(target)
        && AllowedTargets.Any(t => string.Equals(t, target, StringComparison.OrdinalIgnoreCase));

    public bool IsSelectorAllowed(string? selector) =>
        !string.IsNullOrWhiteSpace(selector)
        && AllowedSelectors.Any(s => string.Equals(s, selector, StringComparison.OrdinalIgnoreCase));
}
