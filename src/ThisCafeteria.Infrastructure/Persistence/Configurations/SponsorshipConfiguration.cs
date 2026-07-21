using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Configurations;

public class SponsorshipGrantConfiguration : IEntityTypeConfiguration<SponsorshipGrant>
{
    public void Configure(EntityTypeBuilder<SponsorshipGrant> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ChainKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.OwnerAddress).HasMaxLength(128).IsRequired();
        builder.Property(x => x.BudgetUsd).HasPrecision(18, 6);
        builder.Property(x => x.SpentUsd).HasPrecision(18, 6);
        builder.Property(x => x.MaxOperationCostUsd).HasPrecision(18, 6);

        // RemainingUsd is derived from BudgetUsd/SpentUsd and must not be persisted.
        builder.Ignore(x => x.RemainingUsd);

        // One grant per owner per chain.
        builder.HasIndex(x => new { x.ChainKey, x.OwnerAddress })
               .IsUnique()
               .HasDatabaseName("IX_SponsorshipGrants_ChainOwner");
    }
}

public class SponsorshipUsageConfiguration : IEntityTypeConfiguration<SponsorshipUsage>
{
    public void Configure(EntityTypeBuilder<SponsorshipUsage> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ChainKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.OwnerAddress).HasMaxLength(128).IsRequired();
        builder.Property(x => x.TargetAddress).HasMaxLength(128);
        builder.Property(x => x.Selector).HasMaxLength(10);
        builder.Property(x => x.CostUsd).HasPrecision(18, 6);

        builder.HasIndex(x => x.GrantId).HasDatabaseName("IX_SponsorshipUsages_Grant");
        builder.HasIndex(x => new { x.ChainKey, x.OwnerAddress })
               .HasDatabaseName("IX_SponsorshipUsages_ChainOwner");
    }
}
