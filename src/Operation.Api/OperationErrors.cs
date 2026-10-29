using GearFlow.Operation.Application;
using GearFlow.Operation.Domain;
using Microsoft.AspNetCore.Diagnostics;

namespace GearFlow.Operation.Api;

public sealed class HttpInputException(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public sealed class OperationErrors : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var (status, code) = exception switch
        {
            HttpInputException input => (input.Status, input.Code),
            BadHttpRequestException => (400, "invalid_request"),
            KeyNotFoundException => (404, "operation_not_found"),
            OperationConcurrencyException => (412, "version_conflict"),
            IntentConflictException => (409, "intent_conflict"),
            OperationConflictException => (409, "state_conflict"),
            PricingUnavailableException or InvalidPriceQuoteException => (503, "pricing_unavailable"),
            ArgumentException => (400, "invalid_request"),
            _ => (500, "internal_error")
        };
        await Write(context, status, code);
        return true;
    }

    public static Task Write(HttpContext context, int status, string code) => Results.Problem(
        statusCode: status, title: code, extensions: new Dictionary<string, object?> { ["code"] = code, ["traceId"] = context.TraceIdentifier })
        .ExecuteAsync(context);
}
