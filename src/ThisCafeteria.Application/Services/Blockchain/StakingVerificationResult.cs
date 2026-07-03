namespace ThisCafeteria.Application.Services.Blockchain;

public sealed record StakingVerificationResult(bool Verified, decimal Amount)
{
    public static readonly StakingVerificationResult Failed = new(false, 0m);
}
