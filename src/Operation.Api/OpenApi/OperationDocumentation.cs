using Scalar.AspNetCore;

namespace GearFlow.Operation.Api;

public static class OperationDocumentation
{
    public static void MapOperationDocumentation(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            // The UI must load its schema before a user can enter a Bearer token.
            // Business endpoints keep their Staff policy in every environment.
            app.MapOpenApi().AllowAnonymous();
            app.MapScalarApiReference(options => options.WithTitle("GearFlow Operation API"))
                .AllowAnonymous();
        }
        else
        {
            app.MapOpenApi().RequireAuthorization("Staff");
        }
    }
}
