using GearFlow.Operation.Application;

namespace GearFlow.Operation.Api;

public sealed class PricingOptions
{
    public const string SectionName = "Pricing";
    public string? ConsumableMode { get; set; }
    public ConsumablePricingMode? Mode => Enum.TryParse<ConsumablePricingMode>(ConsumableMode, out var parsed) && Enum.IsDefined(parsed) ? parsed : null;
}
