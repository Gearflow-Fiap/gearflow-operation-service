namespace GearFlow.Operation.Domain;

// LineId identifica a ocorrência solicitada. O mesmo item de catálogo pode aparecer em duas linhas.
public sealed record RequestedJob
{
    public Guid LineId { get; }
    public Guid CatalogJobId { get; }

    public RequestedJob(Guid lineId, Guid catalogJobId)
    {
        LineId = DomainGuard.Id(lineId, nameof(lineId));
        CatalogJobId = DomainGuard.Id(catalogJobId, nameof(catalogJobId));
    }
}

public sealed record RequestedPart
{
    public Guid LineId { get; }
    public Guid PartId { get; }
    public int Quantity { get; }

    public RequestedPart(Guid lineId, Guid partId, int quantity)
    {
        LineId = DomainGuard.Id(lineId, nameof(lineId));
        PartId = DomainGuard.Id(partId, nameof(partId));
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        Quantity = quantity;
    }
}

public sealed record DiagnosticConsumable
{
    public Guid LineId { get; }
    public Guid ConsumableId { get; }
    public decimal Quantity { get; }

    public DiagnosticConsumable(Guid lineId, Guid consumableId, decimal quantity)
    {
        LineId = DomainGuard.Id(lineId, nameof(lineId));
        ConsumableId = DomainGuard.Id(consumableId, nameof(consumableId));
        Quantity = DomainGuard.Quantity(quantity, nameof(quantity));
    }
}
