using System.Globalization;
using System.Security.Claims;
using GearFlow.Operation.Application;
using GearFlow.Operation.Domain;

namespace GearFlow.Operation.Api;

public static class OperationEndpoints
{
    public static void MapOperationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/operation").RequireAuthorization("Staff").WithTags("Operation");
        group.MapGet("/queue", (int? page, int? pageSize, OperationStatus? status, OperationQueries queries, CancellationToken ct) =>
            queries.Queue(page ?? 1, pageSize ?? 20, status, ct));
        group.MapGet("/service-orders", (int? page, int? pageSize, OperationStatus? status, bool? includeInactive, OperationQueries queries, CancellationToken ct) =>
            queries.List(page ?? 1, pageSize ?? 20, status, includeInactive ?? false, ct));
        group.MapGet("/durations", (OperationQueries queries, CancellationToken ct) => queries.Durations(ct));
        group.MapGet("/service-orders/{id:guid}", async (Guid id, HttpContext http, OperationQueries queries, CancellationToken ct) =>
        {
            var result = await queries.Get(id, ct);
            http.Response.Headers.ETag = Tag(result.Version);
            http.Response.Headers.CacheControl = "no-store";
            return result;
        });
        group.MapGet("/service-orders/{id:guid}/history", (Guid id, int? page, int? pageSize, OperationQueries queries, CancellationToken ct) =>
            queries.History(id, page ?? 1, pageSize ?? 20, ct));
        var commands = group.MapGroup("/service-orders/{id:guid}");
        commands.WithMetadata(new Microsoft.AspNetCore.Http.ProducesResponseTypeMetadata(200, typeof(CommandResult), ["application/json"]));
        commands.MapPost("/diagnostic/start", async (Guid id, HttpContext http, OperationService service, CancellationToken ct) =>
            Reply(http, await service.StartDiagnostic(Context(http, id), ct)));
        commands.MapPost("/diagnostic/finish", async (Guid id, FinishDiagnosticRequest body, HttpContext http, OperationService service, CancellationToken ct) =>
        {
            var context = Context(http, id);
            if (body.Consumables is null || body.Consumables.Length > DiagnosticSnapshot.MaximumLines || body.Consumables.Any(item => item is null))
                throw new HttpInputException(400, "invalid_consumables");
            return Reply(http, await service.CompleteDiagnostic(context,
                body.Consumables.Select(item => new DiagnosticConsumable(item.LineId, item.ConsumableId, item.Quantity)), ct));
        });
        commands.MapPost("/diagnostic/fail", async (Guid id, FailDiagnosticRequest body, HttpContext http, OperationService service, CancellationToken ct) =>
            Reply(http, await service.FailDiagnostic(Context(http, id), body.Reason, ct)));
        commands.MapPost("/execution/start", async (Guid id, HttpContext http, OperationService service, CancellationToken ct) =>
            Reply(http, await service.StartExecution(Context(http, id), ct)));
        commands.MapPost("/execution/jobs/{budgetJobId:guid}/done", async (Guid id, Guid budgetJobId, HttpContext http, OperationService service, CancellationToken ct) =>
            Reply(http, await service.ExecuteJob(Context(http, id), budgetJobId, ct)));
        commands.MapPost("/execution/finish", async (Guid id, HttpContext http, OperationService service, CancellationToken ct) =>
            Reply(http, await service.FinalizeExecution(Context(http, id), ct)));
    }

    private static CommandContext Context(HttpContext http, Guid id)
    {
        var key = http.Request.Headers["Idempotency-Key"];
        if (key.Count != 1 || !Guid.TryParse(key[0], out var intent) || intent == Guid.Empty)
            throw new HttpInputException(400, "invalid_idempotency_key");
        var match = http.Request.Headers.IfMatch;
        if (match.Count == 0) throw new HttpInputException(428, "if_match_required");
        var tag = match.Count == 1 ? match[0] : null;
        if (tag is null || tag.Length < 3 || tag[0] != '"' || tag[^1] != '"' ||
            !long.TryParse(tag.AsSpan(1, tag.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1 || Tag(version) != tag)
            throw new HttpInputException(400, "invalid_if_match");
        return new(intent, id, "staff:" + http.User.FindFirstValue("sub"), version);
    }

    private static IResult Reply(HttpContext http, CommandResult result)
    {
        http.Response.Headers.ETag = Tag(result.Version);
        http.Response.Headers.CacheControl = "no-store";
        return Results.Ok(result);
    }
    private static string Tag(long version) => "\"" + version.ToString(CultureInfo.InvariantCulture) + "\"";
}
