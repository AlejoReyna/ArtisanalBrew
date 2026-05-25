using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Configurations;

public sealed class TransparencyRecordConfiguration : IEntityTypeConfiguration<TransparencyRecord>
{
    public void Configure(EntityTypeBuilder<TransparencyRecord> builder)
    {
        builder.HasKey(record => record.Id);
        builder.Property(record => record.OrderNumber).HasMaxLength(64).IsRequired();
        builder.Property(record => record.ProductName).HasMaxLength(200).IsRequired();
        builder.Property(record => record.Total).HasPrecision(18, 2);
        builder.Property(record => record.OrderHash).HasMaxLength(66).IsRequired();
        builder.Property(record => record.NetworkName).HasMaxLength(80).IsRequired();
        builder.Property(record => record.ContractAddress).HasMaxLength(42);
        builder.Property(record => record.TransactionHash).HasMaxLength(66);
        builder.Property(record => record.ExplorerUrl).HasMaxLength(2_048);
        builder.Property(record => record.Status).HasMaxLength(40).IsRequired();

        builder.HasIndex(record => record.OrderHash);
        builder.HasIndex(record => record.TransactionHash);

        builder.HasOne(record => record.Order)
            .WithMany(order => order.TransparencyRecords)
            .HasForeignKey(record => record.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
