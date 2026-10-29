using GearFlow.Operation.Infrastructure.Pricing;

namespace GearFlow.Operation.Api;

public sealed class ConfiguredInventoryCredential(IConfiguration configuration) : IInventoryCredential
{
    public string? GetBearerToken() => configuration["Inventory:BearerToken"];
}
