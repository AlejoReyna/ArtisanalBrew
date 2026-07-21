using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Configurations;

public class AgenticJobDeferredEventConfiguration : IEntityTypeConfiguration<AgenticJobDeferredEvent>
{
    public void Configure(EntityTypeBuilder<AgenticJobDeferredEvent> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ChainKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ContractAddress).HasMaxLength(128).IsRequired();
        builder.Property(x => x.EventType).HasMaxLength(64).IsRequired();
        builder.Property(x => x.TransactionHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.DeferralReason).HasMaxLength(512).IsRequired();

        // Unique log identity – prevents recording the same deferred event twice.
        builder.HasIndex(x => new { x.ChainKey, x.ContractAddress, x.TransactionHash, x.LogIndex })
               .IsUnique()
               .HasDatabaseName("IX_AgenticJobDeferredEvents_LogIdentity");

        // Support lookup by job for re-application or diagnostics.
        builder.HasIndex(x => new { x.ChainKey, x.ContractAddress, x.OnChainJobId })
               .HasDatabaseName("IX_AgenticJobDeferredEvents_Job");
    }
}
