namespace GearFlow.Operation.Api;

public sealed record FinishDiagnosticRequest(ConsumableInput[] Consumables);
public sealed record ConsumableInput(Guid LineId, Guid ConsumableId, decimal Quantity);
public sealed record FailDiagnosticRequest(string Reason);
