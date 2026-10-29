namespace GearFlow.Operation.Domain;

public sealed record DiagnosticLine
{
    public Guid LineId { get; }
    public OperationItemType ItemType { get; }
    public Guid ItemId { get; }
    public string Name { get; }
    public decimal Quantity { get; }
    public int UnitPriceCents { get; }
    public int LineTotalCents { get; }

    public DiagnosticLine(Guid lineId, OperationItemType itemType, Guid itemId, string name,
        decimal quantity, int unitPriceCents, int lineTotalCents)
    {
        LineId = DomainGuard.Id(lineId, nameof(lineId));
        ItemId = DomainGuard.Id(itemId, nameof(itemId));
        if (!Enum.IsDefined(itemType)) throw new ArgumentOutOfRangeException(nameof(itemType));
        ItemType = itemType;
        Name = DomainGuard.Text(name, nameof(name));
        Quantity = DomainGuard.Quantity(quantity, nameof(quantity));
        if (itemType == OperationItemType.Labor && quantity != 1 ||
            itemType == OperationItemType.Part && quantity != decimal.Truncate(quantity))
            throw new ArgumentException("Mão de obra exige uma unidade; peças exigem quantidade inteira.", nameof(quantity));
        if (unitPriceCents < 0) throw new ArgumentOutOfRangeException(nameof(unitPriceCents));
        if (lineTotalCents < 0) throw new ArgumentOutOfRangeException(nameof(lineTotalCents));
        UnitPriceCents = unitPriceCents;
        LineTotalCents = lineTotalCents;
    }
}

/// <summary>
/// Valores autoritativos recebidos de uma política de precificação identificada.
/// Não decide implicitamente a fórmula de insumos do legado versus preço por unidade.
/// A aplicação deverá resolver/validar a política antes de produzir o snapshot.
/// </summary>
public sealed class DiagnosticSnapshot
{
    public const int MaximumLines = 200;
    public Guid DiagnosticId { get; }
    public IReadOnlyList<DiagnosticLine> Lines { get; }
    public DateTimeOffset PricedAt { get; }
    public string PricingPolicyId { get; }
    public string Currency => "BRL";
    public int TotalPriceCents { get; }

    public DiagnosticSnapshot(Guid diagnosticId, IEnumerable<DiagnosticLine> lines,
        DateTimeOffset pricedAt, string pricingPolicyId)
    {
        DiagnosticId = DomainGuard.Id(diagnosticId, nameof(diagnosticId));
        PricingPolicyId = DomainGuard.Text(pricingPolicyId, nameof(pricingPolicyId));
        var copy = DomainGuard.CopyItems(lines, nameof(lines));
        if (copy.Length is 0 or > MaximumLines) throw new ArgumentException("Snapshot deve ter entre 1 e 200 linhas.", nameof(lines));
        if (copy.Select(line => line.LineId).Distinct().Count() != copy.Length)
            throw new ArgumentException("LineId deve ser único no snapshot.", nameof(lines));
        TotalPriceCents = copy.Sum(line => line.LineTotalCents); // Sum<int> verifica overflow.
        Lines = Array.AsReadOnly(copy);
        PricedAt = pricedAt.ToUniversalTime();
    }

    internal bool HasSameContent(DiagnosticSnapshot other) =>
        DiagnosticId == other.DiagnosticId && PricedAt == other.PricedAt &&
        PricingPolicyId == other.PricingPolicyId && TotalPriceCents == other.TotalPriceCents &&
        Lines.Count == other.Lines.Count &&
        Lines.OrderBy(line => line.LineId).SequenceEqual(other.Lines.OrderBy(line => line.LineId));
}
