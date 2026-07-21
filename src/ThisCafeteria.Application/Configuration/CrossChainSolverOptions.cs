namespace ThisCafeteria.Application.Configuration;

/// <summary>
/// Configuration for the standing cross-chain intent solver. Fail-closed by construction: the
/// solver does nothing unless explicitly enabled, and empty allowlists deny every intent rather
/// than filling anything that shows up.
/// </summary>
public sealed record CrossChainSolverOptions
{
    public const string SectionName = "CrossChainSolver";

    /// <summary>Master switch. The solver worker is idle while false.</summary>
    public bool Enabled { get; init; }

    public string SourceChainKey { get; init; } = string.Empty;
    public string SourceResolverAddress { get; init; } = string.Empty;
    public string DestinationChainKey { get; init; } = string.Empty;
    public string DestinationResolverAddress { get; init; } = string.Empty;

    /// <summary>
    /// The solver's own private key on the destination chain, used to sign fillIntent
    /// transactions and spend the solver's destination-chain inventory. Local development only —
    /// never logged, never committed. A real deployment must source it from a secret store or KMS.
    /// </summary>
    public string SolverPrivateKey { get; init; } = string.Empty;

    /// <summary>
    /// (sourceToken, destinationToken) pairs the solver will fill. An empty list denies every
    /// pair — an allowlist that silently means "allow all" when unset is how solver inventory
    /// gets drained by an intent the solver never meant to accept.
    /// </summary>
    public IReadOnlyList<TokenPair> AllowedTokenPairs { get; init; } = Array.Empty<TokenPair>();

    /// <summary>Largest amountIn the solver will fill for a single intent, in token base units.</summary>
    public decimal MaxAmountIn { get; init; }

    /// <summary>
    /// Basis points of amountIn the solver is willing to pay out at most (10000 = pay out exactly
    /// amountIn, no markup to the solver; less than 10000 means the solver keeps a spread). Filling
    /// at more than this ratio is refused rather than eating an unbounded loss.
    /// </summary>
    public int MaxOutputBps { get; init; } = 10000;

    public bool CanOperate => Enabled
        && !string.IsNullOrWhiteSpace(SourceChainKey)
        && !string.IsNullOrWhiteSpace(SourceResolverAddress)
        && !string.IsNullOrWhiteSpace(DestinationChainKey)
        && !string.IsNullOrWhiteSpace(DestinationResolverAddress)
        && !string.IsNullOrWhiteSpace(SolverPrivateKey);

    public bool IsTokenPairAllowed(string sourceToken, string destinationToken) =>
        AllowedTokenPairs.Any(p =>
            string.Equals(p.SourceToken, sourceToken, StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.DestinationToken, destinationToken, StringComparison.OrdinalIgnoreCase));

    public sealed record TokenPair(string SourceToken, string DestinationToken);
}
