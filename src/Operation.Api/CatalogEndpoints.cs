using System.Globalization;
using System.Security.Claims;
using GearFlow.Operation.Application;

namespace GearFlow.Operation.Api;

public sealed record JobRequest(string Name, string Description, int PriceCents);

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/catalog/jobs").RequireAuthorization("Staff").WithTags("Catalog");
        group.MapGet("/", (int? page, int? pageSize, bool? includeInactive, IJobCatalog jobs, CancellationToken ct) =>
            jobs.List(PageRequest.Normalize(page ?? 1, pageSize ?? 20), includeInactive ?? false, ct));
        group.MapGet("/{id:guid}", async (Guid id, HttpContext http, IJobCatalog jobs, CancellationToken ct) =>
            Reply(http, await jobs.Find(id, ct) ?? throw new KeyNotFoundException())).Produces<CatalogJob>().ProducesProblem(404);
        group.MapPost("/", async (JobRequest body, HttpContext http, IJobCatalog jobs, CancellationToken ct) =>
        {
            var job = await jobs.Create(new(body.Name, body.Description, body.PriceCents), Actor(http), ct);
            Headers(http, job.Version);
            return Results.Created($"/api/catalog/jobs/{job.Id:D}", job);
        }).Produces<CatalogJob>(StatusCodes.Status201Created);
        group.MapPut("/{id:guid}", async (Guid id, JobRequest body, HttpContext http, IJobCatalog jobs, CancellationToken ct) =>
            Reply(http, await jobs.Update(id, Version(http), new(body.Name, body.Description, body.PriceCents), Actor(http), ct))).Produces<CatalogJob>();
        group.MapDelete("/{id:guid}", async (Guid id, HttpContext http, IJobCatalog jobs, CancellationToken ct) =>
            Reply(http, await jobs.Deactivate(id, Version(http), Actor(http), ct))).Produces<CatalogJob>();
    }
    private static long Version(HttpContext http)
    {
        var header = http.Request.Headers.IfMatch;
        if (header.Count == 0) throw new HttpInputException(428, "if_match_required");
        var text = header.Count == 1 ? header[0] : null;
        if (text is null || text.Length < 3 || text[0] != '"' || text[^1] != '"' ||
            !long.TryParse(text.AsSpan(1, text.Length - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var version) || version < 1 || Tag(version) != text)
            throw new HttpInputException(400, "invalid_if_match");
        return version;
    }
    private static string Actor(HttpContext http) => "staff:" + http.User.FindFirstValue("sub");
    private static IResult Reply(HttpContext http, CatalogJob job) { Headers(http, job.Version); return Results.Ok(job); }
    private static void Headers(HttpContext http, long version) { http.Response.Headers.ETag = Tag(version); http.Response.Headers.CacheControl = "no-store"; }
    private static string Tag(long version) => "\"" + version.ToString(CultureInfo.InvariantCulture) + "\"";
}


