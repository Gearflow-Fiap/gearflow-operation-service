using Microsoft.EntityFrameworkCore;
using GearFlow.Operation.Infrastructure.Persistence.Configurations;
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
        model.ApplyConfiguration(new BrokerInboxConfiguration());
        model.ApplyConfiguration(new JobConfiguration());
        model.ApplyConfiguration(new OperationConfiguration());
        model.ApplyConfiguration(new HistoryConfiguration());
        model.ApplyConfiguration(new ReceiptConfiguration());
        model.ApplyConfiguration(new OutboxConfiguration());
        foreach (var entity in model.Model.GetEntityTypes())
            foreach (var property in entity.GetProperties())
                property.SetColumnName(Regex.Replace(property.Name, "([a-z0-9])([A-Z])", "$1_$2").ToLowerInvariant());
    }
}
