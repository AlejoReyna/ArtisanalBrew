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

        builder.HasIndex(claim => new { claim.WalletAddress, claim.ClaimedAtUtc });
        builder.HasIndex(claim => claim.TransactionHash);
        builder.HasIndex(claim => claim.PaymentTransactionHash);
    }
}
