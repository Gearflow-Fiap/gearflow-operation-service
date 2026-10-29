using GearFlow.Operation.Domain;

namespace GearFlow.Operation.Infrastructure.Persistence;

internal static class OperationMapper
{
    public static OperationRow ToRow(WorkOperation operation) => new()
    {
        ServiceOrderId = operation.ServiceOrderId, ServiceOrderCode = operation.ServiceOrderCode, VehicleId = operation.VehicleId,
        Status = operation.Status, IsActive = operation.IsActive, IsTombstone = operation.IsTombstone, Version = operation.Version,
        StateFormat = 1, StateJson = StoredJson.Write(operation.CaptureState()), CreatedAt = operation.CreatedAt, UpdatedAt = operation.UpdatedAt,
        DiagnosticStartedAt = operation.DiagnosticStartedAt, DiagnosticCompletedAt = operation.DiagnosticCompletedAt,
        ExecutionAuthorizedAt = operation.ExecutionAuthorizedAt, ExecutionStartedAt = operation.ExecutionStartedAt,
        ExecutionCompletedAt = operation.ExecutionCompletedAt
    };

    public static WorkOperation Restore(OperationRow row, IReadOnlyList<HistoryRow> history)
    {
        if (row.StateFormat != 1) throw new NotSupportedException("Formato de armazenamento não suportado.");
        var state = StoredJson.Read<OperationState>(row.StateJson);
        var operation = WorkOperation.Restore(state, history.Select(ToHistory).ToArray());
        if (operation.ServiceOrderId != row.ServiceOrderId || operation.ServiceOrderCode != row.ServiceOrderCode || operation.VehicleId != row.VehicleId ||
            operation.Status != row.Status || operation.IsActive != row.IsActive || operation.IsTombstone != row.IsTombstone ||
            operation.Version != row.Version || operation.CreatedAt != row.CreatedAt || operation.UpdatedAt != row.UpdatedAt ||
            operation.DiagnosticStartedAt != row.DiagnosticStartedAt || operation.DiagnosticCompletedAt != row.DiagnosticCompletedAt ||
            operation.ExecutionAuthorizedAt != row.ExecutionAuthorizedAt || operation.ExecutionStartedAt != row.ExecutionStartedAt ||
            operation.ExecutionCompletedAt != row.ExecutionCompletedAt)
            throw new InvalidDataException("Metadados da operação divergem do estado auditado.");
        return operation;
    }

    public static HistoryRow ToRow(Guid serviceOrderId, OperationHistory item) => new()
    {
        ServiceOrderId = serviceOrderId, Version = item.Version, Status = item.Status, Action = item.Action,
        Actor = item.Actor, At = item.At, Description = item.Description, SubjectId = item.SubjectId
    };
    public static OperationHistory ToHistory(HistoryRow item) =>
        new(item.Version, item.Status, item.Action, item.Actor, item.At, item.Description, item.SubjectId);
}
