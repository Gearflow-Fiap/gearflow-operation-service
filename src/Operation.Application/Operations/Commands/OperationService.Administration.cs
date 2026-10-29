using GearFlow.Operation.Domain;

namespace GearFlow.Operation.Application;

public sealed partial class OperationService
{
    public Task<CommandResult> UpdateVehicle(CommandContext context, Guid vehicleId, CancellationToken ct = default) =>
        Change(context, "UpdateVehicle", new { VehicleId = vehicleId }, OperationEventKind.VehicleUpdated,
            operation => operation.UpdateVehicle(vehicleId, context.Actor, clock.GetUtcNow()), ct);

    public Task<CommandResult> Deactivate(CommandContext context, string reason, CancellationToken ct = default)
    {
        var detail = Reason(reason);
        return Change(context, "Deactivate", new { Reason = detail }, OperationEventKind.OperationDeactivated,
            operation => operation.Deactivate(detail, context.Actor, clock.GetUtcNow()), ct);
    }

    public Task<CommandResult> CancelOperation(CommandContext context, string reason, CancellationToken ct = default)
    {
        var detail = Reason(reason);
        return Execute(context, "CancelOperation", new { Reason = detail }, OperationEventKind.OperationCanceled, existing =>
        {
            var operation = existing ?? WorkOperation.CreateCanceled(context.ServiceOrderId, detail, context.Actor, clock.GetUtcNow());
            if (existing is not null) operation.CancelOperation(detail, context.Actor, clock.GetUtcNow());
            return Task.FromResult(operation);
        }, ct);
    }
}
