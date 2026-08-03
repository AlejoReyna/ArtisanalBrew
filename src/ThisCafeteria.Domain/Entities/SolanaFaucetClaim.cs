namespace ThisCafeteria.Domain.Entities;

/// <summary>
/// One recorded server-side CAFE devnet faucet mint, used to enforce the per-wallet cooldown. Unlike
/// the EVM faucet (a contract that enforces its own cooldown on-chain), the Solana faucet is an
/// authorized mint by the administrator, so the cooldown lives here.
/// </summary>
public sealed class SolanaFaucetClaim
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ChainKey { get; set; } = string.Empty;
    public string WalletAddress { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public long RawAmount { get; set; }
    public string Signature { get; set; } = string.Empty;
    public DateTime ClaimedAtUtc { get; set; } = DateTime.UtcNow;
}
