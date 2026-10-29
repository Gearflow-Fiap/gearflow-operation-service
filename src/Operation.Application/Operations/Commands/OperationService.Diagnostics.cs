using GearFlow.Operation.Domain;

namespace GearFlow.Operation.Application;

public sealed partial class OperationService
{
    public Task<CommandResult> StartDiagnostic(CommandContext context, CancellationToken ct = default) =>
        Change(context, "StartDiagnostic", new { }, OperationEventKind.DiagnosticStarted, operation =>
        {
            Active(operation);
            if (operation.Status != OperationStatus.InDiagnostic) operation.StartDiagnostic(context.Actor, clock.GetUtcNow());
        }, ct);

    public Task<CommandResult> CompleteDiagnostic(CommandContext context, IEnumerable<DiagnosticConsumable> consumables,
        CancellationToken ct = default)
    {
        var items = Copy(consumables);
        return Execute(context, "CompleteDiagnostic", CommandFingerprint.Consumables(items), OperationEventKind.DiagnosticFinished,
            async existing =>
            {
                var operation = Found(existing);
                Active(operation);
                if (operation.Diagnostic is not null)
                {
                    if (!operation.Consumables.OrderBy(item => item.LineId).SequenceEqual(items.OrderBy(item => item.LineId)))
                        throw new OperationConflictException("Conclusão repetida com consumíveis divergentes.");
                    return operation;
                }
                var snapshot = await pricing.Price(operation, items, ct);
                operation.CompleteDiagnostic(snapshot, items, context.Actor, clock.GetUtcNow());
                return operation;
            }, ct);
    }

    public Task<CommandResult> FailDiagnostic(CommandContext context, string reason, CancellationToken ct = default)
    {
        var detail = Reason(reason);
        return Change(context, "FailDiagnostic", new { Reason = detail }, OperationEventKind.DiagnosticFailed, operation =>
        {
            Active(operation);
            if (operation.Status == OperationStatus.DiagnosticFailed && operation.DiagnosticFailureReason == detail) return;
            operation.FailDiagnostic(detail, context.Actor, clock.GetUtcNow());
        }, ct);
    }
}
