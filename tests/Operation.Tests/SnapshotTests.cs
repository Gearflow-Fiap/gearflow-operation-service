using GearFlow.Operation.Domain;
using Xunit;
using static Operation.Tests.DomainFixture;

namespace Operation.Tests;

public class SnapshotTests
{
    [Fact]
    public void SnapshotIsDefensiveAndKeepsAuthoritativeAmountsWithoutInventingConsumableFormula()
    {
        var operation = Diagnosing();
        var lines = Snapshot(operation, [Consumable()]).Lines.ToList();
        var snapshot = new DiagnosticSnapshot(Id(3), lines, At(2).ToOffset(TimeSpan.FromHours(-3)), " supplied-legacy ");
        lines.Clear();
        Assert.Equal(3, snapshot.Lines.Count);
        Assert.Equal(1800, snapshot.TotalPriceCents); // 1000 + 600 + 200 (not implicitly 1.5*200).
        var consumable = snapshot.Lines.Single(line => line.ItemType == OperationItemType.Consumable);
        Assert.Equal(200, consumable.UnitPriceCents);
        Assert.Equal(200, consumable.LineTotalCents);
        Assert.Equal("supplied-legacy", snapshot.PricingPolicyId); Assert.Equal("BRL", snapshot.Currency);
        Assert.Equal(TimeSpan.Zero, snapshot.PricedAt.Offset);
        Assert.Throws<NotSupportedException>(() => ((IList<DiagnosticLine>)snapshot.Lines).Clear());
    }

    [Theory]
    [InlineData(OperationItemType.Labor, "2")]
    [InlineData(OperationItemType.Part, "1.5")]
    [InlineData(OperationItemType.Consumable, "0")]
    [InlineData(OperationItemType.Consumable, "-1")]
    [InlineData(OperationItemType.Consumable, "0.0001")]
    public void InvalidItemQuantitiesAreRejected(OperationItemType type, string input)
    {
        var quantity = decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        Assert.ThrowsAny<ArgumentException>(() => new DiagnosticLine(Id(1), type, Id(2), "Item", quantity, 1, 1));
    }

    [Theory]
    [InlineData("0")] [InlineData("-1")] [InlineData("0.0001")]
    public void ConsumableInputAlsoEnforcesPrecisionAndPositiveQuantity(string input) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiagnosticConsumable(Id(1), Id(2),
            decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void FractionalConsumableAndExplicitZeroPriceAreValid()
    {
        var item = new DiagnosticConsumable(Id(1), Id(2), 0.001m);
        var line = new DiagnosticLine(item.LineId, OperationItemType.Consumable, item.ConsumableId, "Grátis", item.Quantity, 0, 0);
        Assert.Equal(0, new DiagnosticSnapshot(Id(3), [line], Time, "explicit-zero").TotalPriceCents);
    }

    [Fact]
    public void LineInputRequiresValidIdentityTypeNameAndNonNegativePrices()
    {
        Assert.Throws<ArgumentException>(() => new RequestedJob(Guid.Empty, Id(1)));
        Assert.Throws<ArgumentException>(() => new RequestedJob(Id(1), Guid.Empty));
        Assert.Throws<ArgumentException>(() => new RequestedPart(Guid.Empty, Id(1), 1));
        Assert.Throws<ArgumentException>(() => new RequestedPart(Id(1), Guid.Empty, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RequestedPart(Id(1), Id(2), 0));
        Assert.Throws<ArgumentException>(() => new DiagnosticConsumable(Guid.Empty, Id(1), 1));
        Assert.Throws<ArgumentException>(() => new DiagnosticConsumable(Id(1), Guid.Empty, 1));
        Assert.Throws<ArgumentException>(() => new DiagnosticLine(Guid.Empty, OperationItemType.Part, Id(2), "Item", 1, 1, 1));
        Assert.Throws<ArgumentException>(() => new DiagnosticLine(Id(1), OperationItemType.Part, Guid.Empty, "Item", 1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiagnosticLine(Id(1), (OperationItemType)99, Id(2), "Item", 1, 1, 1));
        Assert.Throws<ArgumentException>(() => new DiagnosticLine(Id(1), OperationItemType.Labor, Id(2), " ", 1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiagnosticLine(Id(1), OperationItemType.Labor, Id(2), "Item", 1, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiagnosticLine(Id(1), OperationItemType.Labor, Id(2), "Item", 1, 1, -1));
    }

    [Fact]
    public void SnapshotRequiresIdentityPolicyUniqueNonNullLinesAndBoundedSize()
    {
        var line = new DiagnosticLine(Id(1), OperationItemType.Labor, Id(2), "Item", 1, 1, 1);
        Assert.Throws<ArgumentException>(() => new DiagnosticSnapshot(Guid.Empty, [line], Time, "p"));
        Assert.Throws<ArgumentException>(() => new DiagnosticSnapshot(Id(3), [line], Time, " "));
        Assert.Throws<ArgumentException>(() => new DiagnosticSnapshot(Id(3), [], Time, "p"));
        Assert.Throws<ArgumentException>(() => new DiagnosticSnapshot(Id(3), [line, line], Time, "p"));
        Assert.Throws<ArgumentNullException>(() => new DiagnosticSnapshot(Id(3), null!, Time, "p"));
        Assert.Throws<ArgumentException>(() => new DiagnosticSnapshot(Id(3), [null!], Time, "p"));
        var lines = Enumerable.Range(1, 201).Select(i => new DiagnosticLine(Id(i), OperationItemType.Labor, Id(999), "Item", 1, 1, 1));
        Assert.Throws<ArgumentException>(() => new DiagnosticSnapshot(Id(3), lines, Time, "p"));
    }

    [Fact]
    public void SnapshotRejectsOverflowInsteadOfWrappingTotal()
    {
        var lines = new[] { new DiagnosticLine(Id(1), OperationItemType.Labor, Id(8), "A", 1, int.MaxValue, int.MaxValue),
            new DiagnosticLine(Id(2), OperationItemType.Labor, Id(8), "B", 1, 1, 1) };
        Assert.Throws<OverflowException>(() => new DiagnosticSnapshot(Id(3), lines, Time, "p"));
    }
}
