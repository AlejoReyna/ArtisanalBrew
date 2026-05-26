using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ThisCafeteria.Application.Configuration;
using ThisCafeteria.Application.DTOs;
using ThisCafeteria.Application.Repositories;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Application.Services;

public sealed class TransparencyService(
    ITransparencyRecordRepository repository,
    BlockchainNetworkOptions chainOptions) : ITransparencyService
{
    public async Task CreatePendingRecordsForOrderAsync(Order order, CancellationToken cancellationToken = default)
    {
        if (order.Items.Count == 0)
        {
            return;
        }

        var orderHash = CreateOrderHash(order);
        var records = order.Items.Select(item => new TransparencyRecord
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            ProductName = item.ProductName,
            Quantity = item.Quantity,
            Total = item.Total,
            OrderHash = orderHash,
            ChainId = chainOptions.ChainId,
            NetworkName = chainOptions.NetworkName,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        }).ToArray();

        await repository.AddRangeAsync(records, cancellationToken);
        order.TransparencyRecords.AddRange(records);
    }

    public async Task<IReadOnlyCollection<TransparencyRecordDto>> GetRecentPurchasesAsync(
        int count = 25,
        CancellationToken cancellationToken = default)
    {
        var records = await repository.GetRecentAsync(count, cancellationToken);
        return records.Select(Map).ToArray();
    }

    private static string CreateOrderHash(Order order)
    {
        var items = order.Items
            .OrderBy(item => item.ProductName, StringComparer.Ordinal)
            .Select(item => string.Join(
                ':',
                item.ProductId,
                item.ProductName,
                item.Quantity.ToString(CultureInfo.InvariantCulture),
                item.UnitPrice.ToString(CultureInfo.InvariantCulture),
                item.Total.ToString(CultureInfo.InvariantCulture)));

        var payload = string.Join('|', order.OrderNumber, order.Total.ToString(CultureInfo.InvariantCulture), string.Join(';', items));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return $"0x{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static TransparencyRecordDto Map(TransparencyRecord record) => new(
        record.Id,
        record.OrderId,
        record.OrderNumber,
        record.ProductName,
        record.Quantity,
        record.Total,
        record.OrderHash,
        record.ChainId,
        record.NetworkName,
        record.ContractAddress,
        record.TransactionHash,
        record.ExplorerUrl,
        record.Status,
        record.CreatedAt,
        record.RecordedOnChainAt);
}
