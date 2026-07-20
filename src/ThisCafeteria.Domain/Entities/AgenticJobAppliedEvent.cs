using System.ComponentModel.DataAnnotations;

namespace ThisCafeteria.Domain.Entities;

public class AgenticJobAppliedEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    
    [MaxLength(64)]
    public string ChainKey { get; set; } = string.Empty;
    
    [MaxLength(128)]
    public string ContractAddress { get; set; } = string.Empty;
    
    [MaxLength(128)]
    public string TransactionHash { get; set; } = string.Empty;
    
    public int LogIndex { get; set; }
    
    public DateTime AppliedAtUtc { get; set; } = DateTime.UtcNow;
}
