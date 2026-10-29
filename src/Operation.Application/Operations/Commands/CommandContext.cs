using GearFlow.Operation.Domain;

namespace GearFlow.Operation.Application;

/// <summary>Metadados internos. O host deve obter Actor de identidade confiável, não do body do cliente.</summary>
public sealed record CommandContext
{
    public Guid IntentId { get; }
    public Guid ServiceOrderId { get; }
    public string Actor { get; }
    public long? ExpectedVersion { get; }

    public CommandContext(Guid intentId, Guid serviceOrderId, string actor, long? expectedVersion = null)
    {
        if (intentId == Guid.Empty || serviceOrderId == Guid.Empty) throw new ArgumentException("Intenção e OS são obrigatórias.");
        if (string.IsNullOrWhiteSpace(actor)) throw new ArgumentException("Ator obrigatório.", nameof(actor));
        if (expectedVersion < 0) throw new ArgumentOutOfRangeException(nameof(expectedVersion));
        IntentId = intentId;
        ServiceOrderId = serviceOrderId;
        Actor = actor.Trim();
        ExpectedVersion = expectedVersion;
    }
}

public sealed record CommandResult(Guid ServiceOrderId, OperationStatus Status, long Version,
    Guid? DiagnosticId, Guid? BudgetId, bool StateChanged);

public sealed record CommandReceipt(Guid IntentId, string Fingerprint, CommandResult Result);

public sealed class IntentConflictException(string message) : Exception(message);
public sealed class OperationConcurrencyException(string message) : Exception(message);
public sealed class PricingUnavailableException(string message) : Exception(message);
public sealed class InvalidPriceQuoteException(string message) : Exception(message);
