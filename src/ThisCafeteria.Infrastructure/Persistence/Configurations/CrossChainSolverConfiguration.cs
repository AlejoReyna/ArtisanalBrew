using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Configurations;

public class CrossChainSolverCheckpointConfiguration : IEntityTypeConfiguration<CrossChainSolverCheckpoint>
{
    public void Configure(EntityTypeBuilder<CrossChainSolverCheckpoint> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SourceChainKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceResolverAddress).HasMaxLength(128).IsRequired();

        builder.HasIndex(x => new { x.SourceChainKey, x.SourceResolverAddress })
               .IsUnique()
               .HasDatabaseName("IX_CrossChainSolverCheckpoints_SourceChainResolver");
    }
}

public class CrossChainSolverFillConfiguration : IEntityTypeConfiguration<CrossChainSolverFill>
{
    public void Configure(EntityTypeBuilder<CrossChainSolverFill> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SourceChainKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceResolverAddress).HasMaxLength(128).IsRequired();
        builder.Property(x => x.OrderId).HasMaxLength(66).IsRequired();
        builder.Property(x => x.SubmitTransactionHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.FillTransactionHash).HasMaxLength(128);
        builder.Property(x => x.DenialReason).HasMaxLength(256);

        // One evaluation per orderId per resolver — the worker must never re-evaluate (and never
        // double-fill) an intent it has already decided on, even across restarts.
        builder.HasIndex(x => new { x.SourceChainKey, x.SourceResolverAddress, x.OrderId })
               .IsUnique()
               .HasDatabaseName("IX_CrossChainSolverFills_Identity");
    }
}
