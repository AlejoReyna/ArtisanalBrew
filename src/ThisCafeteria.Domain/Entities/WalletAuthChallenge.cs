namespace ThisCafeteria.Domain.Entities;

public sealed class WalletAuthChallenge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string NonceHash { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string ChainKey { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string MessageHash { get; set; } = string.Empty;
    public DateTimeOffset IssuedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? ConsumedAtUtc { get; set; }
    public int VerificationAttempts { get; set; }
}
