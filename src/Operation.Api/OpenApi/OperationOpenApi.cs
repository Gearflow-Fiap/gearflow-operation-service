using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace GearFlow.Operation.Api;

public sealed class OperationOpenApi : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
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
                if (!(operational && method == HttpMethod.Post) && !(method == HttpMethod.Put || method == HttpMethod.Delete)) continue;
                operation.Parameters ??= [];
                if (operational) operation.Parameters.Add(new OpenApiParameter { Name = "Idempotency-Key", In = ParameterLocation.Header, Required = true,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" } });
                operation.Parameters.Add(new OpenApiParameter { Name = "If-Match", In = ParameterLocation.Header, Required = true,
                    Description = "ETag forte recebido no GET, por exemplo: \"3\". Replay exato devolve o resultado original.", Schema = new OpenApiSchema { Type = JsonSchemaType.String } });
            }
        }
        return Task.CompletedTask;
    }
}
