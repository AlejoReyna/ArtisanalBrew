using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ThisCafeteria.Domain.Entities;
using ThisCafeteria.Infrastructure.Identity;

namespace ThisCafeteria.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Cart> Carts => Set<Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
    public DbSet<Receipt> Receipts => Set<Receipt>();
    public DbSet<TransparencyRecord> TransparencyRecords => Set<TransparencyRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.WalletAddress).HasMaxLength(42);
            entity.HasIndex(user => user.WalletAddress)
                .IsUnique()
                .HasFilter("\"WalletAddress\" IS NOT NULL");
        });
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        SeedData.Configure(builder);
    }
}
