namespace ThisCafeteria.Application.DTOs;

public sealed record TransparencyRecordDto(
    Guid Id,
    Guid OrderId,
    string OrderNumber,
    string ProductName,
    int Quantity,
    decimal Total,
    string OrderHash,
    int ChainId,
    string NetworkName,
    string ContractAddress,
    string TransactionHash,
    string ExplorerUrl,
    string Status,
    DateTime CreatedAt,
    DateTime? RecordedOnChainAt);
