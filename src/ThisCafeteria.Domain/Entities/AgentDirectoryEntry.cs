namespace ThisCafeteria.Domain.Entities;

public class AgentDirectoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ChainKey { get; set; } = string.Empty;
    public string RegistryAddress { get; set; } = string.Empty;
    public long AgentId { get; set; }
    public string OwnerAddress { get; set; } = string.Empty;
    public string MetadataUri { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
