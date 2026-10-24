namespace GearFlow.Operation.Domain;

// Valores preservados do Workshop. Delivered pertence ao OS Service.
public enum OperationStatus
{
    Received = 1, InDiagnostic = 2, AwaitingApproval = 3, InExecution = 4,
    Finalized = 5, Canceled = 7, AwaitingPartsOrConsumables = 8
}
public sealed record OperationHistory(OperationStatus Status, string Actor, DateTimeOffset At);
public sealed record JobProgress(Guid JobId, DateTimeOffset? ExecutedAt);
public sealed class OperationConflictException(string message) : Exception(message);
