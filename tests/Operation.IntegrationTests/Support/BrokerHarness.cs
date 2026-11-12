using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using GearFlow.Operation.Application;
using GearFlow.Operation.Infrastructure.Messaging;
using GearFlow.Operation.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using W = GearFlow.Contracts.Operation.V1;

namespace Operation.IntegrationTests.Support;

internal sealed record ReceivedWire(object Body, Guid? Id, Guid? Correlation, Guid? Cause, string? TraceId = null, string? Exchange = null);
internal sealed class BrokerHarness : IAsyncDisposable
{
    private readonly HttpClient management;
    private readonly ServiceProvider services;
    private readonly IBusControl probe;
    private readonly IBusControl bus;
    private readonly IntegrationHarness data;
    public string Vhost { get; }
    public OperationRabbitOptions Options { get; private init; } = null!;
    public ConcurrentQueue<ReceivedWire> Events { get; } = new();
    private BrokerHarness(HttpClient management, ServiceProvider services, IBusControl probe, IBusControl bus, IntegrationHarness data, string vhost)
    { this.management = management; this.services = services; this.probe = probe; this.bus = bus; this.data = data; Vhost = vhost; }
    public static async Task<BrokerHarness> Start(IntegrationHarness data, bool observeEvents = true)
    {
        var uri = new Uri(Environment.GetEnvironmentVariable("OPERATION_TEST_RABBITMQ") ?? "amqp://operation_test:operation_test_only@127.0.0.1:5674/");
        var credentials = uri.UserInfo.Split(':', 2).Select(Uri.UnescapeDataString).ToArray();
        var management = new HttpClient { BaseAddress = new Uri(Environment.GetEnvironmentVariable("OPERATION_TEST_RABBITMQ_MANAGEMENT") ?? "http://127.0.0.1:15674/") };
        management.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(string.Join(':', credentials))));
        await Until(async () =>
        {
            try { using var health = await management.GetAsync("api/overview"); health.EnsureSuccessStatusCode(); return true; }
            catch (HttpRequestException error) when (error.StatusCode is null or System.Net.HttpStatusCode.ServiceUnavailable) { return false; }
        });
        var vhost = "operation_it_" + Guid.NewGuid().ToString("N");
        (await management.PutAsJsonAsync("api/vhosts/" + vhost, new { })).EnsureSuccessStatusCode();
        (await management.PutAsJsonAsync($"api/permissions/{vhost}/{Uri.EscapeDataString(credentials[0])}", new { configure = ".*", write = ".*", read = ".*" })).EnsureSuccessStatusCode();
        var options = new OperationRabbitOptions(uri.Host, (ushort)uri.Port, vhost, credentials[0], credentials[1]);
        var services = new ServiceCollection();
        services.AddSingleton<IDbContextFactory<OperationDbContext>>(data.Factory); services.AddSingleton<TimeProvider>(data.Clock);
        services.AddScoped<IOperationUnitOfWork>(_ => data.Store);
        services.AddScoped(_ => new DiagnosticPricer(new IntegrationCatalog(data.Clock), ConsumablePricingMode.PerUnitV1, data.Clock));
        services.AddScoped<OperationService>(); services.AddOperationMessaging(options);
        var provider = services.BuildServiceProvider();
        BrokerHarness? harness = null;
        var probe = Bus.Factory.CreateUsingRabbitMq(cfg =>
        {
            cfg.ConfigureOperationEventNames();
            cfg.Host(options.Host, options.Port, options.VirtualHost, host => { host.Username(options.Username); host.Password(options.Password); });
            if (observeEvents) cfg.ReceiveEndpoint("operation-test-observer", endpoint =>
            {
                Attach<W.DiagnosticQueued>(endpoint); Attach<W.DiagnosticStarted>(endpoint); Attach<W.DiagnosticFinished>(endpoint); Attach<W.DiagnosticFailed>(endpoint);
                Attach<W.ExecutionAuthorized>(endpoint); Attach<W.ExecutionStarted>(endpoint); Attach<W.JobExecuted>(endpoint); Attach<W.ExecutionFinished>(endpoint);
                Attach<W.VehicleUpdated>(endpoint); Attach<W.OperationDeactivated>(endpoint); Attach<W.OperationCanceled>(endpoint);
                Attach<W.OperationCommandRejected>(endpoint); Attach<W.OperationCancellationRejected>(endpoint);
                void Attach<T>(IRabbitMqReceiveEndpointConfigurator receive) where T : class => receive.Handler<T>(context =>
                {
                    harness!.Events.Enqueue(new(context.Message, context.MessageId, context.CorrelationId, context.InitiatorId, System.Diagnostics.Activity.Current?.TraceId.ToString(),
                        context.ReceiveContext.GetPayload<RabbitMqBasicConsumeContext>().Exchange)); return Task.CompletedTask;
                });
            });
        });
        var bus = provider.GetRequiredService<IBusControl>(); harness = new(management, provider, probe, bus, data, vhost) { Options = options };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await probe.StartAsync(timeout.Token); await bus.StartAsync(timeout.Token); return harness; }
        catch { await harness.DisposeAsync(); throw; }
    }
    public async Task Send<T>(string queue, T message, Guid id, Guid order) where T : class
    {
        var endpoint = await probe.GetSendEndpoint(new Uri("queue:" + queue));
        await endpoint.Send(message, context => { context.MessageId = id; context.CorrelationId = order; });
    }
    public async Task PublishEvent(object message, Guid id, Guid order, Guid cause)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IOperationPublisher>().Publish(message, id, order, cause, default);
    }
    public Task StopConsumers() => bus.StopAsync();
    public Task StartConsumers() => bus.StartAsync();
    public async Task Flush() { await using var scope = services.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<OperationOutboxDispatcher>().Dispatch(); }
    public async Task WaitInbox(Guid id)
    {
        await Until(async () => { await using var db = data.Factory.CreateDbContext(); return await db.Set<BrokerInboxRow>().AnyAsync(row => row.MessageId == id && row.IsCompleted); });
    }
    public async Task<T> Event<T>(Guid cause) where T : class
    {
        await Until(() => Task.FromResult(Events.Any(value => value.Body is T && value.Cause == cause)));
        var item = Events.First(value => value.Body is T && value.Cause == cause);
        var bodyId = (Guid)typeof(T).GetProperty("MessageId")!.GetValue(item.Body)!;
        var correlation = (Guid)typeof(T).GetProperty("CorrelationId")!.GetValue(item.Body)!;
        Xunit.Assert.Equal(bodyId, item.Id); Xunit.Assert.Equal(correlation, item.Correlation); return (T)item.Body;
    }
    public async Task<int> ErrorCount(string queue)
    {
        var response = await management.GetAsync($"api/queues/{Vhost}/{queue}_error");
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return 0;
        response.EnsureSuccessStatusCode(); var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return json.TryGetProperty("messages", out var count) ? count.GetInt32() : 0;
    }
    public static async Task Until(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!await condition()) await Task.Delay(50, timeout.Token);
    }
    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await bus.StopAsync(timeout.Token); await probe.StopAsync(timeout.Token); }
        finally
        {
            await services.DisposeAsync();
            if (!Vhost.StartsWith("operation_it_", StringComparison.Ordinal)) throw new InvalidOperationException("Vhost não pertence ao teste.");
            (await management.DeleteAsync("api/vhosts/" + Vhost)).EnsureSuccessStatusCode(); management.Dispose();
        }
    }
}
