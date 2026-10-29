using System.Text.Json.Serialization;
using GearFlow.Operation.Infrastructure.Configuration;

namespace GearFlow.Operation.Api;

public static class OperationApiRegistration
{
    public static void AddOperationApi(this WebApplicationBuilder builder)
    {
        builder.Services.AddOperationSecurity(builder.Configuration);
        builder.AddOperationObservability();
        builder.Services.AddHealthChecks().AddCheck<OperationReadiness>("operation_database", timeout: TimeSpan.FromSeconds(3));
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["traceId"] = context.HttpContext.Items["Operation.TraceId"] as string
                ?? System.Diagnostics.Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier);
        builder.Services.AddExceptionHandler<OperationErrors>();
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            options.SerializerOptions.RespectRequiredConstructorParameters = true;
        });
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        builder.Services.AddOpenApi(options => options.AddDocumentTransformer<OperationOpenApi>());
        builder.Services.AddOptions<PricingOptions>().Bind(builder.Configuration.GetSection("Pricing"));
        builder.Services.AddSingleton<BacklogMetrics>();
        var telemetryOptions = builder.Configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>() ?? new();
        if (telemetryOptions.BacklogEnabled) builder.Services.AddHostedService<BacklogWorker>();
        var messaging = builder.Configuration.GetSection(MessagingOptions.SectionName).Get<MessagingOptions>() ?? new();
        if (messaging.Enabled && messaging.DispatchEnabled) builder.Services.AddHostedService<OutboxWorker>();
    }
}
