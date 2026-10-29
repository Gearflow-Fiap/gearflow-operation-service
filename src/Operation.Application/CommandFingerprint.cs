using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using GearFlow.Operation.Domain;

namespace GearFlow.Operation.Application;

internal static class CommandFingerprint
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Create(string action, CommandContext context, object payload) => Convert.ToHexString(
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Schema = "operation-application-command-v1", Action = action, context.ServiceOrderId, context.Actor, Payload = payload
        }, Json)));

    public static object Consumables(IEnumerable<DiagnosticConsumable> items) => items.OrderBy(item => item.LineId)
        .Select(item => new { item.LineId, item.ConsumableId, Quantity = Number(item.Quantity) }).ToArray();

    public static object Authorization(ExecutionAuthorization authorization) => new
    {
        authorization.AuthorizationId, authorization.BudgetId, authorization.BudgetRevision,
        authorization.Snapshot.DiagnosticId, authorization.Snapshot.PricingPolicyId,
        PricedAt = authorization.Snapshot.PricedAt.ToUniversalTime(), authorization.Snapshot.Currency,
        Lines = authorization.Snapshot.Lines.OrderBy(line => line.LineId).Select(line => new
        {
            line.LineId, line.ItemType, line.ItemId, line.Name, Quantity = Number(line.Quantity), line.UnitPriceCents, line.LineTotalCents
        }).ToArray(),
        Jobs = authorization.Jobs.OrderBy(job => job.BudgetJobId).Select(job => new { job.BudgetJobId, job.DiagnosticLineId }).ToArray()
    };

    private static string Number(decimal value) => value.ToString("G29", CultureInfo.InvariantCulture);
}
