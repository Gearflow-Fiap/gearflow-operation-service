using GearFlow.Operation.Domain;

namespace GearFlow.Operation.Application;

public sealed partial class OperationService
{
    public Task<CommandResult> Receive(CommandContext context, Guid vehicleId, IEnumerable<RequestedJob> requestedJobs,
        IEnumerable<RequestedPart> requestedParts, int? serviceOrderCode = null, CancellationToken ct = default)
    {
        var jobs = Copy(requestedJobs); var parts = Copy(requestedParts);
        var payload = new { VehicleId = vehicleId, ServiceOrderCode = serviceOrderCode,
            Jobs = jobs.OrderBy(item => item.LineId).ToArray(), Parts = parts.OrderBy(item => item.LineId).ToArray() };
        return Execute(context, "Receive", payload, OperationEventKind.DiagnosticQueued, operation =>
        {
            if (operation is not null)
            {
                if (operation.IsTombstone || operation.Status == OperationStatus.Canceled)
                    throw new OperationConflictException("Recebimento atrasado para operação cancelada.");
                if (operation.InitialVehicleId != vehicleId || operation.ServiceOrderCode != serviceOrderCode ||
                    !operation.RequestedJobs.OrderBy(item => item.LineId).SequenceEqual(jobs.OrderBy(item => item.LineId)) ||
                    !operation.RequestedParts.OrderBy(item => item.LineId).SequenceEqual(parts.OrderBy(item => item.LineId)))
                    throw new OperationConflictException("Recebimento conflitante com o pedido inicial.");
                return Task.FromResult(operation);
            }
            return Task.FromResult(new WorkOperation(context.ServiceOrderId, vehicleId, jobs, parts, context.Actor, clock.GetUtcNow(), serviceOrderCode));
        }, ct);
    }
}
