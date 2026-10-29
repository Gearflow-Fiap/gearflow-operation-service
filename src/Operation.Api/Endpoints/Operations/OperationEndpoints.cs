using System.Globalization;
using System.Security.Claims;
using GearFlow.Operation.Application;
using GearFlow.Operation.Domain;

namespace GearFlow.Operation.Api;

public static class OperationEndpoints
{
    public static void MapOperationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/operation").RequireAuthorization("Staff").WithTags("Operation").WithStaffErrorResponses();
        group.MapGet("/queue", (int? page, int? pageSize, OperationStatus? status, OperationQueries queries, CancellationToken ct) =>
            queries.Queue(page ?? 1, pageSize ?? 20, status, ct))
            .WithSummary("Consultar fila acionável")
            .WithDescription("Lista operações ativas nas etapas acionáveis de diagnóstico/execução; não é a fila comercial da Saga. Permite filtrar status. page padrão 1; pageSize padrão 20, limitado a 1–100.")
            .Produces<PageResult<OperationListItem>>();
        group.MapGet("/service-orders", (int? page, int? pageSize, OperationStatus? status, bool? includeInactive, OperationQueries queries, CancellationToken ct) =>
            queries.List(page ?? 1, pageSize ?? 20, status, includeInactive ?? false, ct))
            .WithSummary("Listar operações")
            .WithDescription("Consulta paginada das operações locais. Por padrão exclui inativas; includeInactive permite incluí-las. Aceita filtro de status, page e pageSize (1–100). Não representa o estado global da OS.")
            .Produces<PageResult<OperationListItem>>();
        group.MapGet("/durations", (OperationQueries queries, CancellationToken ct) => queries.Durations(ct))
            .WithSummary("Consultar tempos médios das fases")
            .WithDescription("Retorna médias em minutos e quantidade de amostras das filas e fases de diagnóstico/execução das operações ativas. Duração sem amostras é nula, não zero.")
            .Produces<DurationSummary>();
        group.MapGet("/service-orders/{id:guid}", async (Guid id, HttpContext http, OperationQueries queries, CancellationToken ct) =>
        {
            var result = await queries.Get(id, ct);
            http.Response.Headers.ETag = Tag(result.Version);
            http.Response.Headers.CacheControl = "no-store";
            return result;
        })
            .WithSummary("Consultar operação por OS")
            .WithDescription("Retorna estado local, diagnóstico, autorização e progresso. ETag contém a versão forte para If-Match nas escritas. Não altera a operação; 404 quando a OS não existe neste serviço.")
            .Produces<OperationDetails>().ProducesProblem(404);
        group.MapGet("/service-orders/{id:guid}/history", (Guid id, int? page, int? pageSize, OperationQueries queries, CancellationToken ct) =>
            queries.History(id, page ?? 1, pageSize ?? 20, ct))
            .WithSummary("Consultar histórico da operação")
            .WithDescription("Retorna histórico paginado por versão, com ação, ator e instante registrados. page padrão 1; pageSize padrão 20, limitado a 1–100. OS ausente retorna 404.")
            .Produces<PageResult<HistoryDto>>().ProducesProblem(404);
        var commands = group.MapGroup("/service-orders/{id:guid}").WithOperationCommandResponses();
        commands.MapPost("/diagnostic/start", async (Guid id, HttpContext http, OperationService service, CancellationToken ct) =>
            Reply(http, await service.StartDiagnostic(Context(http, id), ct)))
            .WithSummary("Iniciar diagnóstico físico")
            .WithDescription("Ação do funcionário sobre operação já recebida por mensagem. Exige If-Match e Idempotency-Key. Replay exato devolve resultado e ETag originais; não recebe uma nova OS nem autoriza execução comercial.");
        commands.MapPost("/diagnostic/finish", async (Guid id, FinishDiagnosticRequest body, HttpContext http, OperationService service, CancellationToken ct) =>
        {
            var context = Context(http, id);
            if (body.Consumables is null || body.Consumables.Length > DiagnosticSnapshot.MaximumLines || body.Consumables.Any(item => item is null))
                throw new HttpInputException(400, "invalid_consumables");
            return Reply(http, await service.CompleteDiagnostic(context,
                body.Consumables.Select(item => new DiagnosticConsumable(item.LineId, item.ConsumableId, item.Quantity)), ct));
        })
            .WithSummary("Concluir diagnóstico e congelar preços")
            .WithDescription("Informa apenas os consumíveis; serviços e peças vêm do pedido original. Consulta preços do catálogo local/Inventory e persiste snapshot imutável. Quantidades usam a unidade do item e até três casas decimais; valores são centavos BRL. Sem consumíveis, enviar lista vazia. Cotação inválida/indisponível retorna 503 sem concluir parcialmente. Exige If-Match e Idempotency-Key.")
            .ProducesProblem(503);
        commands.MapPost("/diagnostic/fail", async (Guid id, FailDiagnosticRequest body, HttpContext http, OperationService service, CancellationToken ct) =>
            Reply(http, await service.FailDiagnostic(Context(http, id), body.Reason, ct)))
            .WithSummary("Registrar impossibilidade de diagnóstico")
            .WithDescription("Registra falha explícita com motivo obrigatório de até 2000 caracteres. A Saga decide as próximas ações a partir do evento; esta chamada não cancela orçamento/pagamento. Exige If-Match e Idempotency-Key.");
        commands.MapPost("/execution/start", async (Guid id, HttpContext http, OperationService service, CancellationToken ct) =>
            Reply(http, await service.StartExecution(Context(http, id), ct)))
            .WithSummary("Iniciar reparo físico")
            .WithDescription("Inicia o trabalho humano somente após autorização de execução já persistida por comando do coordenador. Não recebe PaymentConfirmed diretamente nem verifica bancos de outros serviços. Exige If-Match e Idempotency-Key; estado incompatível retorna 409.");
        commands.MapPost("/execution/jobs/{budgetJobId:guid}/done", async (Guid id, Guid budgetJobId, HttpContext http, OperationService service, CancellationToken ct) =>
            Reply(http, await service.ExecuteJob(Context(http, id), budgetJobId, ct)))
            .WithSummary("Marcar linha aprovada como executada")
            .WithDescription("Identifica a linha pelo BudgetJobId aprovado, não pelo ID do catálogo nem pelo DiagnosticLineId. Mantém o vínculo e registra ator/horário. Não baixa estoque. Exige If-Match e Idempotency-Key.");
        commands.MapPost("/execution/finish", async (Guid id, HttpContext http, OperationService service, CancellationToken ct) =>
            Reply(http, await service.FinalizeExecution(Context(http, id), ct)))
            .WithSummary("Concluir execução local")
            .WithDescription("Conclui o reparo somente quando todas as linhas aprovadas foram executadas. Publica o fato para o coordenador; não marca a OS global como entregue nem consome estoque diretamente. Exige If-Match e Idempotency-Key; linhas pendentes retornam 409.");
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
