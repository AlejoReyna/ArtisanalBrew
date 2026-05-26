using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Configurations;

public sealed class RewardClaimConfiguration : IEntityTypeConfiguration<RewardClaim>
{
    public void Configure(EntityTypeBuilder<RewardClaim> builder)
    {
        builder.HasKey(claim => claim.Id);
        builder.Property(claim => claim.WalletAddress).HasMaxLength(42).IsRequired();
        builder.Property(claim => claim.Amount).HasPrecision(36, 18);
        builder.Property(claim => claim.ClaimType).HasMaxLength(32).IsRequired();
        builder.Property(claim => claim.TransactionHash).HasMaxLength(66);
        builder.Property(claim => claim.PaymentTransactionHash).HasMaxLength(66);
        builder.Property(claim => claim.PaymentAmount).HasPrecision(36, 18);
        builder.Property(claim => claim.PaymentNetworkName).HasMaxLength(80);
        builder.Property(claim => claim.PaymentTokenContract).HasMaxLength(42);
        builder.Property(claim => claim.MarketplaceWallet).HasMaxLength(42);
        builder.Property(claim => claim.AllocationName).HasMaxLength(120);
        builder.Property(claim => claim.PaymentExplorerUrl).HasMaxLength(2_048);
        builder.Property(claim => claim.MintExplorerUrl).HasMaxLength(2_048);

        builder.HasIndex(claim => new { claim.WalletAddress, claim.ClaimedAtUtc });
        builder.HasIndex(claim => claim.TransactionHash);
        builder.HasIndex(claim => claim.PaymentTransactionHash)
            .IsUnique()
            .HasFilter("\"PaymentTransactionHash\" IS NOT NULL");
    }
}
