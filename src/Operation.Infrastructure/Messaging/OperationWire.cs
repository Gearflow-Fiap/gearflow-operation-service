using System.Text.Json;
using System.Text.Json.Serialization;
using GearFlow.Operation.Application;
using W = GearFlow.Contracts.Operation.V1;

namespace GearFlow.Operation.Infrastructure.Messaging;

public static class OperationWire
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter(allowIntegerValues: false) } };
    private static readonly IReadOnlyDictionary<string, Type> Types = new[] { typeof(W.DiagnosticQueued), typeof(W.VehicleUpdated), typeof(W.DiagnosticStarted),
        typeof(W.DiagnosticFinished), typeof(W.DiagnosticFailed), typeof(W.ExecutionAuthorized), typeof(W.ExecutionStarted), typeof(W.JobExecuted),
        typeof(W.ExecutionFinished), typeof(W.OperationDeactivated), typeof(W.OperationCanceled), typeof(W.OperationCommandRejected), typeof(W.OperationCancellationRejected) }
        .ToDictionary(type => type.Name, type => type);
    public static string Serialize(object value) => JsonSerializer.Serialize(value, value.GetType(), Options);
    public static object Deserialize(string name, string json) => Types.TryGetValue(name, out var type)
        ? JsonSerializer.Deserialize(json, type, Options) ?? throw new InvalidDataException("Resposta vazia.") : throw new InvalidDataException("Tipo de resposta não suportado.");
    public static W.DiagnosticSnapshot Snapshot(DiagnosticDto value) => new(value.DiagnosticId, value.Currency, value.PricingPolicyId, value.PricedAt,
        value.TotalPriceCents, value.Lines.Select(line => new W.CommercialLine(line.LineId, (W.CommercialItemType)line.ItemType, line.ItemId, line.Name, line.Quantity, line.UnitPriceCents, line.LineTotalCents)).ToArray());
    public static object Project(OperationEvent value)
    {
        var d = value.Data; var id = value.ServiceOrderId; var e = value.EventId; var c = value.CausationId; var at = value.OccurredAt; var v = value.Version; var actor = value.Actor;
        return value.Kind switch
        {
            OperationEventKind.DiagnosticQueued => new W.DiagnosticQueued(id, e, c, at, v, actor, d.DiagnosticId!.Value),
            OperationEventKind.VehicleUpdated => new W.VehicleUpdated(id, e, c, at, v, actor, d.VehicleId!.Value),
            OperationEventKind.DiagnosticStarted => new W.DiagnosticStarted(id, e, c, at, v, actor, d.DiagnosticId!.Value),
            OperationEventKind.DiagnosticFinished => new W.DiagnosticFinished(id, e, c, at, v, actor, Snapshot(d.Diagnostic!)),
            OperationEventKind.DiagnosticFailed => new W.DiagnosticFailed(id, e, c, at, v, actor, d.DiagnosticId!.Value, "DiagnosticNotPossible", d.DiagnosticFailureReason!),
            OperationEventKind.ExecutionAuthorized => new W.ExecutionAuthorized(id, e, c, at, v, actor, d.DiagnosticId!.Value, d.Authorization!.BudgetId, d.Authorization.BudgetRevision, d.Authorization.AuthorizationId),
            OperationEventKind.ExecutionStarted => new W.ExecutionStarted(id, e, c, at, v, actor, d.DiagnosticId!.Value, d.Authorization!.BudgetId, d.Authorization.AuthorizationId),
            OperationEventKind.JobExecuted => Job(value),
            OperationEventKind.ExecutionFinished => new W.ExecutionFinished(id, e, c, at, v, actor, d.DiagnosticId!.Value, d.Authorization!.BudgetId, d.Authorization.AuthorizationId, d.Jobs.Where(job => job.ExecutedAt.HasValue).Select(job => job.BudgetJobId).ToArray()),
            OperationEventKind.OperationDeactivated => new W.OperationDeactivated(id, e, c, at, v, actor, d.DeactivationReason!),
            OperationEventKind.OperationCanceled => new W.OperationCanceled(id, e, c, at, v, actor, d.CancellationReason!),
            _ => throw new InvalidDataException("Evento não suportado.")
        };
    }
    private static W.JobExecuted Job(OperationEvent value)
    {
        var d = value.Data; var job = d.Jobs.Single(item => item.BudgetJobId == d.History[^1].SubjectId);
        return new(value.ServiceOrderId, value.EventId, value.CausationId, value.OccurredAt, value.Version, value.Actor,
            d.DiagnosticId!.Value, d.Authorization!.BudgetId, job.BudgetJobId, job.DiagnosticLineId, job.CatalogJobId, job.ExecutedAt!.Value);
    }
}
