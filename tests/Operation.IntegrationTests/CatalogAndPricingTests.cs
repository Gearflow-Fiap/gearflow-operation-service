using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using GearFlow.Operation.Application;
using GearFlow.Operation.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Operation.IntegrationTests;

internal sealed class InventoryStub : IAsyncDisposable
{
    private readonly WebApplication app;
    public string Address { get; }
    public int Calls { get; private set; }
    public int Status { get; set; } = 200;
    public const string Token = "inventory_test_service_credential";
    private InventoryStub(WebApplication app, string address) { this.app = app; Address = address; }
    public static async Task<InventoryStub> Start()
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        var app = builder.Build(); InventoryStub? stub = null;
        app.MapGet("/api/inventory/{type}/{id:guid}", (string type, Guid id, Microsoft.AspNetCore.Http.HttpContext http) =>
        {
            stub!.Calls++;
            if (http.Request.Headers.Authorization != "Bearer " + Token) return Microsoft.AspNetCore.Http.Results.Unauthorized();
            if (stub.Status != 200) return Microsoft.AspNetCore.Http.Results.StatusCode(stub.Status);
            return type == "parts" ? Microsoft.AspNetCore.Http.Results.Json(new { id, name = "Peça", priceCents = 300, quantity = 10 }) :
                Microsoft.AspNetCore.Http.Results.Json(new { id, name = "Insumo", unitPriceCents = 200, quantity = 5m });
        });
        await app.StartAsync();
        stub = new InventoryStub(app, app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        return stub;
    }
    public async ValueTask DisposeAsync() { await app.StopAsync(); await app.DisposeAsync(); }
}

[Collection("Postgres")]
public class CatalogAndPricingTests(PostgresFixture postgres)
{
    [Fact]
    public async Task CatalogMigrationUpgradesExistingOperationWithoutChangingItsHistory()
    {
        var h = await postgres.Create(migrate: false);
        await using var db = h.Factory.CreateDbContext();
        await db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync(db.Database.GetMigrations().First());
        await h.Receive();
        await db.Database.MigrateAsync();
        Assert.Equal(1, (await h.Queries.Get(h.OrderId)).Version);
        Assert.Equal((1, 1, 1, 1), await h.Counts());
        Assert.NotNull(await new PostgresJobCatalog(h.Factory, h.Clock).Create(new("Migrado", "", 1), "staff:test", default));
    }

    [Fact]
    public async Task LaborOnlyDiagnosticDoesNotNeedInventoryCredential()
    {
        var h = await postgres.Create(); var catalog = new PostgresJobCatalog(h.Factory, h.Clock);
        var labor = await catalog.Create(new("Mão de obra", "", 1000), "staff:test", default);
        await h.Service().Receive(h.Context(), h.VehicleId, [new(h.Job.LineId, labor.Id)], []);
        h.Clock.Advance(); await h.Service().StartDiagnostic(h.Context()); h.Clock.Advance();
        await using var baseApp = new ApiFactory(h, prices: false);
        await using var app = baseApp.WithWebHostBuilder(builder => builder.UseSetting("Pricing:ConsumableMode", "PerUnitV1"));
        using var client = app.CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.Token());
        using var request = Change(HttpMethod.Post, $"/api/operation/service-orders/{h.OrderId}/diagnostic/finish", 2, new { consumables = Array.Empty<object>() });
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(request)).StatusCode);
        Assert.Equal(1000, (await h.Queries.Get(h.OrderId)).Diagnostic!.TotalPriceCents);
    }

    private static HttpRequestMessage Change(HttpMethod method, string path, long? version, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (version.HasValue) request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    [Fact]
    public async Task CatalogCrudIsPersistentVersionedAndStaffProtected()
    {
        var h = await postgres.Create(); await using var app = new ApiFactory(h); using var client = app.Staff();
        var create = await client.PostAsJsonAsync("/api/catalog/jobs/", new { name = "Troca óleo", description = "Serviço", priceCents = 1000 });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode); Assert.Equal("\"1\"", create.Headers.ETag!.Tag);
        var job = (await create.Content.ReadFromJsonAsync<CatalogJob>())!; var path = $"/api/catalog/jobs/{job.Id}";
        Assert.Equal(path, create.Headers.Location!.OriginalString);
        var schema = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/openapi/v1.json");
        var post = schema.GetProperty("paths").EnumerateObject().Single(entry => entry.Name.TrimEnd('/') == "/api/catalog/jobs").Value.GetProperty("post");
        Assert.True(post.TryGetProperty("security", out _));
        Assert.True(post.GetProperty("responses").GetProperty("201").TryGetProperty("content", out _));
        Assert.Equal(1000, (await client.GetFromJsonAsync<CatalogJob>(path))!.PriceCents);
        var body = new { name = "Troca óleo", description = "Novo", priceCents = 1200 };
        Assert.Equal((HttpStatusCode)428, (await client.SendAsync(Change(HttpMethod.Put, path, null, body))).StatusCode);
        var update = await client.SendAsync(Change(HttpMethod.Put, path, 1, body)); Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("\"2\"", update.Headers.ETag!.Tag);
        Assert.Equal((HttpStatusCode)412, (await client.SendAsync(Change(HttpMethod.Put, path, 1, body))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Change(HttpMethod.Delete, path, 2))).StatusCode);
        var inactive = await client.GetFromJsonAsync<CatalogJob>(path); Assert.False(inactive!.IsActive); Assert.Equal(3, inactive.Version);
        Assert.Empty((await client.GetFromJsonAsync<PageResult<CatalogJob>>("/api/catalog/jobs/"))!.Items);
        Assert.Single((await client.GetFromJsonAsync<PageResult<CatalogJob>>("/api/catalog/jobs/?includeInactive=true"))!.Items);
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(Change(HttpMethod.Put, path, 3, body))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/catalog/jobs/{Guid.NewGuid()}" )).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/catalog/jobs/", new { name = "", description = "", priceCents = -1 })).StatusCode);
        using var anonymous = app.CreateClient(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        var fresh = new PostgresJobCatalog(h.Factory, h.Clock); Assert.False((await fresh.Find(job.Id, default))!.IsActive);
    }

    [Fact]
    public async Task CatalogCompetingWritesHaveOneWinnerAndNoCrossDatabaseAccess()
    {
        var h = await postgres.Create(); var catalog = new PostgresJobCatalog(h.Factory, h.Clock);
        var job = await catalog.Create(new("A", "", 0), "staff:test", default);
        var attempts = Enumerable.Range(1, 4).Select(async price =>
        {
            try { await new PostgresJobCatalog(h.Factory, h.Clock).Update(job.Id, 1, new("A", "", price), "staff:test", default); return true; }
            catch (OperationConcurrencyException) { return false; }
        });
        var results = await Task.WhenAll(attempts); Assert.Single(results, result => result);
        Assert.Equal(2, (await catalog.Find(job.Id, default))!.Version);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => catalog.Deactivate(Guid.NewGuid(), 1, "staff:test", default));
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.Create(new("A", "", 1), "", default));
        await Assert.ThrowsAsync<ArgumentException>(() => catalog.List(new(0, 1), false, default));
        Assert.Empty((await catalog.List(new(int.MaxValue, 100), false, default)).Items);
    }

    [Fact]
    public async Task DiagnosticUsesPersistedLaborAndRealHttpInventoryAndKeepsFrozenSnapshotAfterCatalogChanges()
    {
        var h = await postgres.Create(); var catalog = new PostgresJobCatalog(h.Factory, h.Clock);
        var labor = await catalog.Create(new("Mão de obra", "", 1000), "staff:test", default);
        await h.Service().Receive(h.Context(), h.VehicleId, [new(h.Job.LineId, labor.Id)], [h.Part]);
        h.Clock.Advance(); await h.Service().StartDiagnostic(h.Context()); h.Clock.Advance();
        await using var inventory = await InventoryStub.Start();
        await using var baseApp = new ApiFactory(h, prices: false);
        await using var app = baseApp.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Pricing:ConsumableMode", "PerUnitV1"); builder.UseSetting("Inventory:BaseUrl", inventory.Address);
            builder.UseSetting("Inventory:BearerToken", InventoryStub.Token);
        });
        using var client = app.CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.Token());
        var intent = Guid.NewGuid(); var path = $"/api/operation/service-orders/{h.OrderId}/diagnostic/finish";
        HttpRequestMessage Finish() { var request = Change(HttpMethod.Post, path, 2, new { consumables = new[] { new { h.Consumable.LineId, h.Consumable.ConsumableId, h.Consumable.Quantity } } }); request.Headers.Add("Idempotency-Key", intent.ToString()); return request; }
        var response = await client.SendAsync(Finish()); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, inventory.Calls);
        var diagnostic = (await h.Queries.Get(h.OrderId)).Diagnostic!;
        Assert.Equal(1900, diagnostic.TotalPriceCents); Assert.Equal("per-unit-away-from-zero-v1", diagnostic.PricingPolicyId);
        await catalog.Update(labor.Id, 1, new("Preço novo", "", 9999), "staff:test", default);
        await catalog.Deactivate(labor.Id, 2, "staff:test", default);
        inventory.Status = 503;
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Finish())).StatusCode); Assert.Equal(2, inventory.Calls);
        Assert.Equal(1900, (await h.Queries.Get(h.OrderId)).Diagnostic!.TotalPriceCents);
        await Assert.ThrowsAsync<InvalidPriceQuoteException>(() => catalog.ReadPrices([labor.Id], default));
    }

    [Theory]
    [InlineData(401)] [InlineData(404)] [InlineData(503)]
    public async Task InventoryFailureOverRealHttpDoesNotSavePartialDiagnostic(int status)
    {
        var h = await postgres.Create(); var catalog = new PostgresJobCatalog(h.Factory, h.Clock);
        var labor = await catalog.Create(new("A", "", 1000), "staff:test", default);
        await h.Service().Receive(h.Context(), h.VehicleId, [new(h.Job.LineId, labor.Id)], [h.Part]);
        h.Clock.Advance(); await h.Service().StartDiagnostic(h.Context()); h.Clock.Advance();
        await using var inventory = await InventoryStub.Start(); inventory.Status = status;
        await using var baseApp = new ApiFactory(h, prices: false);
        await using var app = baseApp.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Pricing:ConsumableMode", "PerUnitV1"); builder.UseSetting("Inventory:BaseUrl", inventory.Address);
            builder.UseSetting("Inventory:BearerToken", InventoryStub.Token);
        });
        using var client = app.CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.Token());
        using var request = Change(HttpMethod.Post, $"/api/operation/service-orders/{h.OrderId}/diagnostic/finish", 2, new { consumables = Array.Empty<object>() });
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.SendAsync(request)).StatusCode);
        Assert.Equal((1, 2, 2, 2), await h.Counts()); Assert.Null((await h.Queries.Get(h.OrderId)).Diagnostic);
    }
}




