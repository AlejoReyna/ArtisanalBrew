using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ThisCafeteria.Domain.Entities;

namespace ThisCafeteria.Infrastructure.Persistence.Configurations;

public sealed class WalletStatusEventConfiguration : IEntityTypeConfiguration<WalletStatusEvent>
{
    public void Configure(EntityTypeBuilder<WalletStatusEvent> builder)
    {
        builder.ToTable("wallet_status_events");

        builder.HasKey(statusEvent => statusEvent.Id);
        builder.Property(statusEvent => statusEvent.Id).HasColumnName("id");
        builder.Property(statusEvent => statusEvent.WalletAddress)
            .HasColumnName("wallet_address")
            .HasColumnType("text")
            .IsRequired();
        builder.Property(statusEvent => statusEvent.Status)
            .HasColumnName("status")
            .HasColumnType("text")
            .IsRequired();
        builder.Property(statusEvent => statusEvent.EventType)
            .HasColumnName("event_type")
            .HasColumnType("text");
        builder.Property(statusEvent => statusEvent.PayloadJson)
            .HasColumnName("payload_json")
            .HasColumnType("jsonb");
        builder.Property(statusEvent => statusEvent.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()");
        builder.Property(statusEvent => statusEvent.PublishedToAwsAtUtc)
            .HasColumnName("published_to_aws_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(statusEvent => statusEvent.AwsMessageId)
            .HasColumnName("aws_message_id")
            .HasColumnType("text");

        builder.HasIndex(statusEvent => new { statusEvent.WalletAddress, statusEvent.CreatedAt })
            .HasDatabaseName("ix_wallet_status_events_wallet_created_at");
        builder.HasIndex(statusEvent => statusEvent.Status);
        builder.HasIndex(statusEvent => statusEvent.AwsMessageId);
    }
}
