using GearFlow.Operation.Application;

namespace GearFlow.Operation.Api;

public sealed record JobRequest(string Name, string Description, int PriceCents)
{
    // Usa a mesma validação/normalização da aplicação, sem duplicar limites financeiros na API.
    public JobDefinition ToDefinition() => new(Name, Description, PriceCents);
}
