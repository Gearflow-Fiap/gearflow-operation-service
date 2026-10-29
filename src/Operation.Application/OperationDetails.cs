using GearFlow.Operation.Domain;

namespace GearFlow.Operation.Application;

public sealed record RequestedJobDto(Guid LineId, Guid CatalogJobId);
public sealed record RequestedPartDto(Guid LineId, Guid PartId, int Quantity);
public sealed record ConsumableDto(Guid LineId, Guid ConsumableId, decimal Quantity);
public sealed record DiagnosticLineDto(Guid LineId, OperationItemType ItemType, Guid ItemId, string Name,
    decimal Quantity, int UnitPriceCents, int LineTotalCents);
public sealed record DiagnosticDto(Guid DiagnosticId, string Currency, string PricingPolicyId, DateTimeOffset PricedAt,
    int TotalPriceCents, IReadOnlyList<DiagnosticLineDto> Lines);
public sealed record ApprovedJobDto(Guid BudgetJobId, Guid DiagnosticLineId);
public sealed record AuthorizationDto(Guid AuthorizationId, Guid BudgetId, int BudgetRevision, IReadOnlyList<ApprovedJobDto> Jobs);
public sealed record JobProgressDto(Guid BudgetJobId, Guid DiagnosticLineId, Guid CatalogJobId,
    DateTimeOffset? ExecutedAt, string? ExecutedBy);
public sealed record HistoryDto(long Version, OperationStatus Status, OperationAction Action, string Actor,
    DateTimeOffset At, string Description, Guid? SubjectId)
{
    public static HistoryDto From(OperationHistory item) => new(item.Version, item.Status, item.Action, item.Actor, item.At, item.Description, item.SubjectId);
}
public sealed record OperationListItem(Guid ServiceOrderId, int? ServiceOrderCode, Guid? VehicleId,
    OperationStatus Status, bool IsActive, long Version, DateTimeOffset CreatedAt)
{
    public static OperationListItem From(WorkOperation item) => new(item.ServiceOrderId, item.ServiceOrderCode,
        item.VehicleId, item.Status, item.IsActive, item.Version, item.CreatedAt);
}

/// <summary>Projeção de saída congelada; não expõe o agregado mutável para host/consumidor.</summary>
public sealed record OperationDetails(Guid ServiceOrderId, int? ServiceOrderCode, Guid? VehicleId, Guid? InitialVehicleId,
    Guid? DiagnosticId, OperationStatus Status, bool IsActive, bool IsTombstone, long Version,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<RequestedJobDto> RequestedJobs, IReadOnlyList<RequestedPartDto> RequestedParts,
    IReadOnlyList<ConsumableDto> Consumables, DiagnosticDto? Diagnostic, AuthorizationDto? Authorization,
    IReadOnlyList<JobProgressDto> Jobs, IReadOnlyList<HistoryDto> History,
    string? DiagnosticFailureReason, string? DeactivationReason, string? CancellationReason, PhaseDurations Durations)
{
    public static OperationDetails From(WorkOperation operation)
    {
        var diagnostic = operation.Diagnostic;
        var authorization = operation.Authorization;
        return new(operation.ServiceOrderId, operation.ServiceOrderCode, operation.VehicleId, operation.InitialVehicleId,
            operation.DiagnosticId, operation.Status, operation.IsActive, operation.IsTombstone, operation.Version,
            operation.CreatedAt, operation.UpdatedAt,
            Freeze(operation.RequestedJobs.Select(job => new RequestedJobDto(job.LineId, job.CatalogJobId))),
            Freeze(operation.RequestedParts.Select(part => new RequestedPartDto(part.LineId, part.PartId, part.Quantity))),
            Freeze(operation.Consumables.Select(item => new ConsumableDto(item.LineId, item.ConsumableId, item.Quantity))),
            diagnostic is null ? null : new(diagnostic.DiagnosticId, diagnostic.Currency, diagnostic.PricingPolicyId,
                diagnostic.PricedAt, diagnostic.TotalPriceCents, Freeze(diagnostic.Lines.Select(line => new DiagnosticLineDto(
                    line.LineId, line.ItemType, line.ItemId, line.Name, line.Quantity, line.UnitPriceCents, line.LineTotalCents)))),
            authorization is null ? null : new(authorization.AuthorizationId, authorization.BudgetId, authorization.BudgetRevision,
                Freeze(authorization.Jobs.Select(job => new ApprovedJobDto(job.BudgetJobId, job.DiagnosticLineId)))),
            Freeze(operation.Jobs.Select(job => new JobProgressDto(job.BudgetJobId, job.DiagnosticLineId, job.CatalogJobId, job.ExecutedAt, job.ExecutedBy))),
            Freeze(operation.History.Select(HistoryDto.From)), operation.DiagnosticFailureReason,
            operation.DeactivationReason, operation.CancellationReason, DurationCalculator.For(operation));
    }

    private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> items) => Array.AsReadOnly(items.ToArray());
}
