using System.Diagnostics;
using Microsoft.AspNetCore.Http.Features;

namespace GearFlow.Operation.Api;

public sealed class OperationRequestLogging(RequestDelegate next, ILogger<OperationRequestLogging> logger)
{
    public async Task Invoke(HttpContext http)
    {
        var route = (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "unmatched";
        string? order = route.StartsWith("/api/operation/service-orders", StringComparison.Ordinal) && Guid.TryParse(http.Request.RouteValues["id"]?.ToString(), out var id) ? id.ToString("D") : null;
        var previous = Activity.Current;
        var requestActivity = previous ?? http.Features.Get<IHttpActivityFeature>()?.Activity;
        Activity? owned = null;
        if (requestActivity is null)
        {
            owned = new Activity("operation.http").SetIdFormat(ActivityIdFormat.W3C);
            if (ActivityContext.TryParse(http.TraceIdentifier, null, out var parent)) owned.SetParentId(parent.TraceId, parent.SpanId, parent.TraceFlags);
            owned.Start();
            requestActivity = owned;
        }
        Activity.Current = requestActivity;
        var trace = requestActivity.TraceId.ToString();
        http.Items["Operation.TraceId"] = trace;
        using var scope = logger.BeginScope(new Dictionary<string, object?> { ["TraceId"] = trace, ["CorrelationId"] = order, ["RequestId"] = http.TraceIdentifier });
        Activity.Current?.SetTag("gearflow.service_order.id", order);
        try
        {
            await next(http);
            if (!http.Request.Path.StartsWithSegments("/health")) logger.LogInformation("HTTP {Method} {Route} respondeu {StatusCode}", http.Request.Method, route, http.Response.StatusCode);
        }
        finally
        {
            owned?.Dispose();
            Activity.Current = previous;
        }
    }
}
