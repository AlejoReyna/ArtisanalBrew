namespace ThisCafeteria.Domain.Entities;

public sealed class Receipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public string FileUrl { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EmailSentAt { get; set; }

    public Order? Order { get; set; }
}
