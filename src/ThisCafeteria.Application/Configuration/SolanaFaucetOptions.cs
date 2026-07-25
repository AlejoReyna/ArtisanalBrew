namespace ThisCafeteria.Application.Configuration;

/// <summary>
/// Operational policy for the Solana devnet CAFE faucet: how much CAFE a claim mints and how long a
/// wallet must wait between claims. These are server-side policy, not on-chain addresses, so they live
/// in configuration rather than the deployment manifest. The mint authority secret is NEVER here — it
/// is read from the <c>ARTISANALBREW_SOLANA_ADMIN_KEY</c> environment variable at claim time.
/// </summary>
public sealed class SolanaFaucetOptions
{
    public const string SectionName = "Blockchain:SolanaFaucet";

    /// <summary>CAFE minted per claim (whole tokens, converted to raw units using the manifest decimals).</summary>
    public decimal ClaimAmount { get; init; } = 100m;

    /// <summary>Minimum seconds between claims for one wallet.</summary>
    public int CooldownSeconds { get; init; } = 86_400;
}
