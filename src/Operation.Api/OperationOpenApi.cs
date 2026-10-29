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
            if (!path.StartsWith("/api/operation", StringComparison.Ordinal) || item.Operations is null) continue;
            foreach (var (method, operation) in item.Operations)
            {
                operation.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }];
                if (method != HttpMethod.Post) continue;
                operation.Parameters ??= [];
                operation.Parameters.Add(new OpenApiParameter { Name = "Idempotency-Key", In = ParameterLocation.Header, Required = true,
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" } });
                operation.Parameters.Add(new OpenApiParameter { Name = "If-Match", In = ParameterLocation.Header, Required = true,
                    Description = "ETag forte recebido no GET, por exemplo: \"3\". Replay exato devolve o resultado original.", Schema = new OpenApiSchema { Type = JsonSchemaType.String } });
            }
        }
        return Task.CompletedTask;
    }
}
