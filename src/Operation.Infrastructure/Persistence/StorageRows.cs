using GearFlow.Operation.Application;
using GearFlow.Operation.Domain;

namespace GearFlow.Operation.Infrastructure.Persistence;

internal sealed class OperationRow
{
    public Guid ServiceOrderId { get; set; }
    public int? ServiceOrderCode { get; set; }
    public Guid? VehicleId { get; set; }
    public OperationStatus Status { get; set; }
    public bool IsActive { get; set; }
    public bool IsTombstone { get; set; }
    public long Version { get; set; }
    public int StateFormat { get; set; } = 1;
    public string StateJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DiagnosticStartedAt { get; set; }
    public DateTimeOffset? DiagnosticCompletedAt { get; set; }
    public DateTimeOffset? ExecutionAuthorizedAt { get; set; }
    public DateTimeOffset? ExecutionStartedAt { get; set; }
    public DateTimeOffset? ExecutionCompletedAt { get; set; }
}

internal sealed class HistoryRow
{
    public Guid ServiceOrderId { get; set; }
    public long Version { get; set; }
    public OperationStatus Status { get; set; }
    public OperationAction Action { get; set; }
    public string Actor { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public string Description { get; set; } = "";
    public Guid? SubjectId { get; set; }
}

internal sealed class ReceiptRow
{
    public Guid IntentId { get; set; }
    public Guid ServiceOrderId { get; set; }
    public string Fingerprint { get; set; } = "";
    public string ResultJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class OutboxRow
{
    public Guid EventId { get; set; }
    public Guid ServiceOrderId { get; set; }
    public Guid CausationId { get; set; }
    public long Version { get; set; }
    public OperationEventKind Kind { get; set; }
    public string EventJson { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
}
