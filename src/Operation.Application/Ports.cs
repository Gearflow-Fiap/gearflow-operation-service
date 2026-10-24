using GearFlow.Operation.Domain;
namespace GearFlow.Operation.Application;

public sealed record OperationEvent(Guid EventId, Guid ServiceOrderId, Guid CorrelationId,
    string Type, OperationStatus Status, DateTimeOffset OccurredAt);

/// <summary>
/// O adaptador deverá gravar estado e outbox na MESMA transação, com concorrência otimista.
/// Load deve retornar unidade de trabalho isolada; falhas de Commit não podem persistir mutações.
/// Não implementar publicação direta após salvar o agregado.
/// </summary>
public interface IOperationUnitOfWork
{
    Task<WorkOperation?> Load(Guid serviceOrderId, CancellationToken ct);
    Task Commit(WorkOperation operation, OperationEvent pendingEvent, CancellationToken ct);
}

public static class ExecutionQueue
{
    public static IReadOnlyList<WorkOperation> Order(IEnumerable<WorkOperation> operations) => operations
        .Where(o => o.Status is not (OperationStatus.Finalized or OperationStatus.Canceled))
        .OrderBy(o => o.Status switch
        {
            OperationStatus.InExecution => 1,
            OperationStatus.AwaitingApproval => 2,
            OperationStatus.AwaitingPartsOrConsumables => 3,
            OperationStatus.InDiagnostic => 4,
            _ => 5
        }).ThenBy(o => o.CreatedAt).ThenBy(o => o.ServiceOrderId).ToArray();
}
