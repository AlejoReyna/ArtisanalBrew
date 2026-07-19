namespace ThisCafeteria.Domain.Entities;

public sealed class WalletIdentity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public string NormalizedAddress { get; set; } = string.Empty;
    public string DisplayAddress { get; set; } = string.Empty;
    public string WalletProvider { get; set; } = string.Empty;
    public DateTimeOffset VerifiedAtUtc { get; set; }
}
