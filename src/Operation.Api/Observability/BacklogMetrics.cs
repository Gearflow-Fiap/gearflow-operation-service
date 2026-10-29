using System.Diagnostics.Metrics;
using GearFlow.Operation.Application;
using GearFlow.Operation.Infrastructure.Persistence;

namespace GearFlow.Operation.Api;

public sealed class BacklogMetrics : IDisposable
{
    private readonly Meter meter = new(OperationTelemetry.Name);
    private MessagingBacklog? snapshot;
    public BacklogMetrics()
    {
        meter.CreateObservableGauge<long>("gearflow.operation.outbox.pending", Pending, "{message}");
        meter.CreateObservableGauge<long>("gearflow.operation.inbox.incomplete", Incomplete, "{message}");
        meter.CreateObservableGauge<double>("gearflow.operation.outbox.oldest", Oldest, "s");
        meter.CreateObservableGauge<int>("gearflow.operation.backlog.sample_success", () => Volatile.Read(ref snapshot) is null ? 0 : 1);
    }
    public void Update(MessagingBacklog? value) => Volatile.Write(ref snapshot, value);
    private IEnumerable<Measurement<long>> Pending() { var value = Volatile.Read(ref snapshot); if (value is not null) yield return new(value.Events + value.Replies); }
    private IEnumerable<Measurement<long>> Incomplete() { var value = Volatile.Read(ref snapshot); if (value is not null) yield return new(value.IncompleteInbox); }
    private IEnumerable<Measurement<double>> Oldest() { var value = Volatile.Read(ref snapshot); if (value is not null) yield return new(value.OldestOutboxSeconds); }
    public void Dispose() => meter.Dispose();
}
public sealed class BacklogWorker(IServiceScopeFactory scopes, BacklogMetrics metrics, ILogger<BacklogWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
                await using var scope = scopes.CreateAsyncScope();
                metrics.Update(await scope.ServiceProvider.GetRequiredService<MessagingDiagnostics>().Read(timeout.Token));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { metrics.Update(null); logger.LogWarning("Não foi possível amostrar backlog: {ErrorType}", error.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
