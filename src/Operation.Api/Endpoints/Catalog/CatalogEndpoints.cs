using GearFlow.Operation.Application;

namespace GearFlow.Operation.Api;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/catalog/jobs").RequireAuthorization("Staff").WithTags("Catalog").WithStaffErrorResponses();
        group.MapGet("/", (int? page, int? pageSize, bool? includeInactive, IJobCatalog jobs, CancellationToken ct) =>
            jobs.List(PageRequest.Normalize(page ?? 1, pageSize ?? 20), includeInactive ?? false, ct))
            .WithSummary("Listar serviços de mão de obra")
            .WithDescription("Consulta o catálogo próprio do Operation. Por padrão retorna apenas ativos; includeInactive inclui desativados. page padrão 1; pageSize padrão 20, limitado a 1–100. Peças e insumos pertencem ao Inventory.")
            .Produces<PageResult<CatalogJob>>();
        group.MapGet("/{id:guid}", async (Guid id, HttpContext http, IJobCatalog jobs, CancellationToken ct) =>
            Reply(http, await jobs.Find(id, ct) ?? throw new KeyNotFoundException()))
            .WithSummary("Consultar serviço de mão de obra")
            .WithDescription("Retorna o cadastro, inclusive se desativado, e ETag forte da versão para atualização/desativação. ID inexistente retorna 404.")
            .Produces<CatalogJob>().ProducesProblem(404);
        group.MapPost("/", async (JobRequest body, HttpContext http, IJobCatalog jobs, CancellationToken ct) =>
        {
            var job = await jobs.Create(body.ToDefinition(), StaffActor.Read(http.User), ct);
            StrongETag.Write(http.Response, job.Version);
            return Results.Created($"/api/catalog/jobs/{job.Id:D}", job);
        })
            .WithSummary("Cadastrar serviço de mão de obra")
            .WithDescription("Cria item ativo com nome de até 128 caracteres, descrição de até 2000 e priceCents inteiro não negativo em centavos BRL. Retorna 201, Location e ETag. Esta rota não exige If-Match nem Idempotency-Key.")
            .Produces<CatalogJob>(StatusCodes.Status201Created);
        group.MapPut("/{id:guid}", async (Guid id, JobRequest body, HttpContext http, IJobCatalog jobs, CancellationToken ct) =>
            Reply(http, await jobs.Update(id, StrongETag.RequireIfMatch(http.Request), body.ToDefinition(), StaffActor.Read(http.User), ct)))
            .WithSummary("Atualizar serviço de mão de obra")
            .WithDescription("Atualiza nome, descrição e preço em centavos BRL. Exige If-Match forte obtido no GET; retorna novo ETag. Versão divergente retorna 412; snapshots de diagnósticos existentes não são recalculados.")
            .Produces<CatalogJob>().ProducesProblem(404).ProducesProblem(412).ProducesProblem(428);
        group.MapDelete("/{id:guid}", async (Guid id, HttpContext http, IJobCatalog jobs, CancellationToken ct) =>
            Reply(http, await jobs.Deactivate(id, StrongETag.RequireIfMatch(http.Request), StaffActor.Read(http.User), ct)))
            .WithSummary("Desativar serviço de mão de obra")
            .WithDescription("Desativa logicamente o cadastro, preservando seu histórico de uso e snapshots. Exige If-Match; retorna 200 com cadastro e ETag, não 204. Não remove fisicamente o item.")
            .Produces<CatalogJob>().ProducesProblem(404).ProducesProblem(412).ProducesProblem(428);
    }
    private static IResult Reply(HttpContext http, CatalogJob job) { StrongETag.Write(http.Response, job.Version); return Results.Ok(job); }
}
