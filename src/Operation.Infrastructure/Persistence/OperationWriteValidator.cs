using GearFlow.Operation.Application;
using GearFlow.Operation.Domain;

namespace GearFlow.Operation.Infrastructure.Persistence;

internal static class OperationWriteValidator
{
    public static void Validate(OperationWrite write)
    {
        var operation = write.Operation;
        var result = write.Receipt.Result;
        var expected = write.ExpectedStoredVersion;
        var changed = !expected.HasValue || operation.Version != expected;
        if (write.Receipt.IntentId == Guid.Empty || write.Receipt.Fingerprint.Length != 64 ||
            !write.Receipt.Fingerprint.All(Uri.IsHexDigit) || expected is <= 0 ||
            operation.Version != (expected ?? 0) + (changed ? 1 : 0) || operation.History.Count != operation.Version ||
            result.ServiceOrderId != operation.ServiceOrderId || result.Version != operation.Version || result.Status != operation.Status ||
            result.DiagnosticId != operation.DiagnosticId || result.BudgetId != operation.Authorization?.BudgetId || result.StateChanged != changed ||
            write.PendingEvents.Count != (changed ? 1 : 0))
            throw new ArgumentException("Gravação inconsistente com resultado, versão ou intenção.", nameof(write));
        foreach (var item in write.PendingEvents)
        {
            if (item.EventId == Guid.Empty || item.ServiceOrderId != operation.ServiceOrderId || item.CausationId != write.Receipt.IntentId ||
                item.Version != operation.Version || item.OccurredAt != operation.UpdatedAt || item.Actor != operation.History[^1].Actor ||
                item.Kind != Kind(operation.History[^1].Action) || StoredJson.Write(item.Data) != StoredJson.Write(OperationDetails.From(operation)))
                throw new ArgumentException("Evento pendente diverge do estado gravado.", nameof(write));
        }
    }

    public static void PreservePrefix(WorkOperation previous, WorkOperation next)
    {
        if (previous.InitialVehicleId != next.InitialVehicleId || previous.DiagnosticId != next.DiagnosticId ||
            previous.ServiceOrderCode != next.ServiceOrderCode || !previous.RequestedJobs.SequenceEqual(next.RequestedJobs) ||
            !previous.RequestedParts.SequenceEqual(next.RequestedParts) ||
            !previous.History.SequenceEqual(next.History.Where(item => item.Version <= previous.Version)) ||
            previous.Diagnostic is not null && (next.Diagnostic is null || !previous.Diagnostic.HasSameContent(next.Diagnostic)) ||
            previous.Authorization is not null && (next.Authorization is null || previous.Authorization.AuthorizationId != next.Authorization.AuthorizationId ||
                !previous.Authorization.HasSameTerms(next.Authorization)) ||
            next.Version == previous.Version && StoredJson.Write(OperationDetails.From(previous)) != StoredJson.Write(OperationDetails.From(next)))
            throw new ArgumentException("Gravação tenta reescrever estado imutável/histórico anterior.");
    }

    private static OperationEventKind Kind(OperationAction action) => action switch
    {
        OperationAction.Received => OperationEventKind.DiagnosticQueued,
        OperationAction.VehicleUpdated => OperationEventKind.VehicleUpdated,
        OperationAction.DiagnosticStarted => OperationEventKind.DiagnosticStarted,
        OperationAction.DiagnosticCompleted => OperationEventKind.DiagnosticFinished,
        OperationAction.DiagnosticFailed => OperationEventKind.DiagnosticFailed,
        OperationAction.ExecutionAuthorized => OperationEventKind.ExecutionAuthorized,
        OperationAction.ExecutionStarted => OperationEventKind.ExecutionStarted,
        OperationAction.JobExecuted => OperationEventKind.JobExecuted,
        OperationAction.ExecutionCompleted => OperationEventKind.ExecutionFinished,
        OperationAction.Deactivated => OperationEventKind.OperationDeactivated,
        OperationAction.Canceled => OperationEventKind.OperationCanceled,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };
}
