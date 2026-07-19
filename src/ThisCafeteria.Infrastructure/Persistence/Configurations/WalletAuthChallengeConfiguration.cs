using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Configurations;

public sealed class WalletAuthChallengeConfiguration : IEntityTypeConfiguration<WalletAuthChallenge>
{
    public void Configure(EntityTypeBuilder<WalletAuthChallenge> builder)
    {
        builder.HasKey(item => item.Id);
        builder.Property(item => item.NonceHash).HasMaxLength(64).IsRequired();
        builder.Property(item => item.PublicKey).HasMaxLength(128).IsRequired();
        builder.Property(item => item.ChainKey).HasMaxLength(64).IsRequired();
        builder.Property(item => item.Origin).HasMaxLength(512).IsRequired();
        builder.Property(item => item.MessageHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(item => item.NonceHash).IsUnique();
        builder.HasIndex(item => new { item.ExpiresAtUtc, item.ConsumedAtUtc });
        builder.HasIndex(item => new { item.PublicKey, item.ChainKey });
    }
}
