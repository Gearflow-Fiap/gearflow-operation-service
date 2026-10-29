using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GearFlow.Operation.Infrastructure.Persistence.Configurations;

internal sealed class ReceiptConfiguration : IEntityTypeConfiguration<ReceiptRow>
{
    public void Configure(EntityTypeBuilder<ReceiptRow> builder)
    {
        builder.ToTable("command_receipts", table => table.HasCheckConstraint("ck_receipt_fingerprint", "length(fingerprint) = 64"));
        builder.HasKey(row => row.IntentId);
        builder.Property(row => row.Fingerprint).HasMaxLength(64);
        builder.Property(row => row.ResultJson).HasColumnType("jsonb");
        builder.HasIndex(row => row.ServiceOrderId);
        builder.HasOne<OperationRow>().WithMany().HasForeignKey(row => row.ServiceOrderId).OnDelete(DeleteBehavior.Restrict);
    }
}
