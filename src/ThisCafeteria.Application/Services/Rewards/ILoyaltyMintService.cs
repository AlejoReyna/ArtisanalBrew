using ThisCafeteria.Application.Configuration;

namespace ThisCafeteria.Application.Services.Rewards;

public enum LoyaltyMintStatus
{
    Minted,

    /// <summary>The payment transaction has already been used to claim a reward.</summary>
    AlreadyClaimed,

    PendingConfirmations,
    PaymentNotVerified,
    MintingNotConfigured,

    /// <summary>The on-chain mint failed. Nothing is recorded - the claim is rolled back.</summary>
    MintFailed
}

public sealed record LoyaltyMintResult(
    LoyaltyMintStatus Status,
    string? MintTransactionHash = null,
    string? Error = null)
{
    public static LoyaltyMintResult Minted(string mintTransactionHash) =>
        new(LoyaltyMintStatus.Minted, mintTransactionHash);

    public static readonly LoyaltyMintResult AlreadyClaimed = new(LoyaltyMintStatus.AlreadyClaimed);
    public static readonly LoyaltyMintResult PendingConfirmations = new(LoyaltyMintStatus.PendingConfirmations);
    public static readonly LoyaltyMintResult PaymentNotVerified = new(LoyaltyMintStatus.PaymentNotVerified);
    public static readonly LoyaltyMintResult MintingNotConfigured = new(LoyaltyMintStatus.MintingNotConfigured);

    public static LoyaltyMintResult MintFailed(string error) =>
        new(LoyaltyMintStatus.MintFailed, Error: error);
}

public sealed record LoyaltyMintCommand(
    string WalletAddress,
    decimal Amount,
    decimal PaymentAmount,
    string PaymentTransactionHash,
    string? AllocationName);

/// <summary>
/// Verifies a payment, records the resulting reward claim, and mints the reward on chain.
///
/// The three steps are one atomic unit: the claim row is written before the mint so a concurrent
/// request cannot claim the same payment, and if the mint then fails the whole thing rolls back
/// rather than leaving a claim recorded for tokens that were never issued.
/// </summary>
public interface ILoyaltyMintService
{
    Task<LoyaltyMintResult> MintAsync(
        BlockchainNetworkOptions chain,
        LoyaltyMintCommand command,
        CancellationToken cancellationToken = default);
}
