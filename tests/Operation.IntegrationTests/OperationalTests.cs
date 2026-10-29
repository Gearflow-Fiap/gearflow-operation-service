using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using GearFlow.Operation.Api;
using GearFlow.Operation.Application;
using GearFlow.Operation.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Operation.IntegrationTests;

[Collection("Postgres")]
public class OperationalTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ReadinessRejectsPendingSchemaThenBecomesReadyWithoutLeakingDetails()
    {
        var h = await postgres.Create(migrate: false); await using var api = new ApiFactory(h); using var client = api.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        var pending = await client.GetAsync("/health/ready"); Assert.Equal(HttpStatusCode.ServiceUnavailable, pending.StatusCode);
        var body = await pending.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(); Assert.Single(body.EnumerateObject());
        Assert.Equal("not_ready", body.GetProperty("status").GetString());
        await using (var db = h.Factory.CreateDbContext()) await db.Database.MigrateAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }
    [Fact]
    public async Task LivenessRemainsAvailableWhenDatabaseIsUnavailable()
    {
        var h = await postgres.Create(); await using var baseApi = new ApiFactory(h);
        await using var api = baseApi.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDbContextFactory<OperationDbContext>>();
            services.AddSingleton<IDbContextFactory<OperationDbContext>>(new OperationContextFactory("Host=127.0.0.1;Port=1;Database=unavailable;Username=none;Password=none;Timeout=1;Pooling=false"));
        }));
        using var client = api.CreateClient(); Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
    }
    [Fact]
    public async Task HttpErrorsKeepIncomingW3CTraceAndLogsContainOnlyBoundedRouteAndSafeScope()
    {
        var h = await postgres.Create(); await h.Receive(); var logs = new CapturedLogs();
        await using var baseApi = new ApiFactory(h);
        await using var api = baseApi.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(logs)));
        using var client = api.CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.Token());
        const string trace = "11111111111111111111111111111111";
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/operation/service-orders/{h.OrderId}/diagnostic/start?debug=DO_NOT_LOG_QUERY");
        request.Headers.Add("traceparent", "00-" + trace + "-2222222222222222-01"); request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); request.Headers.TryAddWithoutValidation("If-Match", "\"99\"");
        var response = await client.SendAsync(request); Assert.Equal((HttpStatusCode)412, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(); Assert.Equal(trace, body.GetProperty("traceId").GetString());
        Assert.Contains(logs.Rows, row => row.Category.EndsWith(nameof(OperationRequestLogging), StringComparison.Ordinal) && row.Scope.TryGetValue("TraceId", out var value) && Equals(value, trace));
        Assert.Contains(logs.Rows, row => row.Scope.TryGetValue("CorrelationId", out var value) && Equals(value, h.OrderId.ToString("D")));
        Assert.DoesNotContain(logs.Rows, row => row.Text.Contains("DO_NOT_LOG_QUERY", StringComparison.Ordinal));
        using var valid = new HttpRequestMessage(HttpMethod.Post, $"/api/operation/service-orders/{h.OrderId}/diagnostic/start");
        valid.Headers.Add("traceparent", "00-" + trace + "-3333333333333333-01");
        valid.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); valid.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        Assert.True((await client.SendAsync(valid)).IsSuccessStatusCode);
        var events = await h.Store.ReadPendingEvents();
        Assert.Contains(events, item => item.TraceParent is not null && item.TraceParent.Contains(trace, StringComparison.Ordinal));
    }
    [Fact]
    public async Task BacklogQueriesReportRealPendingAndIncompleteStateAndSuppressStaleGaugeAfterFailure()
    {
        var h = await postgres.Create(); await h.Receive(); h.Clock.Advance();
        var diagnostics = new MessagingDiagnostics(h.Factory, h.Clock); var data = await diagnostics.Read(default);
        Assert.Equal(1, data.Events); Assert.Equal(0, data.Replies); Assert.Equal(0, data.IncompleteInbox); Assert.Equal(60, data.OldestOutboxSeconds);
        using var metrics = new BacklogMetrics(); var samples = new List<(string Name, long Value)>();
        using var listener = new MeterListener { InstrumentPublished = (instrument, value) => { if (instrument.Meter.Name == OperationTelemetry.Name) value.EnableMeasurementEvents(instrument); } };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => samples.Add((instrument.Name, value))); listener.Start();
        metrics.Update(data); listener.RecordObservableInstruments(); Assert.Contains(samples, sample => sample.Name == "gearflow.operation.outbox.pending" && sample.Value == 1);
        samples.Clear(); metrics.Update(null); listener.RecordObservableInstruments(); Assert.DoesNotContain(samples, sample => sample.Name == "gearflow.operation.outbox.pending");
    }
    [Fact]
    public async Task OldOutboxPayloadWithoutTraceFieldsRemainsReadable()
    {
        var h = await postgres.Create(); await h.Receive();
        await using var db = h.Factory.CreateDbContext(); var row = await db.Outbox.SingleAsync();
        var json = System.Text.Json.Nodes.JsonNode.Parse(row.EventJson)!; json.AsObject().Remove("traceParent"); json.AsObject().Remove("traceState");
        row.EventJson = json.ToJsonString(); await db.SaveChangesAsync();
        Assert.Null(Assert.Single(await h.Store.ReadPendingEvents()).TraceParent);
    }
}

internal sealed record CapturedLog(string Category, string Text, Dictionary<string, object?> Scope);
internal sealed class CapturedLogs : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();
    public ConcurrentQueue<CapturedLog> Rows { get; } = new();
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => scopes = scopeProvider;
    public ILogger CreateLogger(string categoryName) => new CapturedLogger(this, categoryName);
    public void Dispose() { }
    private sealed class CapturedLogger(CapturedLogs owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => owner.scopes.Push(state);
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var fields = new Dictionary<string, object?>();
            owner.scopes.ForEachScope((scope, target) => { if (scope is IEnumerable<KeyValuePair<string, object?>> values) foreach (var value in values) target.TryAdd(value.Key, value.Value); }, fields);
            owner.Rows.Enqueue(new(category, formatter(state, exception), fields));
        }
    }
}

