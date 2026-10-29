using System.Text.Json;
using Npgsql;
using GearFlow.Operation.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

if (args.Length != 1 || args[0] is not ("--check" or "--apply"))
{
    Console.Error.WriteLine("Use --check ou --apply com OPERATION_CONNECTION_STRING configurada."); return 64;
}
var connection = Environment.GetEnvironmentVariable("OPERATION_CONNECTION_STRING") ?? Environment.GetEnvironmentVariable("ConnectionStrings__Operation");
if (string.IsNullOrWhiteSpace(connection)) { Console.Error.WriteLine("Conexão Operation obrigatória."); return 64; }
try
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    await using var db = new OperationContextFactory(connection).CreateDbContext();
    if (args[0] == "--check")
    {
        var count = (await db.Database.GetPendingMigrationsAsync(timeout.Token)).Count();
        Console.WriteLine(JsonSerializer.Serialize(new { status = count == 0 ? "current" : "pending", count })); return count == 0 ? 0 : 2;
    }
    await db.Database.MigrateAsync(timeout.Token);
    Console.WriteLine("{\"status\":\"migrations_applied\"}"); return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(JsonSerializer.Serialize(new { status = "failed", errorType = error.GetType().Name, sqlState = (error as PostgresException)?.SqlState })); return 1;
}
