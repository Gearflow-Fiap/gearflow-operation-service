using GearFlow.Operation.Domain;

namespace GearFlow.Operation.Application;

public static class ExecutionQueue
{
    // Fila acionável do desenho alvo; espera comercial continua na consulta global/Saga.
    public static IReadOnlyList<WorkOperation> Order(IEnumerable<WorkOperation> operations) => Array.AsReadOnly(operations
        .Where(operation => operation.IsActive && operation.Status is OperationStatus.InExecution or
            OperationStatus.ReadyForExecution or OperationStatus.InDiagnostic or OperationStatus.Received)
        .OrderBy(operation => operation.Status switch
        {
            OperationStatus.InExecution => 1,
            OperationStatus.ReadyForExecution => 2,
            OperationStatus.InDiagnostic => 3,
            _ => 4
        })
        .ThenBy(operation => operation.CreatedAt)
        .ThenBy(operation => operation.ServiceOrderId)
        .ToArray());
}
