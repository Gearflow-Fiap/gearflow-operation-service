using GearFlow.Operation.Domain;

namespace GearFlow.Operation.Application;

public enum OperationEventKind
{
    DiagnosticQueued, VehicleUpdated, DiagnosticStarted, DiagnosticFinished, DiagnosticFailed,
    ExecutionAuthorized, ExecutionStarted, JobExecuted, ExecutionFinished, OperationDeactivated, OperationCanceled
}

/// <summary>Registro interno completo para o futuro adapter outbox. Não é o contrato RabbitMQ publicado.</summary>
public sealed record OperationEvent(Guid EventId, Guid ServiceOrderId, Guid CausationId,
    OperationEventKind Kind, long Version, DateTimeOffset OccurredAt, string Actor, OperationDetails Data);

public sealed record OperationWrite(WorkOperation Operation, long? ExpectedStoredVersion,
    CommandReceipt Receipt, IReadOnlyList<OperationEvent> PendingEvents);

/// <summary>
/// Load deve retornar cópia destacada. Commit deve verificar versão/ausência e unicidade de IntentId,
/// e salvar estado, receipt e outbox na MESMA transação, inclusive quando não há mudança de estado.
/// Se outra execução já comitou a mesma intenção/hash, retornar o resultado original sem novos efeitos;
/// hash divergente gera IntentConflictException. Checar receipt antes da versão nesse replay concorrente.
/// Falha não pode tornar estado, receipt ou eventos visíveis parcialmente. Nenhum adapter real existe ainda.
/// </summary>
public interface IOperationUnitOfWork
{
    Task<WorkOperation?> Load(Guid serviceOrderId, CancellationToken ct);
    Task<CommandReceipt?> FindReceipt(Guid intentId, CancellationToken ct);
    Task<CommandResult> Commit(OperationWrite write, CancellationToken ct);
}

public enum OperationListScope { All, ActionableQueue }
public sealed record PageRequest(int Page, int PageSize)
{
    public static PageRequest Normalize(int page, int pageSize) => new(Math.Max(1, page), Math.Clamp(pageSize, 1, 100));
    public long Offset => ((long)Page - 1) * PageSize;
}
public sealed record PageResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
public sealed record OperationSearch(OperationListScope Scope, OperationStatus? Status = null, bool IncludeInactive = false);

/// <summary>
/// Adapter futuro pagina/filtra no banco. Queue usa ExecutionQueue e exclui inativas;
/// All ordena CreatedAt desc e ServiceOrderId asc. GetHistory retorna null para OS ausente e ordena por Version.
/// Summary considera operações ativas. Implementação em memória existe somente nos testes.
/// </summary>
public interface IOperationReadStore
{
    Task<WorkOperation?> Find(Guid serviceOrderId, CancellationToken ct);
    Task<PageResult<OperationListItem>> GetPage(OperationSearch search, PageRequest page, CancellationToken ct);
    Task<PageResult<OperationHistory>?> GetHistory(Guid serviceOrderId, PageRequest page, CancellationToken ct);
    Task<DurationSummary> GetDurationSummary(CancellationToken ct);
}
