using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using GearFlow.Operation.Infrastructure.Messaging;

namespace GearFlow.Operation.Infrastructure.Persistence.Configurations;

internal sealed class BrokerInboxConfiguration : IEntityTypeConfiguration<BrokerInboxRow>
{
    public void Configure(EntityTypeBuilder<BrokerInboxRow> builder)
    {
        builder.ToTable("broker_inbox", table => table.HasCheckConstraint("ck_broker_inbox_hash", "length(fingerprint) = 64"));
        builder.HasKey(row => row.MessageId);
        builder.Property(row => row.Fingerprint).HasMaxLength(64);
        builder.Property(row => row.CommandName).HasMaxLength(100);
        builder.Property(row => row.TraceParent).HasMaxLength(55);
        builder.Property(row => row.TraceState).HasMaxLength(512);
        builder.Property(row => row.ReplyType).HasMaxLength(100);
        builder.Property(row => row.ReplyJson).HasColumnType("jsonb");
        builder.HasIndex(row => new { row.IsCompleted, row.ReceivedAt });
        builder.HasIndex(row => new { row.ProcessedAt, row.MessageId }).HasFilter("reply_json IS NOT NULL AND published_at IS NULL AND is_completed");
    }
}
