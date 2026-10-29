using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GearFlow.Operation.Infrastructure.Persistence.Configurations;

internal sealed class JobConfiguration : IEntityTypeConfiguration<JobRow>
{
    public void Configure(EntityTypeBuilder<JobRow> builder)
    {
        builder.ToTable("catalog_jobs", table => table.HasCheckConstraint("ck_catalog_job", "price_cents >= 0 AND version > 0 AND length(trim(name)) > 0"));
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Name).HasMaxLength(128);
        builder.Property(row => row.Description).HasMaxLength(2000);
        builder.Property(row => row.UpdatedBy).HasMaxLength(200);
        builder.Property(row => row.Version).IsConcurrencyToken();
        builder.HasIndex(row => new { row.IsActive, row.Name, row.Id });
    }
}
