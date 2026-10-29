using System.Net;
using System.Text;
using GearFlow.Operation.Application;
using GearFlow.Operation.Domain;
using GearFlow.Operation.Infrastructure.Pricing;
using Xunit;

namespace Operation.UnitTests.Infrastructure;

internal sealed class TestInventoryCredential(string? token = "service-test-token") : IInventoryCredential
{
    public string? GetBearerToken() => token;
}
internal sealed class InventoryHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
}
public class InventoryPriceTests
{
    [Fact]
    public async Task HttpClientDeadlineCancelsAnUnresponsiveDependency()
    {
        using var http = new HttpClient(new InventoryHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        })) { BaseAddress = new Uri("http://localhost/"), Timeout = TimeSpan.FromMilliseconds(20) };
        await Assert.ThrowsAsync<PricingUnavailableException>(() => new InventoryPriceClient(http, new TestInventoryCredential()).Get(new(OperationItemType.Part, ItemId), default));
    }

    private static readonly Guid ItemId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static InventoryPriceClient Client(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new HttpClient(new InventoryHandler((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") })))
            { BaseAddress = new Uri("http://localhost/") }, new TestInventoryCredential());

    [Theory]
    [InlineData(OperationItemType.Part, "parts", "priceCents")]
    [InlineData(OperationItemType.Consumable, "consumables", "unitPriceCents")]
    public async Task ReadsExistingInventoryContractWithSeparateBearerCredential(OperationItemType type, string path, string field)
    {
        using var http = new HttpClient(new InventoryHandler((request, _) =>
        {
            Assert.Equal($"/api/inventory/{path}/{ItemId:D}", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme); Assert.Equal("service-test-token", request.Headers.Authorization.Parameter);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($$"""{"id":"{{ItemId}}","name":"Item","{{field}}":0,"quantity":4} """) });
        })) { BaseAddress = new Uri("http://localhost/") };
        var result = await new InventoryPriceClient(http, new TestInventoryCredential()).Get(new(type, ItemId), default);
        Assert.Equal(0, result.UnitPriceCents); Assert.Equal(type, result.ItemType); Assert.Equal(ItemId, result.ItemId);
    }

    [Theory]
    [InlineData(401)] [InlineData(403)] [InlineData(429)] [InlineData(500)] [InlineData(302)]
    public async Task DependencyFailureIsNotInventedPrice(int status) =>
        await Assert.ThrowsAsync<PricingUnavailableException>(() => Client("{}", (HttpStatusCode)status).Get(new(OperationItemType.Part, ItemId), default));

    [Fact]
    public async Task MissingItemIsRejected() => await Assert.ThrowsAsync<InvalidPriceQuoteException>(() => Client("{}", HttpStatusCode.NotFound).Get(new(OperationItemType.Part, ItemId), default));

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("not-json")]
    [InlineData("{\"id\":\"11111111-1111-4111-8111-111111111111\",\"name\":\"x\",\"priceCents\":-1}")]
    [InlineData("{\"id\":\"11111111-1111-4111-8111-111111111111\",\"name\":\"x\",\"priceCents\":1.5}")]
    [InlineData("{\"id\":\"11111111-1111-4111-8111-111111111111\",\"name\":\"x\",\"priceCents\":1,\"isActive\":false}")]
    [InlineData("{\"id\":\"22222222-2222-4222-8222-222222222222\",\"name\":\"x\",\"priceCents\":1}")]
    public async Task MalformedOrMismatchedPriceIsRejected(string json) =>
        await Assert.ThrowsAsync<InvalidPriceQuoteException>(() => Client(json).Get(new(OperationItemType.Part, ItemId), default));

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task NetworkAndTimeoutBecomePricingUnavailable(bool timeout)
    {
        using var http = new HttpClient(new InventoryHandler((_, _) => throw (timeout ? new TaskCanceledException() : new HttpRequestException()))) { BaseAddress = new Uri("http://localhost/") };
        await Assert.ThrowsAsync<PricingUnavailableException>(() => new InventoryPriceClient(http, new TestInventoryCredential()).Get(new(OperationItemType.Part, ItemId), default));
    }

    [Fact]
    public async Task CallerCancellationIsPropagated()
    {
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        using var http = new HttpClient(new InventoryHandler((_, ct) => Task.FromCanceled<HttpResponseMessage>(ct))) { BaseAddress = new Uri("http://localhost/") };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new InventoryPriceClient(http, new TestInventoryCredential()).Get(new(OperationItemType.Part, ItemId), canceled.Token));
    }
    [Fact]
    public async Task MissingConfigurationAndInvalidItemAreExplicit()
    {
        using var http = new HttpClient();
        await Assert.ThrowsAsync<PricingUnavailableException>(() => new InventoryPriceClient(http, new TestInventoryCredential()).Get(new(OperationItemType.Part, ItemId), default));
        http.BaseAddress = new Uri("http://localhost/");
        await Assert.ThrowsAsync<PricingUnavailableException>(() => new InventoryPriceClient(http, new TestInventoryCredential(null)).Get(new(OperationItemType.Part, ItemId), default));
        await Assert.ThrowsAsync<ArgumentException>(() => Client("{}").Get(new(OperationItemType.Labor, ItemId), default));
        await Assert.ThrowsAsync<ArgumentException>(() => Client("{}").Get(new(OperationItemType.Part, Guid.Empty), default));
    }

    [Theory]
    [InlineData("", "ok", 1)] [InlineData("a", "ok", -1)] [InlineData("a", null, 1)]
    public void InvalidJobDefinitionIsRejected(string name, string? description, int price) => Assert.Throws<ArgumentException>(() => new JobDefinition(name, description!, price));

    [Fact]
    public void JobDefinitionPreservesExplicitZeroPriceAndTrimsText()
    {
        var job = new JobDefinition("  Mão de obra ", " descrição ", 0);
        Assert.Equal("Mão de obra", job.Name); Assert.Equal("descrição", job.Description); Assert.Equal(0, job.PriceCents);
        Assert.Throws<ArgumentException>(() => new JobDefinition(new string('x', 129), "", 1));
        Assert.Throws<ArgumentException>(() => new JobDefinition("x", new string('x', 2001), 1));
    }
}


