using GearFlow.Operation.Domain;

namespace GearFlow.Operation.Api;

public sealed record FinishDiagnosticRequest(ConsumableInput[] Consumables)
{
    public DiagnosticConsumable[] ToConsumables()
    {
        if (Consumables is null || Consumables.Length > DiagnosticSnapshot.MaximumLines || Consumables.Any(item => item is null))
            throw new HttpInputException(400, "invalid_consumables");
        // Identidades e quantidade continuam sendo validadas pelos value objects do domínio.
        return Consumables.Select(item => new DiagnosticConsumable(item.LineId, item.ConsumableId, item.Quantity)).ToArray();
    }
}
public sealed record ConsumableInput(Guid LineId, Guid ConsumableId, decimal Quantity);
public sealed record FailDiagnosticRequest(string Reason);
