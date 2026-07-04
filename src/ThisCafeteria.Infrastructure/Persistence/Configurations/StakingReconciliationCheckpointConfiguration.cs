using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Configurations;

public sealed class StakingReconciliationCheckpointConfiguration : IEntityTypeConfiguration<StakingReconciliationCheckpoint>
{
    public void Configure(EntityTypeBuilder<StakingReconciliationCheckpoint> builder)
    {
        builder.HasKey(checkpoint => checkpoint.Id);
        builder.Property(checkpoint => checkpoint.StakingPoolContract).HasMaxLength(42).IsRequired();

        builder.HasIndex(checkpoint => checkpoint.StakingPoolContract).IsUnique();
    }
}
