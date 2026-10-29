using GearFlow.Operation.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;

namespace GearFlow.Operation.Infrastructure.Persistence;

public sealed record MessagingBacklog(long Events, long Replies, long IncompleteInbox, double OldestOutboxSeconds);
public sealed class MessagingDiagnostics(IDbContextFactory<OperationDbContext> factory, TimeProvider clock)
{
    public async Task<MessagingBacklog> Read(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
        var events = db.Outbox.AsNoTracking().Where(row => row.DispatchedAt == null);
        var replies = db.Set<BrokerInboxRow>().AsNoTracking().Where(row => row.IsCompleted && row.ReplyJson != null && row.PublishedAt == null);
        var count = await events.LongCountAsync(ct); var replyCount = await replies.LongCountAsync(ct);
        var incomplete = await db.Set<BrokerInboxRow>().LongCountAsync(row => !row.IsCompleted, ct);
        var eventTime = await events.Select(row => (DateTimeOffset?)row.OccurredAt).MinAsync(ct);
        var replyTime = await replies.Select(row => row.ProcessedAt).MinAsync(ct);
        var oldest = eventTime.HasValue && replyTime.HasValue ? (eventTime < replyTime ? eventTime : replyTime) : eventTime ?? replyTime;
        return new(count, replyCount, incomplete, oldest.HasValue ? Math.Max(0, (clock.GetUtcNow() - oldest.Value).TotalSeconds) : 0);
    }
}
