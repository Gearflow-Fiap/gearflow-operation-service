using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;

namespace GearFlow.Operation.Api;

public sealed class OperationOpenApi : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info.Title = "GearFlow Operation API";
        document.Info.Description = "Diagnóstico, catálogo de mão de obra e execução física. Requer JWT Staff nas APIs de negócio. Autorizações comerciais e cancelamentos da Saga entram por mensageria, não por estas rotas.";
        document.Components ??= new();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" };
        foreach (var (path, item) in document.Paths)
        {
            var operational = path.StartsWith("/api/operation", StringComparison.Ordinal);
            if ((!operational && !path.StartsWith("/api/catalog/jobs", StringComparison.Ordinal)) || item.Operations is null) continue;
            foreach (var (method, operation) in item.Operations)
            {
                operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }];
                var command = operational && method == HttpMethod.Post;
                if (command || method == HttpMethod.Put || method == HttpMethod.Delete)
                {
                    operation.Parameters ??= [];
                    if (command) operation.Parameters.Add(new OpenApiParameter { Name = "Idempotency-Key", In = ParameterLocation.Header, Required = true,
                        Description = "GUID não vazio da intenção. Reenviar o mesmo ID e conteúdo para replay; novo conteúdo exige nova intenção. Reuso divergente retorna 409.",
                        Example = JsonValue.Create("11111111-1111-4111-8111-111111111111"), Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" } });
                    operation.Parameters.Add(new OpenApiParameter { Name = "If-Match", In = ParameterLocation.Header, Required = true,
                        Description = "ETag forte recebido no GET, por exemplo: \"3\". Ausente: 428; formato inválido: 400; versão divergente: 412. Replay exato devolve resultado e ETag originais.",
                        Example = JsonValue.Create("\"3\""), Schema = new OpenApiSchema { Type = JsonSchemaType.String } });
                }
                DescribeParameters(operation, operational);
                DescribeBody(path, operation);
                DescribeResponses(path, method, operation, command);
            }
        }
        return Task.CompletedTask;
    }

    private static void DescribeParameters(OpenApiOperation operation, bool operational)
    {
        if (operation.Parameters is null) return;
        foreach (var parameter in operation.Parameters.OfType<OpenApiParameter>())
        {
            switch (parameter.Name)
            {
                case "id":
                    parameter.Description = operational ? "ServiceOrderId da OS recebida pelo Operation; não é ID de orçamento ou de item do catálogo." : "ID do serviço de mão de obra no catálogo Operation.";
                    parameter.Example = JsonValue.Create("22222222-2222-4222-8222-222222222222");
                    break;
                case "budgetJobId":
                    parameter.Description = "Identidade da linha aprovada no Budget. Não usar CatalogJobId nem DiagnosticLineId.";
                    parameter.Example = JsonValue.Create("33333333-3333-4333-8333-333333333333");
                    break;
                case "page":
                    parameter.Description = "Página padrão 1; valores menores são normalizados para 1.";
                    parameter.Example = JsonValue.Create(1);
                    break;
                case "pageSize":
                    parameter.Description = "Tamanho padrão 20; valores são normalizados para o intervalo 1–100.";
                    parameter.Example = JsonValue.Create(20);
                    break;
                case "includeInactive":
                    parameter.Description = "Padrão false. Quando true, inclui registros desativados na consulta.";
                    parameter.Example = JsonValue.Create(false);
                    break;
                case "status":
                    parameter.Description = "Filtro opcional pelo estado local da operação. Estado global da OS pertence ao coordenador.";
                    break;
            }
        }
    }

    private static void DescribeBody(string path, OpenApiOperation operation)
    {
        if (operation.RequestBody?.Content is not { } content || !content.TryGetValue("application/json", out var media)) return;
        if (path.EndsWith("/diagnostic/finish", StringComparison.Ordinal))
            media.Example = JsonNode.Parse("""{"consumables":[]}""");
        else if (path.EndsWith("/diagnostic/fail", StringComparison.Ordinal))
            media.Example = JsonNode.Parse("""{"reason":"Diagnóstico inviável após inspeção técnica."}""");
        else if (path.StartsWith("/api/catalog/jobs", StringComparison.Ordinal))
            media.Example = JsonNode.Parse("""{"name":"Troca de óleo","description":"Mão de obra; peças e insumos são cotados separadamente.","priceCents":12000}""");
    }

    private static void DescribeResponses(string path, HttpMethod method, OpenApiOperation operation, bool command)
    {
        if (operation.Responses is null) return;
        foreach (var (status, value) in operation.Responses)
        {
            if (value is not OpenApiResponse response) continue;
            var error = status switch
            {
                "400" => ("Entrada, parâmetro ou header inválido.", "invalid_request"),
                "401" => ("JWT ausente, inválido ou expirado.", "http_error"),
                "403" => ("Identidade sem permissão Staff válida.", "http_error"),
                "404" => ("Recurso não encontrado neste serviço.", "operation_not_found"),
                "409" => ("Estado incompatível ou intenção reutilizada com conteúdo divergente.", "state_conflict"),
                "412" => ("Versão do If-Match diverge da versão persistida.", "version_conflict"),
                "428" => ("Header If-Match obrigatório ausente.", "if_match_required"),
                "503" => ("Cotação indisponível/inválida; diagnóstico não é concluído parcialmente.", "pricing_unavailable"),
                "500" => ("Falha técnica interna. Detalhes sensíveis não são retornados.", "internal_error"),
                _ => default
            };
            if (error.Item1 is not null)
            {
                response.Description = error.Item1;
                if (response.Content?.TryGetValue("application/problem+json", out var problem) == true)
                    problem.Example = new JsonObject { ["title"] = error.Item2, ["status"] = int.Parse(status, System.Globalization.CultureInfo.InvariantCulture),
                        ["code"] = error.Item2, ["traceId"] = "11111111111111111111111111111111", ["requestId"] = "exemplo-local" };
                continue;
            }
            var singleRead = method == HttpMethod.Get && (path == "/api/operation/service-orders/{id}" || path == "/api/catalog/jobs/{id}");
            var catalogWrite = path.StartsWith("/api/catalog/jobs", StringComparison.Ordinal) && method != HttpMethod.Get;
            if (status is not ("200" or "201")) continue;
            response.Description = status == "201" ? "Cadastro criado; Location indica o recurso e ETag a versão." : "Consulta ou comando concluído com sucesso.";
            if (command || singleRead || catalogWrite)
            {
                response.Headers ??= new Dictionary<string, IOpenApiHeader>();
                response.Headers["ETag"] = new OpenApiHeader { Description = "Versão forte do resultado, por exemplo: \"3\". Usar no próximo If-Match; replay de comando mantém a versão original.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String, Example = JsonValue.Create("\"3\"") } };
                response.Headers["Cache-Control"] = new OpenApiHeader { Description = "Resposta versionada não deve ser armazenada em cache.", Schema = new OpenApiSchema { Type = JsonSchemaType.String, Example = JsonValue.Create("no-store") } };
            }
            if (status == "201")
                response.Headers!["Location"] = new OpenApiHeader { Description = "URI relativa do cadastro criado.", Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uri-reference" } };
        }
    }
}
