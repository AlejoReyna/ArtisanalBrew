namespace ThisCafeteria.Application.Services.Blockchain;

public sealed class CafeFaucetStatus
{
    public bool IsConfigured { get; init; }
    public decimal ClaimAmount { get; init; }
    public int CooldownSeconds { get; init; }
    public DateTime? NextClaimAtUtc { get; init; }
    public bool CanClaim { get; init; }
    public decimal FaucetBalance { get; init; }
}
