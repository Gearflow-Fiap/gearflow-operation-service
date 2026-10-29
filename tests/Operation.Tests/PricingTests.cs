using GearFlow.Operation.Application;
using GearFlow.Operation.Domain;
using Xunit;
using static Operation.Tests.DomainFixture;

namespace Operation.Tests;

public class PricingTests
{
    [Fact]
    public async Task MissingPolicyBlocksPricingWithoutCallingCatalogOrChangingState()
    {
        var clock = new ApplicationClock();
        var catalog = new TestPriceCatalog(clock);
        var pricer = new DiagnosticPricer(catalog, null, clock);
        var operation = Diagnosing();
        await Assert.ThrowsAsync<PricingUnavailableException>(() => pricer.Price(operation, [], default));
        Assert.Throws<PricingUnavailableException>(() => pricer.PolicyId);
        Assert.Equal(0, catalog.Calls);
        Assert.Equal(OperationStatus.InDiagnostic, operation.Status);
        Assert.Equal(2, operation.Version);
    }

    [Theory]
    [InlineData(ConsumablePricingMode.LegacyLinePriceV1, 1800, "legacy-consumable-line-v1")]
    [InlineData(ConsumablePricingMode.PerUnitV1, 1900, "per-unit-away-from-zero-v1")]
    public async Task SelectedPolicyIsExplicitAndFrozenWithPriceSnapshot(ConsumablePricingMode mode, int expected, string policy)
    {
        var fixture = new ApplicationFixture(mode); await fixture.Diagnosing();
        await fixture.Service.CompleteDiagnostic(fixture.Context(), [Consumable()]);
        var operation = fixture.Store.Peek(Id(1))!;
        Assert.Equal(expected, operation.Diagnostic!.TotalPriceCents);
        Assert.Equal(policy, operation.Diagnostic.PricingPolicyId);
        Assert.Equal(1.5m, operation.Consumables[0].Quantity);
        Assert.Equal(3, fixture.Prices.LastRequest!.Count);
        Assert.Throws<NotSupportedException>(() => ((IList<PriceRequest>)fixture.Prices.LastRequest).Clear());
    }

    [Fact]
    public async Task PricesAreRequestedOncePerCatalogItemWhileOccurrencesKeepIndependentLines()
    {
        var fixture = new ApplicationFixture(); await fixture.Diagnosing(2);
        await fixture.Service.CompleteDiagnostic(fixture.Context(), [Consumable()]);
        Assert.Equal(3, fixture.Prices.LastRequest!.Count);
        var snapshot = fixture.Store.Peek(Id(1))!.Diagnostic!;
        Assert.Equal(4, snapshot.Lines.Count); Assert.Equal(4, snapshot.Lines.Select(line => line.LineId).Distinct().Count());
    }

    [Theory]
    [InlineData("missing")] [InlineData("extra")] [InlineData("duplicate")]
    [InlineData("wrong-type")] [InlineData("empty-id")] [InlineData("wrong-id")]
    [InlineData("negative")] [InlineData("name")] [InlineData("currency")]
    [InlineData("future")] [InlineData("stale")] [InlineData("null-quote")]
    [InlineData("null-list")] [InlineData("null-item")]
    public async Task InvalidOrIncompleteQuoteDoesNotSavePartialDiagnostic(string error)
    {
        var fixture = new ApplicationFixture(); await fixture.Diagnosing();
        fixture.Prices.Quote = requests =>
        {
            var quote = fixture.Prices.DefaultQuote(requests); var items = quote.Items.ToList();
            switch (error)
            {
                case "missing": items.RemoveAt(0); break;
                case "extra": items.Add(new(OperationItemType.Part, Id(999), "Extra", 1)); break;
                case "duplicate": items.Add(items[0]); break;
                case "wrong-type": items[0] = items[0] with { ItemType = (OperationItemType)99 }; break;
                case "empty-id": items[0] = items[0] with { ItemId = Guid.Empty }; break;
                case "wrong-id": items[0] = items[0] with { ItemId = Id(999) }; break;
                case "negative": items[0] = items[0] with { UnitPriceCents = -1 }; break;
                case "name": items[0] = items[0] with { Name = " " }; break;
                case "currency": return quote with { Currency = "USD" };
                case "future": return quote with { PricedAt = fixture.Clock.Now.AddMinutes(1) };
                case "stale": return quote with { PricedAt = Time.AddMinutes(-1) };
                case "null-quote": return null!;
                case "null-list": return quote with { Items = null! };
                case "null-item": items[0] = null!; break;
            }
            return quote with { Items = items };
        };
        var context = fixture.Context();
        await Assert.ThrowsAsync<InvalidPriceQuoteException>(() => fixture.Service.CompleteDiagnostic(context, [Consumable()]));
        var operation = fixture.Store.Peek(Id(1))!;
        Assert.Equal(OperationStatus.InDiagnostic, operation.Status); Assert.Null(operation.Diagnostic);
        Assert.Empty(operation.Consumables); Assert.Equal(2, operation.Version);
        Assert.Null(await fixture.Store.FindReceipt(context.IntentId, default));
        Assert.Equal(2, fixture.Store.Events.Count);
    }

    [Fact]
    public async Task TransientPricingFailureCanRetrySameIntentAndExplicitZeroIsAccepted()
    {
        var fixture = new ApplicationFixture(); await fixture.Diagnosing(); var context = fixture.Context();
        fixture.Prices.Failure = new PricingUnavailableException("catalog down");
        await Assert.ThrowsAsync<PricingUnavailableException>(() => fixture.Service.CompleteDiagnostic(context, []));
        Assert.Null(await fixture.Store.FindReceipt(context.IntentId, default));
        fixture.Prices.Failure = null;
        fixture.Prices.Quote = requests => fixture.Prices.DefaultQuote(requests) with
        { Items = requests.Select(item => new CatalogPrice(item.ItemType, item.ItemId, "Grátis", 0)).ToArray() };
        await fixture.Service.CompleteDiagnostic(context, []);
        Assert.Equal(0, fixture.Store.Peek(Id(1))!.Diagnostic!.TotalPriceCents); Assert.Equal(2, fixture.Prices.Calls);
    }

    [Fact]
    public async Task PerUnitPolicyRoundsHalfAwayFromZeroInCents()
    {
        var fixture = new ApplicationFixture(); await fixture.Diagnosing();
        fixture.Prices.Quote = requests => new(fixture.Clock.Now, "BRL", requests.Select(item =>
            new CatalogPrice(item.ItemType, item.ItemId, "Item", 1)).ToArray());
        var consumable = new DiagnosticConsumable(Id(30), Id(300), 2.5m);
        await fixture.Service.CompleteDiagnostic(fixture.Context(), [consumable]);
        Assert.Equal(3, fixture.Store.Peek(Id(1))!.Diagnostic!.Lines.Single(line => line.ItemType == OperationItemType.Consumable).LineTotalCents);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task MoneyOverflowIsExplicitAndLeavesDiagnosticOpen(bool overflowSingleLine)
    {
        var fixture = new ApplicationFixture(); await fixture.Diagnosing();
        fixture.Prices.Quote = requests => new(fixture.Clock.Now, "BRL", requests.Select(item => new CatalogPrice(item.ItemType,
            item.ItemId, "Item", overflowSingleLine ? int.MaxValue : item.ItemType == OperationItemType.Labor ? int.MaxValue : 1)).ToArray());
        await Assert.ThrowsAsync<InvalidPriceQuoteException>(() => fixture.Service.CompleteDiagnostic(fixture.Context(), []));
        Assert.Null(fixture.Store.Peek(Id(1))!.Diagnostic); Assert.Equal(2, fixture.Store.Events.Count);
    }

    [Fact]
    public async Task InvalidConsumableLinesFailBeforeCatalogCall()
    {
        var fixture = new ApplicationFixture(); await fixture.Diagnosing();
        var collision = new DiagnosticConsumable(Id(10), Id(300), 1);
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CompleteDiagnostic(fixture.Context(), [collision]));
        var tooMany = Enumerable.Range(1, 201).Select(i => new DiagnosticConsumable(Id(1000 + i), Id(300), 1));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.CompleteDiagnostic(fixture.Context(), tooMany));
        await Assert.ThrowsAsync<ArgumentNullException>(() => fixture.Service.CompleteDiagnostic(fixture.Context(), null!));
        Assert.Equal(0, fixture.Prices.Calls);
    }

    [Fact]
    public async Task NumericScaleAndCultureDoNotChangeMeaningOfAnIntent()
    {
        var fixture = new ApplicationFixture(); await fixture.Diagnosing(); var context = fixture.Context();
        var result = await fixture.Service.CompleteDiagnostic(context, [new(Id(30), Id(300), 1.5m)]);
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
            Assert.Equal(result, await fixture.Service.CompleteDiagnostic(context, [new(Id(30), Id(300), 1.500m)]));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
        Assert.Equal(1, fixture.Prices.Calls);
    }

    [Fact]
    public void PolicyMustBeDefinedAndDependenciesPresent()
    {
        var clock = new ApplicationClock(); var prices = new TestPriceCatalog(clock);
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiagnosticPricer(prices, (ConsumablePricingMode)99, clock));
        Assert.Throws<ArgumentNullException>(() => new DiagnosticPricer(null!, ConsumablePricingMode.PerUnitV1, clock));
        Assert.Throws<ArgumentNullException>(() => new DiagnosticPricer(prices, ConsumablePricingMode.PerUnitV1, null!));
    }
}
