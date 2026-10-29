using GearFlow.Operation.Application;

namespace GearFlow.Operation.Api;

public sealed class UnconfiguredPriceCatalog : IPriceCatalog
{
    public Task<CatalogPriceQuote> GetPrices(IReadOnlyList<PriceRequest> items, CancellationToken ct) =>
        throw new PricingUnavailableException("Catálogo/preços ainda não configurados.");
}
