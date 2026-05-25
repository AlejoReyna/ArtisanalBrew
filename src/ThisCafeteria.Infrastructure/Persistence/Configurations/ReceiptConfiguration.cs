using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Configurations;

public sealed class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        builder.HasKey(receipt => receipt.Id);
        builder.Property(receipt => receipt.FileUrl).HasMaxLength(2_048).IsRequired();

        builder.HasOne(receipt => receipt.Order)
            .WithOne(order => order.Receipt)
            .HasForeignKey<Receipt>(receipt => receipt.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
