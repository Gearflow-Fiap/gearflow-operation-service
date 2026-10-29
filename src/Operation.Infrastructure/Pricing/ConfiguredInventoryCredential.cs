using GearFlow.Operation.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace GearFlow.Operation.Infrastructure.Pricing;

public sealed class ConfiguredInventoryCredential(IOptionsMonitor<InventoryOptions> options) : IInventoryCredential
{
    public string? GetBearerToken() => options.CurrentValue.BearerToken;
}
