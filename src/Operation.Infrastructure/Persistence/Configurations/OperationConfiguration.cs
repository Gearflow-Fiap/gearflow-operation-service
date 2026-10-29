using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GearFlow.Operation.Infrastructure.Persistence.Configurations;

internal sealed class OperationConfiguration : IEntityTypeConfiguration<OperationRow>
{
    public void Configure(EntityTypeBuilder<OperationRow> builder)
    {
        builder.ToTable("operations", table =>
        {
            table.HasCheckConstraint("ck_operation_version", "version > 0 AND state_format = 1");
            table.HasCheckConstraint("ck_operation_identity", "service_order_id <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("ck_operation_tombstone", "NOT is_tombstone OR (NOT is_active AND status = 'Canceled' AND vehicle_id IS NULL)");
        });
        builder.HasKey(row => row.ServiceOrderId);
        builder.Property(row => row.Status).HasConversion<string>().HasMaxLength(40);
        builder.Property(row => row.Version).IsConcurrencyToken();
        builder.Property(row => row.StateJson).HasColumnType("jsonb");
        builder.HasIndex(row => new { row.IsActive, row.Status, row.CreatedAt, row.ServiceOrderId });
        builder.HasIndex(row => new { row.CreatedAt, row.ServiceOrderId });
    }
}
