namespace ThisCafeteria.Application.Services.Blockchain;

public sealed record StakingVerificationResult(
    TransactionVerificationStatus Status,
    decimal Amount,
    int Confirmations = 0,
    int RequiredConfirmations = 0)
{
    public bool Verified => Status == TransactionVerificationStatus.Verified;

    public static readonly StakingVerificationResult Failed = new(TransactionVerificationStatus.Failed, 0m);

    public static StakingVerificationResult Pending(int confirmations, int requiredConfirmations) =>
        new(TransactionVerificationStatus.PendingConfirmations, 0m, confirmations, requiredConfirmations);
}
