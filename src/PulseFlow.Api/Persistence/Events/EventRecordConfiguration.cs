using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PulseFlow.Api.Persistence.Events;

public sealed class EventRecordConfiguration : IEntityTypeConfiguration<EventRecord>
{
    public void Configure(EntityTypeBuilder<EventRecord> builder)
    {
        builder.ToTable("events");

        builder.HasKey(eventRecord => eventRecord.Id);

        builder.Property(eventRecord => eventRecord.Id)
            .HasColumnName("id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .IsRequired();

        builder.Property(eventRecord => eventRecord.EventId)
            .HasColumnName("event_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(eventRecord => eventRecord.Type)
            .HasColumnName("type")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(eventRecord => eventRecord.Source)
            .HasColumnName("source")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(eventRecord => eventRecord.OccurredAt)
            .HasColumnName("occurred_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(eventRecord => eventRecord.ReceivedAt)
            .HasColumnName("received_at")
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(eventRecord => eventRecord.PayloadJson)
            .HasColumnName("payload")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.HasIndex(eventRecord => new { eventRecord.Source, eventRecord.EventId })
            .IsUnique();
    }
}
