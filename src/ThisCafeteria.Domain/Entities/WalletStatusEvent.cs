namespace ThisCafeteria.Domain.Entities;

public sealed class WalletStatusEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string WalletAddress { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? EventType { get; set; }
    public string? PayloadJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedToAwsAtUtc { get; set; }
    public string? AwsMessageId { get; set; }
}
