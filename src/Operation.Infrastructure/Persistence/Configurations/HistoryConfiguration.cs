using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GearFlow.Operation.Infrastructure.Persistence.Configurations;

internal sealed class HistoryConfiguration : IEntityTypeConfiguration<HistoryRow>
{
    public void Configure(EntityTypeBuilder<HistoryRow> builder)
    {
        builder.ToTable("histories", table => table.HasCheckConstraint("ck_history_version", "version > 0"));
        builder.HasKey(row => new { row.ServiceOrderId, row.Version });
        builder.Property(row => row.Status).HasConversion<string>().HasMaxLength(40);
        builder.Property(row => row.Action).HasConversion<string>().HasMaxLength(40);
        builder.HasOne<OperationRow>().WithMany().HasForeignKey(row => row.ServiceOrderId).OnDelete(DeleteBehavior.Restrict);
    }
}
