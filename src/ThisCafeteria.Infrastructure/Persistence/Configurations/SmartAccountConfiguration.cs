using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Configurations;

public class SmartAccountRecordConfiguration : IEntityTypeConfiguration<SmartAccountRecord>
{
    public void Configure(EntityTypeBuilder<SmartAccountRecord> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ChainKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.OwnerAddress).HasMaxLength(128).IsRequired();
        builder.Property(x => x.AccountAddress).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Salt).HasMaxLength(80).IsRequired();
        builder.Property(x => x.FactoryAddress).HasMaxLength(128);

        // One record per owner per account type per chain.
        builder.HasIndex(x => new { x.ChainKey, x.OwnerAddress, x.AccountType })
               .IsUnique()
               .HasDatabaseName("IX_SmartAccountRecords_ChainOwnerType");

        builder.HasIndex(x => new { x.ChainKey, x.AccountAddress })
               .HasDatabaseName("IX_SmartAccountRecords_ChainAddress");
    }
}

public class AgentPermissionEpochConfiguration : IEntityTypeConfiguration<AgentPermissionEpoch>
{
    public void Configure(EntityTypeBuilder<AgentPermissionEpoch> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ChainKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.DelegatorAddress).HasMaxLength(128).IsRequired();
        builder.Property(x => x.OwnerAddress).HasMaxLength(128).IsRequired();
        builder.Property(x => x.AgentAddress).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Epoch).HasMaxLength(80).IsRequired();
        builder.Property(x => x.InstalledTxHash).HasMaxLength(128);
        builder.Property(x => x.RevokedTxHash).HasMaxLength(128);

        // One row per (account, epoch value) — the epoch counter never repeats for an account.
        builder.HasIndex(x => new { x.ChainKey, x.SmartAccountRecordId, x.Epoch })
               .IsUnique()
               .HasDatabaseName("IX_AgentPermissionEpochs_AccountEpoch");

        builder.HasIndex(x => new { x.ChainKey, x.DelegatorAddress })
               .HasDatabaseName("IX_AgentPermissionEpochs_ChainDelegator");

        builder.HasMany<AgentPermissionGrant>()
               .WithOne()
               .HasForeignKey(x => x.EpochId)
               .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AgentPermissionGrantConfiguration : IEntityTypeConfiguration<AgentPermissionGrant>
{
    public void Configure(EntityTypeBuilder<AgentPermissionGrant> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TargetAddress).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Selector).HasMaxLength(10).IsRequired();
        builder.Property(x => x.TokenAddress).HasMaxLength(128);
        builder.Property(x => x.AmountWei).HasMaxLength(80).IsRequired();
        builder.Property(x => x.DelegationHash).HasMaxLength(66).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(64);

        builder.HasIndex(x => x.EpochId).HasDatabaseName("IX_AgentPermissionGrants_Epoch");
        builder.HasIndex(x => x.DelegationHash).HasDatabaseName("IX_AgentPermissionGrants_DelegationHash");
    }
}
