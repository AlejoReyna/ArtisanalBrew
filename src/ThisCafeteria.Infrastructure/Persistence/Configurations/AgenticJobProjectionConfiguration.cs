using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Configurations;

public class AgenticJobProjectionConfiguration : IEntityTypeConfiguration<AgenticJobProjection>
{
    public void Configure(EntityTypeBuilder<AgenticJobProjection> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ChainKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.EscrowAddress).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ClientAddress).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ProviderAddress).HasMaxLength(128).IsRequired();
        builder.Property(x => x.EvaluatorAddress).HasMaxLength(128).IsRequired();
        builder.Property(x => x.DescriptionCommitment).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.DeliverableCommitment).HasMaxLength(1024);
        builder.Property(x => x.DecisionReason).HasMaxLength(1024);
        
        // Use the on-chain identity for unique index since JobId is auto-increment internal ID
        builder.HasIndex(x => new { x.ChainKey, x.ContractAddress, x.OnChainJobId }).IsUnique();
        builder.HasIndex(x => x.ClientAddress);
        builder.HasIndex(x => x.ProviderAddress);
        builder.HasIndex(x => x.EvaluatorAddress);
    }
}
