using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Configurations;

public class AgentDirectoryEntryConfiguration : IEntityTypeConfiguration<AgentDirectoryEntry>
{
    public void Configure(EntityTypeBuilder<AgentDirectoryEntry> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ChainKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.RegistryAddress).HasMaxLength(128).IsRequired();
        builder.Property(x => x.OwnerAddress).HasMaxLength(128).IsRequired();
        builder.Property(x => x.MetadataUri).HasMaxLength(2048).IsRequired();

        builder.HasIndex(x => new { x.ChainKey, x.RegistryAddress, x.AgentId }).IsUnique();
        builder.HasIndex(x => x.OwnerAddress);
    }
}
