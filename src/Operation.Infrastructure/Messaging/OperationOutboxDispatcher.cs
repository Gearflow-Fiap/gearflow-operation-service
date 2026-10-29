using GearFlow.Operation.Infrastructure.Persistence;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace GearFlow.Operation.Infrastructure.Messaging;

public interface IOperationPublisher
{
    Task Publish(object message, Guid messageId, Guid correlationId, Guid causationId, CancellationToken ct);
}
public sealed class MassTransitOperationPublisher(IPublishEndpoint endpoint) : IOperationPublisher
{
    public Task Publish(object message, Guid messageId, Guid correlationId, Guid causationId, CancellationToken ct) =>
        endpoint.Publish(message, message.GetType(), context =>
        {
            context.MessageId = messageId; context.CorrelationId = correlationId; context.InitiatorId = causationId;
            if (!context.TryGetPayload<RabbitMqSendContext>(out var rabbit)) throw new InvalidOperationException("Publicador requer contexto RabbitMQ.");
            rabbit.Mandatory = true;
        }, ct);
}

public sealed class OperationOutboxDispatcher(IDbContextFactory<OperationDbContext> factory, IOperationPublisher publisher, TimeProvider clock)
{
    public async Task<int> Dispatch(int limit = 100, CancellationToken ct = default)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        var delivered = 0;
        for (var i = 0; i < limit; i++)
        {
            var found = i % 2 == 0 ? await Internal(ct) || await Reply(ct) : await Reply(ct) || await Internal(ct);
            if (!found) break;
            delivered++;
        }
        return delivered;
    }
    private async Task<bool> Internal(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var rows = await db.Outbox.FromSqlRaw("SELECT * FROM operation.outbox_events WHERE dispatched_at IS NULL ORDER BY occurred_at, service_order_id, version LIMIT 1 FOR UPDATE SKIP LOCKED").ToArrayAsync(ct);
        if (rows.Length == 0) return false;
        var row = rows[0]; var value = StoredJson.Read<GearFlow.Operation.Application.OperationEvent>(row.EventJson);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var activity = GearFlow.Operation.Application.OperationTelemetry.Delivery(value.TraceParent, value.TraceState);
        await publisher.Publish(OperationWire.Project(value), row.EventId, row.ServiceOrderId, row.CausationId, deadline.Token);
        row.DispatchedAt = clock.GetUtcNow(); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        activity.Complete();
        GearFlow.Operation.Application.OperationTelemetry.Delivered("event"); return true;
    }
    private async Task<bool> Reply(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var rows = await db.Set<BrokerInboxRow>().FromSqlRaw("SELECT * FROM operation.broker_inbox WHERE is_completed AND reply_json IS NOT NULL AND published_at IS NULL ORDER BY processed_at, message_id LIMIT 1 FOR UPDATE SKIP LOCKED").ToArrayAsync(ct);
        if (rows.Length == 0) return false;
        var row = rows[0]; using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var activity = GearFlow.Operation.Application.OperationTelemetry.Delivery(row.TraceParent, row.TraceState);
        await publisher.Publish(OperationWire.Deserialize(row.ReplyType!, row.ReplyJson!), row.ReplyId!.Value, row.CorrelationId, row.MessageId, deadline.Token);
        row.PublishedAt = clock.GetUtcNow(); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
        activity.Complete();
        GearFlow.Operation.Application.OperationTelemetry.Delivered("reply"); return true;
    }
}
