using GearFlow.Operation.Domain;

namespace GearFlow.Operation.Application;

/// <summary>
/// Núcleo inicial adaptado às assinaturas do domínio do passo 2.
/// Casos de uso completos, consultas, política de preço e adapters ficam para o passo 3/incrementos posteriores.
/// OperationEvent permanece registro interno, não contrato publicado no RabbitMQ.
/// </summary>
public sealed class OperationService(IOperationUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task Receive(Guid orderId, Guid correlationId, Guid vehicleId,
        IEnumerable<RequestedJob> requestedJobs, IEnumerable<RequestedPart> requestedParts,
        string actor, CancellationToken ct = default)
    {
        ValidateCorrelation(orderId, correlationId);
        if (await unitOfWork.Load(orderId, ct) is not null)
            throw new OperationConflictException("Operação já recebida. Journal de intenção será tratado no passo 3.");
        var now = clock.GetUtcNow();
        await unitOfWork.Commit(new WorkOperation(orderId, vehicleId, requestedJobs, requestedParts, actor, now),
            new(Guid.NewGuid(), orderId, correlationId, "OperationReceived.v1", OperationStatus.Received, now), ct);
    }

    public Task StartDiagnostic(Guid id, Guid correlation, string actor, CancellationToken ct = default) =>
        Change(id, correlation, "DiagnosticStarted.v1", (operation, now) => operation.StartDiagnostic(actor, now), ct);

    public Task CompleteDiagnostic(Guid id, Guid correlation, DiagnosticSnapshot snapshot,
        IEnumerable<DiagnosticConsumable> consumables, string actor, CancellationToken ct = default) =>
        Change(id, correlation, "DiagnosticFinished.v1", (operation, now) => operation.CompleteDiagnostic(snapshot, consumables, actor, now), ct);

    public Task AuthorizeExecution(Guid id, Guid correlation, ExecutionAuthorization authorization, string actor, CancellationToken ct = default) =>
        Change(id, correlation, "ExecutionAuthorized.v1", (operation, now) => operation.AuthorizeExecution(authorization, actor, now), ct);

    public Task StartExecution(Guid id, Guid correlation, string actor, CancellationToken ct = default) =>
        Change(id, correlation, "ExecutionStarted.v1", (operation, now) => operation.StartExecution(actor, now), ct);

    public Task ExecuteJob(Guid id, Guid correlation, Guid budgetJobId, string actor, CancellationToken ct = default) =>
        Change(id, correlation, "ExecutionJobCompleted.v1", (operation, now) => operation.ExecuteJob(budgetJobId, actor, now), ct);

    public Task FinalizeExecution(Guid id, Guid correlation, string actor, CancellationToken ct = default) =>
        Change(id, correlation, "ExecutionFinished.v1", (operation, now) => operation.FinalizeExecution(actor, now), ct);

    public Task CancelOperation(Guid id, Guid correlation, string reason, string actor, CancellationToken ct = default) =>
        Change(id, correlation, "OperationCanceled.v1", (operation, now) => operation.CancelOperation(reason, actor, now), ct);

    private async Task Change(Guid id, Guid correlation, string type, Action<WorkOperation, DateTimeOffset> change, CancellationToken ct)
    {
        ValidateCorrelation(id, correlation);
        var operation = await unitOfWork.Load(id, ct) ?? throw new KeyNotFoundException("Operação não encontrada.");
        var version = operation.Version;
        var now = clock.GetUtcNow();
        change(operation, now);
        if (operation.Version == version) return;
        await unitOfWork.Commit(operation, new(Guid.NewGuid(), id, correlation, type, operation.Status, now), ct);
    }

    private static void ValidateCorrelation(Guid id, Guid correlation)
    {
        if (correlation == Guid.Empty || correlation != id)
            throw new ArgumentException("CorrelationId deve ser igual ao ServiceOrderId.", nameof(correlation));
    }
}
