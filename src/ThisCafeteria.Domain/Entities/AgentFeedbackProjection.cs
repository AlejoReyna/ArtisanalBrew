namespace ThisCafeteria.Domain.Entities;

public class AgentFeedbackProjection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ChainKey { get; set; } = string.Empty;
    public string RegistryAddress { get; set; } = string.Empty;
    public long AgentId { get; set; }
    public long JobId { get; set; }
    public string ReviewerAddress { get; set; } = string.Empty;
    public long Score { get; set; }
    public string CommentUri { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
