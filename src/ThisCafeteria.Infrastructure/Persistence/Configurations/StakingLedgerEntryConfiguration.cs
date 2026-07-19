using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Configurations;

public sealed class StakingLedgerEntryConfiguration : IEntityTypeConfiguration<StakingLedgerEntry>
{
    public void Configure(EntityTypeBuilder<StakingLedgerEntry> builder)
    {
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.WalletAddress).HasMaxLength(128).IsRequired();
        builder.Property(entry => entry.ChainKey).HasMaxLength(64).IsRequired();
        builder.Property(entry => entry.Family).HasMaxLength(16).IsRequired();
        builder.Property(entry => entry.ActionType).HasMaxLength(32).IsRequired();
        builder.Property(entry => entry.Amount).HasPrecision(36, 18);
        builder.Property(entry => entry.AssetAmount).HasPrecision(36, 18);
        builder.Property(entry => entry.ShareAmount).HasPrecision(36, 18);
        builder.Property(entry => entry.RewardAmount).HasPrecision(36, 18);
        builder.Property(entry => entry.RawAssetAmount).HasMaxLength(80).IsRequired();
        builder.Property(entry => entry.RawShareAmount).HasMaxLength(80).IsRequired();
        builder.Property(entry => entry.RawRewardAmount).HasMaxLength(80).IsRequired();
        builder.Property(entry => entry.TransactionHash).HasMaxLength(256).IsRequired();
        builder.Property(entry => entry.VerificationState).HasMaxLength(32).IsRequired();
        builder.Property(entry => entry.NetworkName).HasMaxLength(80).IsRequired();
        builder.Property(entry => entry.PaymentTokenContract).HasMaxLength(128).IsRequired();
        builder.Property(entry => entry.StakingPoolContract).HasMaxLength(128).IsRequired();
        builder.Property(entry => entry.AssetIdentifier).HasMaxLength(128).IsRequired();
        builder.Property(entry => entry.ReceiptIdentifier).HasMaxLength(128).IsRequired();
        builder.Property(entry => entry.RewardIdentifier).HasMaxLength(128).IsRequired();
        builder.Property(entry => entry.VaultOrProgramIdentifier).HasMaxLength(128).IsRequired();
        builder.Property(entry => entry.ExplorerUrl).HasMaxLength(2_048);

        builder.HasIndex(entry => new { entry.WalletAddress, entry.RecordedAtUtc });
        builder.HasIndex(entry => new { entry.ChainKey, entry.TransactionHash, entry.OperationIndex }).IsUnique();
    }
}
