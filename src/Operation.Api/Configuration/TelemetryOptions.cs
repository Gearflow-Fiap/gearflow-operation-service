using Microsoft.Extensions.Options;

namespace GearFlow.Operation.Api;

public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";
    public bool ExportEnabled { get; set; }
    public bool BacklogEnabled { get; set; } = true;
    public bool AllowInsecureHttp { get; set; }
}

public sealed class OtlpOptions
{
    public string? Endpoint { get; set; }
    public string Protocol { get; set; } = "grpc";
    public string Headers { get; set; } = "";

    public static OtlpOptions Read(IConfiguration configuration) => new()
    {
        Endpoint = configuration["Otlp:Endpoint"] ?? configuration["OTEL_EXPORTER_OTLP_ENDPOINT"],
        Protocol = configuration["OTEL_EXPORTER_OTLP_PROTOCOL"] ?? "grpc",
        Headers = configuration["OTEL_EXPORTER_OTLP_HEADERS"] ?? ""
    };

    public Uri? Validate(TelemetryOptions telemetry)
    {
        if (!telemetry.ExportEnabled) return null;
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && (uri.IsLoopback || telemetry.AllowInsecureHttp))))
            throw new OptionsValidationException(Options.DefaultName, typeof(OtlpOptions), ["Endpoint OTLP válido obrigatório para exportação."]);
        if (Protocol is not ("grpc" or "http/protobuf"))
            throw new OptionsValidationException(Options.DefaultName, typeof(OtlpOptions), ["Protocolo OTLP inválido."]);
        return uri;
    }
}
