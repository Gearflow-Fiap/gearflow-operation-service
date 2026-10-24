using GearFlow.Operation.Domain;
namespace GearFlow.Operation.Application;

/// <summary>Primeiro recorte local; não substitui consumidores, inbox, outbox e Saga distribuída.</summary>
public sealed class OperationService(IOperationUnitOfWork unitOfWork, TimeProvider clock)
{
    public async Task Receive(Guid orderId, Guid correlationId, string actor, CancellationToken ct = default)
    {
        ValidateCorrelation(orderId, correlationId);
        if (await unitOfWork.Load(orderId, ct) is not null)
            throw new OperationConflictException("Operação já recebida. Deduplicação deve ocorrer na inbox.");
        var now = clock.GetUtcNow();
        await unitOfWork.Commit(new WorkOperation(orderId, actor, now),
            new(Guid.NewGuid(), orderId, correlationId, "OperationReceived.v1", OperationStatus.Received, now), ct);
    }

    public Task StartDiagnostic(Guid id, Guid correlation, string actor, CancellationToken ct = default) =>
        Change(id, correlation, "DiagnosticStarted.v1", (o, now) => o.StartDiagnostic(actor, now), ct);
    public Task CompleteDiagnostic(Guid id, Guid correlation, string actor, CancellationToken ct = default) =>
        Change(id, correlation, "DiagnosticFinished.v1", (o, now) => o.CompleteDiagnostic(actor, now), ct);
    public Task AcceptExecution(Guid id, Guid correlation, IEnumerable<Guid> jobs, bool stockReserved, string actor, CancellationToken ct = default) =>
        Change(id, correlation, stockReserved ? "ExecutionStarted.v1" : "ExecutionWaitingForStock.v1",
            (o, now) => o.AcceptExecution(jobs, stockReserved, actor, now), ct);
    public Task ResumeExecution(Guid id, Guid correlation, string actor, CancellationToken ct = default) =>
        Change(id, correlation, "ExecutionStarted.v1", (o, now) => o.ResumeExecution(actor, now), ct);
    public Task ExecuteJob(Guid id, Guid correlation, Guid job, string actor, CancellationToken ct = default) =>
        Change(id, correlation, "ExecutionJobCompleted.v1", (o, now) => o.ExecuteJob(job, actor, now), ct);
    public Task FinalizeExecution(Guid id, Guid correlation, string actor, CancellationToken ct = default) =>
        Change(id, correlation, "ExecutionFinished.v1", (o, now) => o.FinalizeExecution(actor, now), ct);
    public Task RejectBudget(Guid id, Guid correlation, string actor, CancellationToken ct = default) =>
        Change(id, correlation, "OperationCanceled.v1", (o, now) => o.RejectBudget(actor, now), ct);

    public Task CancelDiagnostic(Guid id, Guid correlation, string actor, CancellationToken ct = default) =>
        Change(id, correlation, "DiagnosticCanceled.v1", (o, now) => o.CancelDiagnostic(actor, now), ct);

    private async Task Change(Guid id, Guid correlation, string type, Action<WorkOperation, DateTimeOffset> change, CancellationToken ct)
    {
        ValidateCorrelation(id, correlation);
        var operation = await unitOfWork.Load(id, ct) ?? throw new KeyNotFoundException("Operação não encontrada.");
        var now = clock.GetUtcNow();
        change(operation, now);
        await unitOfWork.Commit(operation, new(Guid.NewGuid(), id, correlation, type, operation.Status, now), ct);
    }
    private static void ValidateCorrelation(Guid id, Guid correlation)
    {
        if (correlation == Guid.Empty || correlation != id) throw new ArgumentException("CorrelationId deve ser igual ao ServiceOrderId.", nameof(correlation));
    }
}
