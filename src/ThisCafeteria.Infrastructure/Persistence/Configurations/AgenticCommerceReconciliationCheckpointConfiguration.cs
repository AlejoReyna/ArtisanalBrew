using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Configurations;

public class AgenticCommerceReconciliationCheckpointConfiguration
    : IEntityTypeConfiguration<AgenticCommerceReconciliationCheckpoint>
{
    public void Configure(EntityTypeBuilder<AgenticCommerceReconciliationCheckpoint> builder)
    {
        builder.HasKey(e => e.Id);
        builder.HasIndex(e => new { e.ChainKey, e.EscrowAddress }).IsUnique();
        builder.Property(e => e.ChainKey).HasMaxLength(64).IsRequired();
        builder.Property(e => e.EscrowAddress).HasMaxLength(128).IsRequired();
    }
}
