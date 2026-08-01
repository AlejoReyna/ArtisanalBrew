using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Configurations;

public sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Email).HasMaxLength(320).IsRequired();
        builder.HasIndex(user => user.Email).IsUnique();
        builder.Property(user => user.DisplayName).HasMaxLength(160).IsRequired();
        builder.Property(user => user.Role).HasConversion<string>().HasMaxLength(80);

        // One jsonb column rather than six text columns: the slot list is a
        // presentation concern that will keep growing, and nothing queries an
        // avatar by what it is wearing. The navigation stays optional — a NULL
        // here means "never edited", which is what makes the page fall back to
        // the wallet-derived seed instead of to a hardcoded default robot.
        builder.OwnsOne(user => user.Avatar, avatar =>
        {
            avatar.ToJson("Avatar");

            // The ids are catalog keys, not free text. The cap is a guard on
            // what a bad write could put here; the read path still runs every
            // value through AvatarCatalog.Normalize before rendering it.
            avatar.Property(look => look.Chassis).HasMaxLength(32);
            avatar.Property(look => look.Visor).HasMaxLength(32);
            avatar.Property(look => look.Hat).HasMaxLength(32);
            avatar.Property(look => look.Wear).HasMaxLength(32);
            avatar.Property(look => look.Hold).HasMaxLength(32);
            avatar.Property(look => look.Backdrop).HasMaxLength(32);
        });
    }
}
