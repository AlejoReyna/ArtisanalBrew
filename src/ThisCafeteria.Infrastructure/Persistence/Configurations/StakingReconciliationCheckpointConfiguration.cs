using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Configurations;

public sealed class StakingReconciliationCheckpointConfiguration : IEntityTypeConfiguration<StakingReconciliationCheckpoint>
{
    public void Configure(EntityTypeBuilder<StakingReconciliationCheckpoint> builder)
    {
        builder.HasKey(checkpoint => checkpoint.Id);
        builder.Property(checkpoint => checkpoint.ChainKey).HasMaxLength(64).IsRequired();
        builder.Property(checkpoint => checkpoint.Family).HasMaxLength(16).IsRequired();
        builder.Property(checkpoint => checkpoint.SourceIdentifier).HasMaxLength(128).IsRequired();
        builder.Property(checkpoint => checkpoint.CursorType).HasMaxLength(16).IsRequired();
        builder.Property(checkpoint => checkpoint.StakingPoolContract).HasMaxLength(128).IsRequired();
        builder.Property(checkpoint => checkpoint.LastScannedSignature).HasMaxLength(256).IsRequired();

        builder.HasIndex(checkpoint => new { checkpoint.ChainKey, checkpoint.SourceIdentifier }).IsUnique();
    }
}
