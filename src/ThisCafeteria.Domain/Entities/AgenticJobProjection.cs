using System.ComponentModel.DataAnnotations;

namespace ThisCafeteria.Domain.Entities;

public class AgenticJobProjection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ChainKey { get; set; } = string.Empty;
    public string EscrowAddress { get; set; } = string.Empty;
    public long JobId { get; set; }
    public string ClientAddress { get; set; } = string.Empty;
    public string ProviderAddress { get; set; } = string.Empty;
    public string EvaluatorAddress { get; set; } = string.Empty;
    public string DescriptionCommitment { get; set; } = string.Empty;
    public decimal Budget { get; set; }
    public long ExpiredAt { get; set; }
    public const string StatusOpen = "Open";
    public const string StatusFunded = "Funded";
    public const string StatusSubmitted = "Submitted";
    public const string StatusCompleted = "Completed";
    public const string StatusRejected = "Rejected";
    public const string StatusExpired = "Expired";

    public string Status { get; set; } = StatusOpen; // Open, Funded, Submitted, Completed, Rejected, Expired
    public string? DeliverableCommitment { get; set; }
    public string? DecisionReason { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // --- On-chain tracking (populated by reconciliation worker) ---

    /// <summary>On-chain job ID from the escrow contract's jobCounter.</summary>
    public long OnChainJobId { get; set; }

    /// <summary>EVM chain ID where the escrow contract is deployed.</summary>
    public long ChainId { get; set; }

    /// <summary>Address of the escrow contract.</summary>
    public string ContractAddress { get; set; } = string.Empty;

    /// <summary>Transaction hash of the JobCreated event.</summary>
    public string CreationTransactionHash { get; set; } = string.Empty;

    /// <summary>Transaction hash of the JobFunded event.</summary>
    public string FundedTransactionHash { get; set; } = string.Empty;

    /// <summary>Transaction hash of the terminal event (Completed/Rejected/Expired).</summary>
    public string CompletionTransactionHash { get; set; } = string.Empty;

    /// <summary>Block number of the last reconciled event.</summary>
    public long LastReconciledBlock { get; set; }

    /// <summary>Optimistic concurrency token to prevent race conditions between worker and API.</summary>
    [ConcurrencyCheck]
    public long ConcurrencyToken { get; set; }
}

