using GearFlow.Operation.Domain;

namespace GearFlow.Operation.Application;

public sealed record PriceRequest(OperationItemType ItemType, Guid ItemId);
public sealed record CatalogPrice(OperationItemType ItemType, Guid ItemId, string Name, int UnitPriceCents);
public sealed record CatalogPriceQuote(DateTimeOffset PricedAt, string Currency, IReadOnlyList<CatalogPrice> Items);

/// <summary>Fonte autoritativa do backend; nunca preços livres do navegador.</summary>
public interface IPriceCatalog
{
    Task<CatalogPriceQuote> GetPrices(IReadOnlyList<PriceRequest> items, CancellationToken ct);
}

public enum ConsumablePricingMode { LegacyLinePriceV1, PerUnitV1 }

/// <summary>Política explícita, sem default: ausência bloqueia Price/PolicyId; integração deve acordar escolha com Budget.</summary>
public sealed class DiagnosticPricer
{
    private readonly IPriceCatalog catalog;
    private readonly ConsumablePricingMode? mode;
    private readonly TimeProvider clock;
    public string PolicyId => !mode.HasValue ? throw new PricingUnavailableException("Política financeira não configurada.") : mode == ConsumablePricingMode.LegacyLinePriceV1 ? "legacy-consumable-line-v1" : "per-unit-away-from-zero-v1";

    public DiagnosticPricer(IPriceCatalog catalog, ConsumablePricingMode? mode, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(clock);
        if (mode.HasValue && !Enum.IsDefined(mode.Value)) throw new ArgumentOutOfRangeException(nameof(mode));
        this.catalog = catalog;
        this.mode = mode;
        this.clock = clock;
    }

    public async Task<DiagnosticSnapshot> Price(WorkOperation operation, IReadOnlyList<DiagnosticConsumable> consumables, CancellationToken ct)
    {
        if (!mode.HasValue) throw new PricingUnavailableException("Política financeira não configurada.");
        if (!operation.IsActive || operation.Status != OperationStatus.InDiagnostic)
            throw new OperationConflictException("Operação deve estar ativa e em diagnóstico para consultar preços.");
        var inputs = operation.RequestedJobs.Select(job => new LineInput(job.LineId, OperationItemType.Labor, job.CatalogJobId, 1))
            .Concat(operation.RequestedParts.Select(part => new LineInput(part.LineId, OperationItemType.Part, part.PartId, part.Quantity)))
            .Concat(consumables.Select(item => new LineInput(item.LineId, OperationItemType.Consumable, item.ConsumableId, item.Quantity))).ToArray();
        if (inputs.Length > DiagnosticSnapshot.MaximumLines || inputs.Select(item => item.LineId).Distinct().Count() != inputs.Length)
            throw new ArgumentException("Limite de linhas excedido ou LineId repetido.", nameof(consumables));
        var requests = inputs.Select(item => new PriceRequest(item.Type, item.ItemId)).Distinct()
            .OrderBy(item => item.ItemType).ThenBy(item => item.ItemId).ToArray();
        var quote = await catalog.GetPrices(Array.AsReadOnly(requests), ct);
        if (quote is null || quote.Items is null || quote.Currency != "BRL" ||
            quote.PricedAt > clock.GetUtcNow() || quote.PricedAt < operation.DiagnosticStartedAt)
            throw new InvalidPriceQuoteException("Cotação inválida, moeda incompatível ou data fora da janela do diagnóstico.");
        var prices = new Dictionary<PriceRequest, CatalogPrice>();
        foreach (var price in quote.Items.ToArray())
        {
            if (price is null || !Enum.IsDefined(price.ItemType) || price.ItemId == Guid.Empty ||
                string.IsNullOrWhiteSpace(price.Name) || price.UnitPriceCents < 0 ||
                !prices.TryAdd(new(price.ItemType, price.ItemId), price))
                throw new InvalidPriceQuoteException("Cotação contém item inválido ou duplicado.");
        }
        if (prices.Count != requests.Length || requests.Any(item => !prices.ContainsKey(item)))
            throw new InvalidPriceQuoteException("Cotação deve conter exatamente todos os itens solicitados; preço ausente não é zero.");
        try
        {
            var lines = inputs.Select(item =>
            {
                var price = prices[new(item.Type, item.ItemId)];
                var quantity = item.Type == OperationItemType.Consumable && mode == ConsumablePricingMode.LegacyLinePriceV1 ? 1 : item.Quantity;
                var total = checked((int)decimal.Round(quantity * price.UnitPriceCents, 0, MidpointRounding.AwayFromZero));
                return new DiagnosticLine(item.LineId, item.Type, item.ItemId, price.Name, item.Quantity, price.UnitPriceCents, total);
            }).ToArray();
            return new(operation.DiagnosticId!.Value, lines, quote.PricedAt, PolicyId);
        }
        catch (OverflowException)
        {
            throw new InvalidPriceQuoteException("Cotação excede o limite monetário suportado.");
        }
    }

    private sealed record LineInput(Guid LineId, OperationItemType Type, Guid ItemId, decimal Quantity);
}
