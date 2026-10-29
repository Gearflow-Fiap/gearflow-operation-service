namespace GearFlow.Operation.Domain;

// Estado técnico local: os valores não são contrato wire nem o enum global da OS.
public enum OperationStatus
{
    Received = 1,
    InDiagnostic = 2,
    AwaitingExecutionAuthorization = 3,
    ReadyForExecution = 4,
    InExecution = 5,
    ExecutionCompleted = 6,
    DiagnosticFailed = 7,
    Canceled = 8
}

public enum OperationAction
{
    Received, VehicleUpdated, DiagnosticStarted, DiagnosticCompleted, DiagnosticFailed,
    ExecutionAuthorized, ExecutionStarted, JobExecuted, ExecutionCompleted, Deactivated, Canceled
}

public enum OperationItemType { Labor, Part, Consumable }

public sealed record OperationHistory(
    long Version, OperationStatus Status, OperationAction Action, string Actor,
    DateTimeOffset At, string Description, Guid? SubjectId = null);

public sealed class OperationConflictException(string message) : Exception(message);
