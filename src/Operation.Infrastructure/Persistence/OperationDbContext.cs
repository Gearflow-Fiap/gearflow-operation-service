using Microsoft.EntityFrameworkCore;
using GearFlow.Operation.Infrastructure.Messaging;
using System.Text.RegularExpressions;

namespace GearFlow.Operation.Infrastructure.Persistence;

public sealed class OperationDbContext(DbContextOptions<OperationDbContext> options) : DbContext(options)
{
    internal DbSet<OperationRow> Operations => Set<OperationRow>();
    internal DbSet<HistoryRow> Histories => Set<HistoryRow>();
    internal DbSet<ReceiptRow> Receipts => Set<ReceiptRow>();
    internal DbSet<OutboxRow> Outbox => Set<OutboxRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("operation");
        var inbox = model.Entity<BrokerInboxRow>();
        inbox.ToTable("broker_inbox", table => table.HasCheckConstraint("ck_broker_inbox_hash", "length(fingerprint) = 64"));
        inbox.HasKey(row => row.MessageId);
        inbox.Property(row => row.Fingerprint).HasMaxLength(64);
        inbox.Property(row => row.CommandName).HasMaxLength(100);
        inbox.Property(row => row.TraceParent).HasMaxLength(55);
        inbox.Property(row => row.TraceState).HasMaxLength(512);
        inbox.Property(row => row.ReplyType).HasMaxLength(100);
        inbox.Property(row => row.ReplyJson).HasColumnType("jsonb");
        inbox.HasIndex(row => new { row.IsCompleted, row.ReceivedAt });
        inbox.HasIndex(row => new { row.ProcessedAt, row.MessageId }).HasFilter("reply_json IS NOT NULL AND published_at IS NULL AND is_completed");
        var job = model.Entity<JobRow>();
        job.ToTable("catalog_jobs", table => table.HasCheckConstraint("ck_catalog_job", "price_cents >= 0 AND version > 0 AND length(trim(name)) > 0"));
        job.HasKey(row => row.Id);
        job.Property(row => row.Name).HasMaxLength(128);
        job.Property(row => row.Description).HasMaxLength(2000);
        job.Property(row => row.UpdatedBy).HasMaxLength(200);
        job.Property(row => row.Version).IsConcurrencyToken();
        job.HasIndex(row => new { row.IsActive, row.Name, row.Id });
        var operation = model.Entity<OperationRow>();
        operation.ToTable("operations", table =>
        {
            table.HasCheckConstraint("ck_operation_version", "version > 0 AND state_format = 1");
            table.HasCheckConstraint("ck_operation_identity", "service_order_id <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("ck_operation_tombstone", "NOT is_tombstone OR (NOT is_active AND status = 'Canceled' AND vehicle_id IS NULL)");
        });
        operation.HasKey(row => row.ServiceOrderId);
        operation.Property(row => row.Status).HasConversion<string>().HasMaxLength(40);
        operation.Property(row => row.Version).IsConcurrencyToken();
        operation.Property(row => row.StateJson).HasColumnType("jsonb");
        operation.HasIndex(row => new { row.IsActive, row.Status, row.CreatedAt, row.ServiceOrderId });
        operation.HasIndex(row => new { row.CreatedAt, row.ServiceOrderId });
        var history = model.Entity<HistoryRow>();
        history.ToTable("histories", table => table.HasCheckConstraint("ck_history_version", "version > 0"));
        history.HasKey(row => new { row.ServiceOrderId, row.Version });
        history.Property(row => row.Status).HasConversion<string>().HasMaxLength(40);
        history.Property(row => row.Action).HasConversion<string>().HasMaxLength(40);
        history.HasOne<OperationRow>().WithMany().HasForeignKey(row => row.ServiceOrderId).OnDelete(DeleteBehavior.Restrict);
        var receipt = model.Entity<ReceiptRow>();
        receipt.ToTable("command_receipts", table => table.HasCheckConstraint("ck_receipt_fingerprint", "length(fingerprint) = 64"));
        receipt.HasKey(row => row.IntentId);
        receipt.Property(row => row.Fingerprint).HasMaxLength(64);
        receipt.Property(row => row.ResultJson).HasColumnType("jsonb");
        receipt.HasIndex(row => row.ServiceOrderId);
        receipt.HasOne<OperationRow>().WithMany().HasForeignKey(row => row.ServiceOrderId).OnDelete(DeleteBehavior.Restrict);
        var outbox = model.Entity<OutboxRow>();
        outbox.ToTable("outbox_events");
        outbox.HasKey(row => row.EventId);
        outbox.Property(row => row.Kind).HasConversion<string>().HasMaxLength(40);
        outbox.Property(row => row.EventJson).HasColumnType("jsonb");
        outbox.HasIndex(row => new { row.ServiceOrderId, row.Version }).IsUnique();
        outbox.HasIndex(row => new { row.OccurredAt, row.EventId }).HasFilter("dispatched_at IS NULL");
        outbox.HasOne<OperationRow>().WithMany().HasForeignKey(row => row.ServiceOrderId).OnDelete(DeleteBehavior.Restrict);
        outbox.HasOne<ReceiptRow>().WithMany().HasForeignKey(row => row.CausationId).OnDelete(DeleteBehavior.Restrict);
        foreach (var entity in model.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
    }
}
