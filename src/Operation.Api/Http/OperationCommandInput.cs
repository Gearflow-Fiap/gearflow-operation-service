using GearFlow.Operation.Application;

namespace GearFlow.Operation.Api;

/// <summary>Metadados apenas dos comandos HTTP de operação; catálogo não exige intenção.</summary>
public static class OperationCommandInput
{
    public static CommandContext Read(HttpContext http, Guid serviceOrderId)
    {
        var key = http.Request.Headers["Idempotency-Key"];
        if (key.Count != 1 || !Guid.TryParse(key[0], out var intent) || intent == Guid.Empty)
            throw new HttpInputException(400, "invalid_idempotency_key");
        var version = StrongETag.RequireIfMatch(http.Request);
        return new(intent, serviceOrderId, StaffActor.Read(http.User), version);
    }
}
