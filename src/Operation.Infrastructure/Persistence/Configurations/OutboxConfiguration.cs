using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GearFlow.Operation.Infrastructure.Persistence.Configurations;

internal sealed class OutboxConfiguration : IEntityTypeConfiguration<OutboxRow>
{
    public void Configure(EntityTypeBuilder<OutboxRow> builder)
    {
        builder.ToTable("outbox_events");
        builder.HasKey(row => row.EventId);
        builder.Property(row => row.Kind).HasConversion<string>().HasMaxLength(40);
        builder.Property(row => row.EventJson).HasColumnType("jsonb");
        builder.HasIndex(row => new { row.ServiceOrderId, row.Version }).IsUnique();
        builder.HasIndex(row => new { row.OccurredAt, row.EventId }).HasFilter("dispatched_at IS NULL");
        builder.HasOne<OperationRow>().WithMany().HasForeignKey(row => row.ServiceOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ReceiptRow>().WithMany().HasForeignKey(row => row.CausationId).OnDelete(DeleteBehavior.Restrict);
    }
}
