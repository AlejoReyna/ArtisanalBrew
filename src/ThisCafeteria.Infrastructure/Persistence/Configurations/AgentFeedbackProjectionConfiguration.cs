using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Configurations;

public class AgentFeedbackProjectionConfiguration : IEntityTypeConfiguration<AgentFeedbackProjection>
{
    public void Configure(EntityTypeBuilder<AgentFeedbackProjection> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ChainKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.RegistryAddress).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ReviewerAddress).HasMaxLength(128).IsRequired();
        builder.Property(x => x.CommentUri).HasMaxLength(2048).IsRequired();
        
        builder.HasIndex(x => new { x.ChainKey, x.RegistryAddress, x.AgentId, x.JobId }).IsUnique();
        builder.HasIndex(x => x.ReviewerAddress);
    }
}
