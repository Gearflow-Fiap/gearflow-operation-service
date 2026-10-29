using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using GearFlow.Operation.Application;
using GearFlow.Operation.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GearFlow.Operation.Infrastructure.Persistence;

public sealed class PostgresOperationStore(IDbContextFactory<OperationDbContext> factory) : IOperationUnitOfWork, IOperationReadStore
{
    public async Task<WorkOperation?> Load(Guid id, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Operations.AsNoTracking().SingleOrDefaultAsync(item => item.ServiceOrderId == id, ct);
        if (row is null) return null;
        // Histórico é append-only; limitar pela versão capturada evita misturar ações de um commit posterior.
        var history = await db.Histories.AsNoTracking().Where(item => item.ServiceOrderId == id && item.Version <= row.Version)
            .OrderBy(item => item.Version).ToArrayAsync(ct);
        return OperationMapper.Restore(row, history);
    }

    public Task<WorkOperation?> Find(Guid id, CancellationToken ct) => Load(id, ct);

    public async Task<CommandReceipt?> FindReceipt(Guid intentId, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var row = await db.Receipts.AsNoTracking().SingleOrDefaultAsync(item => item.IntentId == intentId, ct);
        return row is null ? null : new(row.IntentId, row.Fingerprint, StoredJson.Read<CommandResult>(row.ResultJson));
    }

    public async Task<CommandResult> Commit(OperationWrite write, CancellationToken ct)
    {
        OperationWriteValidator.Validate(write);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s';", ct);
            // Ordenação global evita inversão de locks; apenas o curto commit é serializado, não a precificação.
            var keys = new[] { LockKey("intent", write.Receipt.IntentId), LockKey("operation", write.Operation.ServiceOrderId) };
            foreach (var key in keys.Distinct().Order())
                await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key});", ct);
            var receipt = await db.Receipts.AsNoTracking().SingleOrDefaultAsync(item => item.IntentId == write.Receipt.IntentId, ct);
            if (receipt is not null)
            {
                if (receipt.Fingerprint != write.Receipt.Fingerprint) throw new IntentConflictException("Intenção já gravada com conteúdo diferente.");
                return StoredJson.Read<CommandResult>(receipt.ResultJson);
            }
            var current = await db.Operations.SingleOrDefaultAsync(item => item.ServiceOrderId == write.Operation.ServiceOrderId, ct);
            if (current?.Version != write.ExpectedStoredVersion) throw new OperationConcurrencyException("Versão persistida mudou desde a leitura.");
            if (current is not null)
            {
                var prefix = await db.Histories.AsNoTracking().Where(item => item.ServiceOrderId == current.ServiceOrderId)
                    .OrderBy(item => item.Version).ToArrayAsync(ct);
                OperationWriteValidator.PreservePrefix(OperationMapper.Restore(current, prefix), write.Operation);
            }
            var changed = current is null || current.Version != write.Operation.Version;
            if (changed)
            {
                var candidate = OperationMapper.ToRow(write.Operation);
                if (current is null) db.Operations.Add(candidate);
                else db.Entry(current).CurrentValues.SetValues(candidate);
                await db.SaveChangesAsync(ct);
                db.Histories.AddRange(write.Operation.History.Where(item => item.Version > (write.ExpectedStoredVersion ?? 0))
                    .Select(item => OperationMapper.ToRow(write.Operation.ServiceOrderId, item)));
            }
            db.Receipts.Add(new() { IntentId = write.Receipt.IntentId, ServiceOrderId = write.Operation.ServiceOrderId,
                Fingerprint = write.Receipt.Fingerprint, ResultJson = StoredJson.Write(write.Receipt.Result), CreatedAt = DateTimeOffset.UtcNow });
            db.Outbox.AddRange(write.PendingEvents.Select(item => new OutboxRow { EventId = item.EventId,
                ServiceOrderId = item.ServiceOrderId, CausationId = item.CausationId, Version = item.Version,
                Kind = item.Kind, OccurredAt = item.OccurredAt, EventJson = StoredJson.Write(item) }));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return write.Receipt.Result;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new OperationConcurrencyException("Atualização concorrente da operação.");
        }
        catch (Exception error) when (error is PostgresException { SqlState: "40001" or "40P01" or "55P03" } ||
            error.InnerException is PostgresException { SqlState: "40001" or "40P01" or "55P03" })
        {
            throw new OperationConcurrencyException("Conflito transitório no commit; recarregue e repita a intenção.");
        }
    }

    public async Task<PageResult<OperationListItem>> GetPage(OperationSearch search, PageRequest page, CancellationToken ct)
    {
        ValidatePage(page);
        if (!Enum.IsDefined(search.Scope) || search.Status.HasValue && !Enum.IsDefined(search.Status.Value)) throw new ArgumentException("Filtro inválido.");
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        var query = db.Operations.AsNoTracking().Where(item => search.IncludeInactive || item.IsActive);
        if (search.Status.HasValue) query = query.Where(item => item.Status == search.Status.Value);
        IOrderedQueryable<OperationRow> ordered;
        if (search.Scope == OperationListScope.ActionableQueue)
        {
            query = query.Where(item => item.IsActive && (item.Status == OperationStatus.InExecution || item.Status == OperationStatus.ReadyForExecution ||
                item.Status == OperationStatus.InDiagnostic || item.Status == OperationStatus.Received));
            ordered = query.OrderBy(item => item.Status == OperationStatus.InExecution ? 1 : item.Status == OperationStatus.ReadyForExecution ? 2 :
                item.Status == OperationStatus.InDiagnostic ? 3 : 4).ThenBy(item => item.CreatedAt).ThenBy(item => item.ServiceOrderId);
        }
        else ordered = query.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.ServiceOrderId);
        var count = await query.CountAsync(ct);
        var items = page.Offset >= count ? [] : await ordered.Skip((int)page.Offset).Take(page.PageSize)
            .Select(item => new OperationListItem(item.ServiceOrderId, item.ServiceOrderCode, item.VehicleId, item.Status, item.IsActive, item.Version, item.CreatedAt)).ToArrayAsync(ct);
        return new(Array.AsReadOnly(items), page.Page, page.PageSize, count);
    }

    public async Task<PageResult<OperationHistory>?> GetHistory(Guid id, PageRequest page, CancellationToken ct)
    {
        ValidatePage(page);
        await using var db = await factory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        if (!await db.Operations.AnyAsync(item => item.ServiceOrderId == id, ct)) return null;
        var query = db.Histories.AsNoTracking().Where(item => item.ServiceOrderId == id);
        var count = await query.CountAsync(ct);
        var rows = page.Offset >= count ? [] : await query.OrderBy(item => item.Version).Skip((int)page.Offset).Take(page.PageSize).ToArrayAsync(ct);
        return new(Array.AsReadOnly(rows.Select(OperationMapper.ToHistory).ToArray()), page.Page, page.PageSize, count);
    }

    public async Task<DurationSummary> GetDurationSummary(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var values = await db.Operations.AsNoTracking().Where(item => item.IsActive).GroupBy(item => 1).Select(group => new
        {
            QueueDiagnostic = group.Average(item => item.DiagnosticStartedAt.HasValue ? (double?)(item.DiagnosticStartedAt.Value - item.CreatedAt).TotalMinutes : null),
            QueueDiagnosticCount = group.Count(item => item.DiagnosticStartedAt.HasValue),
            Diagnostic = group.Average(item => item.DiagnosticCompletedAt.HasValue ? (double?)(item.DiagnosticCompletedAt.Value - item.DiagnosticStartedAt!.Value).TotalMinutes : null),
            DiagnosticCount = group.Count(item => item.DiagnosticCompletedAt.HasValue),
            QueueExecution = group.Average(item => item.ExecutionStartedAt.HasValue ? (double?)(item.ExecutionStartedAt.Value - item.ExecutionAuthorizedAt!.Value).TotalMinutes : null),
            QueueExecutionCount = group.Count(item => item.ExecutionStartedAt.HasValue),
            Execution = group.Average(item => item.ExecutionCompletedAt.HasValue ? (double?)(item.ExecutionCompletedAt.Value - item.ExecutionStartedAt!.Value).TotalMinutes : null),
            ExecutionCount = group.Count(item => item.ExecutionCompletedAt.HasValue)
        }).SingleOrDefaultAsync(ct);
        return values is null ? new(new(null, 0), new(null, 0), new(null, 0), new(null, 0)) :
            new(new(values.QueueDiagnostic, values.QueueDiagnosticCount), new(values.Diagnostic, values.DiagnosticCount),
                new(values.QueueExecution, values.QueueExecutionCount), new(values.Execution, values.ExecutionCount));
    }

    public async Task<IReadOnlyList<OperationEvent>> ReadPendingEvents(int limit = 100, CancellationToken ct = default)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var db = await factory.CreateDbContextAsync(ct);
        var json = await db.Outbox.AsNoTracking().Where(item => item.DispatchedAt == null).OrderBy(item => item.OccurredAt)
            .ThenBy(item => item.ServiceOrderId).ThenBy(item => item.Version).ThenBy(item => item.EventId).Take(limit).Select(item => item.EventJson).ToArrayAsync(ct);
        return Array.AsReadOnly(json.Select(StoredJson.Read<OperationEvent>).ToArray());
    }

    private static long LockKey(string scope, Guid id) => BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"{scope}:{id:D}")));
    private static void ValidatePage(PageRequest page)
    {
        if (page.Page < 1 || page.PageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(page));
    }
}
