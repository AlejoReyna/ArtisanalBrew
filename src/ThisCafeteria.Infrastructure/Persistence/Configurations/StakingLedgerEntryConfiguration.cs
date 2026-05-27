using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Configurations;

public sealed class StakingLedgerEntryConfiguration : IEntityTypeConfiguration<StakingLedgerEntry>
{
    public void Configure(EntityTypeBuilder<StakingLedgerEntry> builder)
    {
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.WalletAddress).HasMaxLength(42).IsRequired();
        builder.Property(entry => entry.ActionType).HasMaxLength(16).IsRequired();
        builder.Property(entry => entry.Amount).HasPrecision(36, 18);
        builder.Property(entry => entry.TransactionHash).HasMaxLength(66).IsRequired();
        builder.Property(entry => entry.NetworkName).HasMaxLength(80).IsRequired();
        builder.Property(entry => entry.PaymentTokenContract).HasMaxLength(42).IsRequired();
        builder.Property(entry => entry.StakingPoolContract).HasMaxLength(42).IsRequired();
        builder.Property(entry => entry.ExplorerUrl).HasMaxLength(2_048);

        builder.HasIndex(entry => new { entry.WalletAddress, entry.RecordedAtUtc });
        builder.HasIndex(entry => entry.TransactionHash).IsUnique();
    }
}
