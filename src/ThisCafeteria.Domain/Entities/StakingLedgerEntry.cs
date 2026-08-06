namespace ThisCafeteria.Domain.Entities;

public sealed class StakingLedgerEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string WalletAddress { get; set; } = string.Empty;
    public string ChainKey { get; private set; } = "ethereum-sepolia";
    public string Family { get; set; } = "Evm";
    public string ActionType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal AssetAmount { get; set; }
    public decimal ShareAmount { get; set; }
    public decimal RewardAmount { get; set; }
    // Canonical on-chain quantities. Decimal fields remain display projections only.
    public string RawAssetAmount { get; set; } = "0";
    public string RawShareAmount { get; set; } = "0";
    public string RawRewardAmount { get; set; } = "0";
    public string TransactionHash { get; private set; } = string.Empty;
    public int OperationIndex { get; private set; }
    public int ChainId { get; set; }
    public string NetworkName { get; set; } = string.Empty;
    public string PaymentTokenContract { get; set; } = string.Empty;
    public string StakingPoolContract { get; set; } = string.Empty;
    public string AssetIdentifier { get; set; } = string.Empty;
    public string ReceiptIdentifier { get; set; } = string.Empty;
    public string RewardIdentifier { get; set; } = string.Empty;
    public string VaultOrProgramIdentifier { get; set; } = string.Empty;
    public long BlockOrSlot { get; set; }
    public string VerificationState { get; set; } = "verified";
    public bool Verified { get; set; }
    public DateTime? OccurredAtUtc { get; set; }
    public string ExplorerUrl { get; set; } = string.Empty;
    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;

    private StakingLedgerEntry() { }

    public static StakingLedgerEntry Create(
        string chainKey,
        string transactionHash,
        int operationIndex,
        Action<StakingLedgerEntry>? configure = null)
    {
        var identity = StakingOperationIdentity.Create(chainKey, transactionHash, operationIndex);
        var entry = new StakingLedgerEntry
        {
            ChainKey = identity.ChainKey,
            TransactionHash = identity.TransactionHash,
            OperationIndex = identity.OperationIndex
        };
        configure?.Invoke(entry);
        return entry;
    }

    /// <summary>
    /// The durable idempotency key. A transaction may contain several operations, but an
    /// operation can only be recorded once within its chain.
    /// </summary>
    public StakingOperationIdentity OperationIdentity =>
        StakingOperationIdentity.Create(ChainKey, TransactionHash, OperationIndex);
}

public sealed record StakingOperationIdentity(string ChainKey, string TransactionHash, int OperationIndex)
{
    public static StakingOperationIdentity Create(string chainKey, string transactionHash, int operationIndex)
    {
        if (string.IsNullOrWhiteSpace(chainKey) || string.IsNullOrWhiteSpace(transactionHash) || operationIndex < 0)
        {
            throw new InvalidOperationException("A staking ledger operation requires a chain, transaction hash, and non-negative operation index.");
        }

        return new StakingOperationIdentity(chainKey, transactionHash, operationIndex);
    }
}
