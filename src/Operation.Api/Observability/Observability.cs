using GearFlow.Operation.Application;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace GearFlow.Operation.Api;

public static class Observability
{
    public static void AddOperationObservability(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        builder.Logging.AddJsonConsole(options => { options.IncludeScopes = true; options.UseUtcTimestamp = true; options.TimestampFormat = "O"; });
        builder.Services.AddOptions<TelemetryOptions>().Bind(builder.Configuration.GetSection(TelemetryOptions.SectionName));
        var settings = builder.Configuration.GetSection(TelemetryOptions.SectionName).Get<TelemetryOptions>() ?? new();
        var otlp = OtlpOptions.Read(builder.Configuration);
        builder.Services.AddOptions<OtlpOptions>().Configure(options =>
        {
            options.Endpoint = otlp.Endpoint; options.Protocol = otlp.Protocol; options.Headers = otlp.Headers;
        });
        var export = settings.ExportEnabled;
        var target = otlp.Validate(settings);
        var protocol = otlp.Protocol;
        var telemetry = builder.Services.AddOpenTelemetry().ConfigureResource(resource => resource.AddService(OperationTelemetry.Name,
            serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString()).AddAttributes(new Dictionary<string, object> { ["deployment.environment.name"] = builder.Environment.EnvironmentName }));
        telemetry.WithTracing(traces =>
        {
            traces.AddAspNetCoreInstrumentation(options => options.Filter = context => context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/openapi"))
                .AddHttpClientInstrumentation().AddSource(OperationTelemetry.Name, "MassTransit");
            if (export) traces.AddOtlpExporter(options => Configure(options, "traces"));
        });
        telemetry.WithMetrics(metrics =>
        {
            metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation().AddMeter(OperationTelemetry.Name, "MassTransit");
            if (export) metrics.AddOtlpExporter((options, reader) =>
            {
                Configure(options, "metrics"); reader.TemporalityPreference = MetricReaderTemporalityPreference.Delta;
                reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 10000;
                reader.PeriodicExportingMetricReaderOptions.ExportTimeoutMilliseconds = 5000;
            });
        });
        builder.Logging.AddOpenTelemetry(options =>
        {
            options.IncludeScopes = true; options.IncludeFormattedMessage = true;
            if (export) options.AddOtlpExporter(exporter => Configure(exporter, "logs"));
        });
        void Configure(OtlpExporterOptions options, string signal)
        {
            options.Protocol = protocol == "http/protobuf" ? OtlpExportProtocol.HttpProtobuf : OtlpExportProtocol.Grpc;
            options.Endpoint = options.Protocol == OtlpExportProtocol.HttpProtobuf ? new Uri(target!.AbsoluteUri.TrimEnd('/') + "/v1/" + signal) : target!;
            options.TimeoutMilliseconds = 5000;
            options.Headers = otlp.Headers;
        }
    }
}
