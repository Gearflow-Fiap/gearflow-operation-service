using GearFlow.Operation.Application;
using GearFlow.Operation.Domain;
using GearFlow.Operation.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Operation.IntegrationTests.Support;

[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }

public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? container;
    private string admin = "";
    private readonly List<string> databases = [];
    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("OPERATION_TEST_POSTGRES");
        if (!string.IsNullOrWhiteSpace(external)) admin = external;
        else
        {
            container = new PostgreSqlBuilder("postgres:17").WithDatabase("operation_test_admin")
                .WithUsername("operation_test").WithPassword("operation_test_only").Build();
            await container.StartAsync();
            admin = container.GetConnectionString();
        }
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT version();", connection);
        var version = (string)(await command.ExecuteScalarAsync())!;
        Assert.Contains("PostgreSQL 17.", version);
    }

    internal async Task<IntegrationHarness> Create(bool migrate = true)
    {
        // Collection serializada: liberar pools ociosos dos bancos anteriores antes do próximo cenário.
        NpgsqlConnection.ClearAllPools();
        var name = "operation_it_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\";", connection);
        await command.ExecuteNonQueryAsync();
        databases.Add(name);
        var builder = new NpgsqlConnectionStringBuilder(admin) { Database = name, MaxPoolSize = 8 };
        var harness = new IntegrationHarness(builder.ConnectionString);
        if (migrate)
        {
            await using var db = harness.Factory.CreateDbContext();
            await db.Database.MigrateAsync();
        }
        return harness;
    }

    public async Task DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        if (container is not null) { await container.DisposeAsync(); return; }
        await using var connection = new NpgsqlConnection(admin);
        await connection.OpenAsync();
        foreach (var name in databases)
        {
            await using var command = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE);", connection);
            command.CommandTimeout = 120; // DROP can wait for a filesystem checkpoint on the Windows test runtime.
            await command.ExecuteNonQueryAsync();
        }
    }
}

internal sealed class IntegrationClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 28, 20, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
    public void Advance() => Now = Now.AddMinutes(1);
}

internal sealed class IntegrationCatalog(IntegrationClock clock) : IPriceCatalog
{
    public Task<CatalogPriceQuote> GetPrices(IReadOnlyList<PriceRequest> items, CancellationToken ct) => Task.FromResult(new CatalogPriceQuote(
        clock.Now, "BRL", items.Select(item => new CatalogPrice(item.ItemType, item.ItemId, item.ItemType.ToString(),
            item.ItemType == OperationItemType.Labor ? 1000 : item.ItemType == OperationItemType.Part ? 300 : 200)).ToArray()));
}

internal sealed class InterceptingFactory(string connectionString, IInterceptor interceptor) : IDbContextFactory<OperationDbContext>
{
    public OperationDbContext CreateDbContext() => new(new DbContextOptionsBuilder<OperationDbContext>()
        .UseNpgsql(connectionString).AddInterceptors(interceptor).Options);
}

internal sealed class FailSecondSaveInterceptor : SaveChangesInterceptor
{
    private int saves;
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Increment(ref saves) == 2) throw new IOException("Injected failure after root save, before receipt/outbox commit.");
        return ValueTask.FromResult(result);
    }
}

internal sealed class IntegrationHarness(string connectionString)
{
    public string ConnectionString { get; } = connectionString;
    public OperationContextFactory Factory { get; } = new(connectionString);
    public PostgresOperationStore Store => new(Factory);
    public OperationQueries Queries => new(Store);
    public IntegrationClock Clock { get; } = new();
    public Guid OrderId { get; } = Guid.NewGuid();
    public Guid VehicleId { get; } = Guid.NewGuid();
    public RequestedJob Job { get; } = new(Guid.NewGuid(), Guid.NewGuid());
    public RequestedPart Part { get; } = new(Guid.NewGuid(), Guid.NewGuid(), 2);
    public DiagnosticConsumable Consumable { get; } = new(Guid.NewGuid(), Guid.NewGuid(), 1.5m);
    public Guid BudgetJobId { get; } = Guid.NewGuid();
    public CommandContext Context(Guid? intent = null, Guid? order = null, long? version = null) => new(intent ?? Guid.NewGuid(), order ?? OrderId, "staff:test", version);
    public OperationService Service(IDbContextFactory<OperationDbContext>? factory = null) => new(new PostgresOperationStore(factory ?? Factory),
        new DiagnosticPricer(new IntegrationCatalog(Clock), ConsumablePricingMode.PerUnitV1, Clock), Clock);
    public Task<CommandResult> Receive(CommandContext? context = null) => Service().Receive(context ?? Context(), VehicleId, [Job], [Part], 45);
    public async Task Diagnosed()
    {
        await Receive(); Clock.Advance(); await Service().StartDiagnostic(Context());
        Clock.Advance(); await Service().CompleteDiagnostic(Context(), [Consumable]);
    }
    public async Task Executing()
    {
        await Diagnosed(); Clock.Advance();
        var context = Context(); var operation = (await Store.Load(OrderId, default))!;
        await Service().AuthorizeExecution(context, new(context.IntentId, Guid.NewGuid(), 1, operation.Diagnostic!, [new(BudgetJobId, Job.LineId)]));
        Clock.Advance(); await Service().StartExecution(Context());
    }
    public async Task<(int Operations, int History, int Receipts, int Outbox)> Counts()
    {
        await using var db = Factory.CreateDbContext();
        return (await db.Operations.CountAsync(), await db.Histories.CountAsync(), await db.Receipts.CountAsync(), await db.Outbox.CountAsync());
    }
}
