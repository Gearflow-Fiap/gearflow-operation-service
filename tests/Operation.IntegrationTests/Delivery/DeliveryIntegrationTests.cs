using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using GearFlow.Operation.Application;
using GearFlow.Operation.Infrastructure.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using W = GearFlow.Contracts.Operation.V1;

namespace Operation.IntegrationTests.Delivery;

[Collection("Postgres")]
public class DeliveryIntegrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task MigratorCheckAndApplyOperateOnlyOnExplicitDisposableDatabase()
    {
        var h = await postgres.Create(migrate: false);
        // The local login equals the application schema: once created, search_path changes current_schema().
        var connection = new Npgsql.NpgsqlConnectionStringBuilder(h.ConnectionString) { SearchPath = "operation,public" }.ConnectionString;
        Assert.Equal(64, await Run(null, "--check"));
        Assert.Equal(2, await Run(connection, "--check"));
        Assert.Equal(0, await Run(connection, "--apply"));
        Assert.Equal(0, await Run(connection, "--check"));
        Assert.Equal(0, await Run(connection, "--apply"));
        static async Task<int> Run(string? connection, string argument)
        {
            var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
            var path = Path.GetFullPath($"../../../../../tools/Operation.Migrate/bin/{configuration}/net10.0/Operation.Migrate.dll", AppContext.BaseDirectory);
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(path); start.ArgumentList.Add(argument);
            start.Environment.Remove("OPERATION_CONNECTION_STRING"); start.Environment.Remove("ConnectionStrings__Operation");
            if (connection is not null) start.Environment["OPERATION_CONNECTION_STRING"] = connection;
            using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { process.Kill(entireProcessTree: true); throw; }
            Assert.DoesNotContain("Password=", await output); Assert.DoesNotContain("Password=", await error); return process.ExitCode;
        }
    }

    [Fact]
    public async Task TraceSurvivesDatabaseOutboxAndActualRabbitMqDelivery()
    {
        using var listener = new ActivityListener { ShouldListenTo = source => source.Name is OperationTelemetry.Name or "MassTransit",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded };
        ActivitySource.AddActivityListener(listener);
        var h = await postgres.Create(); await using var broker = await BrokerHarness.Start(h);
        var command = new W.StartDiagnostic(h.OrderId, Guid.NewGuid(), h.Clock.Now, h.VehicleId, 45,
            [new(h.Job.LineId, h.Job.CatalogJobId)], [new(h.Part.LineId, h.Part.PartId, h.Part.Quantity)]);
        string trace;
        using (var origin = new Activity("origin").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            trace = origin.TraceId.ToString(); await broker.Send("operation-start-diagnostic", command, command.MessageId, h.OrderId);
        }
        await broker.WaitInbox(command.MessageId);
        var pending = Assert.Single(await h.Store.ReadPendingEvents()); Assert.NotNull(pending.TraceParent);
        Assert.Contains(trace, pending.TraceParent);
        await broker.Flush(); await broker.Event<W.DiagnosticQueued>(command.MessageId);
        Assert.Equal(trace, broker.Events.Single(value => value.Body is W.DiagnosticQueued).TraceId);
        Assert.DoesNotContain("traceParent", OperationWire.Serialize(OperationWire.Project(pending)));
    }

    [Fact]
    public async Task OtlpHttpExporterSendsTracesMetricsAndLogsToLocalReceiver()
    {
        await using var collector = await CollectorStub.Start(); var h = await postgres.Create(); await h.Receive();
        await using var baseApi = new ApiFactory(h);
        await using var api = baseApi.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Telemetry:ExportEnabled", "true").UseSetting("Otlp:Endpoint", collector.Address);
            builder.UseSetting("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf").UseSetting("OTEL_EXPORTER_OTLP_HEADERS", "api-key=collector-test-only");
        });
        using var client = api.CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.Token());
        var response = await client.GetAsync($"/api/operation/service-orders/{h.OrderId}"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await BrokerHarness.Until(() => Task.FromResult(new[] { "traces", "metrics", "logs" }.All(signal => collector.Signals.Contains(signal))));
    }
}

internal sealed class CollectorStub(WebApplication app, string address) : IAsyncDisposable
{
    public string Address { get; } = address;
    public ConcurrentBag<string> Signals { get; } = [];
    public static async Task<CollectorStub> Start()
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        var app = builder.Build(); CollectorStub? collector = null;
        app.MapPost("/v1/{signal}", async (string signal, HttpContext http) =>
        {
            Assert.Equal("collector-test-only", http.Request.Headers["api-key"].ToString());
            Assert.StartsWith("application/x-protobuf", http.Request.ContentType);
            using var body = new MemoryStream(); await http.Request.Body.CopyToAsync(body); Assert.True(body.Length > 0);
            collector!.Signals.Add(signal); http.Response.ContentType = "application/x-protobuf"; http.Response.StatusCode = 200;
        });
        await app.StartAsync(); collector = new(app, app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()); return collector;
    }
    public async ValueTask DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); }
}
