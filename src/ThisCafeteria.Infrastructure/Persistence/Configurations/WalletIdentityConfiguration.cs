using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Identity;

namespace ThisCafeteria.Infrastructure.Persistence.Configurations;

public sealed class WalletIdentityConfiguration : IEntityTypeConfiguration<WalletIdentity>
{
    public void Configure(EntityTypeBuilder<WalletIdentity> builder)
    {
        builder.HasKey(identity => identity.Id);
        builder.Property(identity => identity.UserId).HasMaxLength(450).IsRequired();
        builder.Property(identity => identity.Family).HasMaxLength(16).IsRequired();
        builder.Property(identity => identity.NormalizedAddress).HasMaxLength(128).IsRequired();
        builder.Property(identity => identity.DisplayAddress).HasMaxLength(128).IsRequired();
        builder.Property(identity => identity.WalletProvider).HasMaxLength(64).IsRequired();
        builder.HasIndex(identity => new { identity.Family, identity.NormalizedAddress }).IsUnique();
        builder.HasIndex(identity => identity.UserId);
        builder.HasOne<ApplicationUser>().WithMany(user => user.WalletIdentities).HasForeignKey(identity => identity.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
