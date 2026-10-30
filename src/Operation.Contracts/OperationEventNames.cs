using System.Collections.Frozen;

namespace GearFlow.Contracts.Operation.V1;

/// <summary>Nomes de exchange dos eventos; namespace/URN e payload permanecem independentes.</summary>
public static class OperationEventNames
{
    public static IReadOnlyDictionary<Type, string> All { get; } = new Dictionary<Type, string>
    {
        [typeof(DiagnosticQueued)] = "operation.diagnostic-queued",
        [typeof(DiagnosticStarted)] = "operation.diagnostic-started",
        [typeof(DiagnosticFinished)] = "operation.diagnostic-finished",
        [typeof(DiagnosticFailed)] = "operation.diagnostic-failed",
        [typeof(ExecutionAuthorized)] = "operation.execution-authorized",
        [typeof(ExecutionStarted)] = "operation.execution-started",
        [typeof(JobExecuted)] = "operation.job-executed",
        [typeof(ExecutionFinished)] = "operation.execution-finished",
        [typeof(VehicleUpdated)] = "operation.vehicle-updated",
        [typeof(OperationDeactivated)] = "operation.deactivated",
        [typeof(OperationCanceled)] = "operation.canceled",
        [typeof(OperationCommandRejected)] = "operation.command-rejected",
        [typeof(OperationCancellationRejected)] = "operation.cancellation-rejected",
    }.ToFrozenDictionary();
}
