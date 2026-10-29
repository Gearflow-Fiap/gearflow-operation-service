using GearFlow.Operation.Domain;

namespace GearFlow.Operation.Application;

public sealed partial class OperationService(IOperationUnitOfWork unitOfWork, DiagnosticPricer pricing, TimeProvider clock)
{
    private Task<CommandResult> Change(CommandContext context, string action, object payload, OperationEventKind kind,
        Action<WorkOperation> change, CancellationToken ct) => Execute(context, action, payload, kind, existing =>
        {
            var operation = Found(existing);
            change(operation);
            return Task.FromResult(operation);
        }, ct);

    private async Task<CommandResult> Execute(CommandContext context, string action, object payload, OperationEventKind kind,
        Func<WorkOperation?, Task<WorkOperation>> change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ct.ThrowIfCancellationRequested();
        using var telemetry = OperationTelemetry.Start(action, context.ServiceOrderId, context.IntentId);
        var fingerprint = CommandFingerprint.Create(action, context, payload);
        var previous = await unitOfWork.FindReceipt(context.IntentId, ct);
        if (previous is not null)
        {
            if (previous.Fingerprint != fingerprint) throw new IntentConflictException("Intenção reutilizada com ação, OS, ator ou conteúdo diferentes.");
            telemetry.Complete("replay");
            return previous.Result; // Replay vem antes de precondição de versão e de consultas externas.
        }
        var existing = await unitOfWork.Load(context.ServiceOrderId, ct);
        var storedVersion = existing?.Version;
        if (context.ExpectedVersion.HasValue && context.ExpectedVersion.Value != (storedVersion ?? 0))
            throw new OperationConcurrencyException("Versão esperada diverge do estado atual.");
        var operation = await change(existing);
        var changed = !storedVersion.HasValue || operation.Version != storedVersion.Value;
        var result = new CommandResult(operation.ServiceOrderId, operation.Status, operation.Version,
            operation.DiagnosticId, operation.Authorization?.BudgetId, changed);
        var events = changed ? new[] { new OperationEvent(Guid.NewGuid(), operation.ServiceOrderId, context.IntentId,
            kind, operation.Version, operation.UpdatedAt, context.Actor, OperationDetails.From(operation), OperationTelemetry.TraceParent, OperationTelemetry.TraceState) } : [];
        var receipt = new CommandReceipt(context.IntentId, fingerprint, result);
        ct.ThrowIfCancellationRequested();
        var committed = await unitOfWork.Commit(new(operation, storedVersion, receipt, Array.AsReadOnly(events)), ct);
        telemetry.Complete(committed.StateChanged ? "changed" : "unchanged");
        return committed;
    }

    private static WorkOperation Found(WorkOperation? operation) => operation ?? throw new KeyNotFoundException("Operação não encontrada.");

    private static void Active(WorkOperation operation)
    {
        if (!operation.IsActive) throw new OperationConflictException("Operação inativa/cancelada não pode executar trabalho.");
    }

    private static string Reason(string reason) => string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 2000
        ? throw new ArgumentException("Motivo obrigatório, com até 2000 caracteres.", nameof(reason)) : reason.Trim();

    private static T[] Copy<T>(IEnumerable<T> items) where T : class
    {
        ArgumentNullException.ThrowIfNull(items);
        var copy = items.ToArray();
        if (copy.Any(item => item is null)) throw new ArgumentException("Item nulo.", nameof(items));
        return copy;
    }
}
