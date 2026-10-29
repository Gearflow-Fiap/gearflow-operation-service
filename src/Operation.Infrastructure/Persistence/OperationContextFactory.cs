using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GearFlow.Operation.Infrastructure.Persistence;

/// <summary>Factory standalone para testes/host futuro; não aplica migrations implicitamente no request.</summary>
public sealed class OperationContextFactory(string connectionString) : IDbContextFactory<OperationDbContext>
{
    public OperationDbContext CreateDbContext() => new(new DbContextOptionsBuilder<OperationDbContext>().UseNpgsql(connectionString, options => options.MigrationsHistoryTable("__EFMigrationsHistory", "public")).Options);
}

public sealed class DesignTimeOperationContextFactory : IDesignTimeDbContextFactory<OperationDbContext>
{
    public OperationDbContext CreateDbContext(string[] args) => new OperationContextFactory(
        Environment.GetEnvironmentVariable("OPERATION_CONNECTION_STRING")
        ?? "Host=localhost;Port=5434;Database=gearflow_operation;Username=gearflow").CreateDbContext();
}
