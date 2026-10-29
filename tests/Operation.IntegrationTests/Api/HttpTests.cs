using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using GearFlow.Operation.Application;
using GearFlow.Operation.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Operation.IntegrationTests.Api;

[Collection("Postgres")]
public class HttpTests(PostgresFixture postgres)
{
    private static string Path(IntegrationHarness h, string suffix = "") => $"/api/operation/service-orders/{h.OrderId}{suffix}";
    private static HttpRequestMessage Command(string path, long version, Guid? intent = null, object? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add("Idempotency-Key", (intent ?? Guid.NewGuid()).ToString());
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }
    private static async Task Problem(HttpResponseMessage response, int status, string? code = null)
    {
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var data = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(data.TryGetProperty("traceId", out _));
        if (code is not null) Assert.Equal(code, data.GetProperty("code").GetString());
        Assert.False(data.TryGetProperty("exception", out _));
    }

    [Theory]
    [InlineData("missing", 401)] [InlineData("customer", 403)] [InlineData("audience", 401)]
    [InlineData("expired", 401)] [InlineData("signature", 401)] [InlineData("subject", 403)]
    public async Task RealJwtValidationAndStaffPolicyRejectUnauthorizedRequests(string scenario, int status)
    {
        var h = await postgres.Create(); await using var app = new ApiFactory(h);
        using var client = app.CreateClient();
        if (scenario != "missing") client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiFactory.Token(
            actorType: scenario == "customer" ? "customer" : "staff", audience: scenario == "audience" ? "other" : "operation-api",
            expired: scenario == "expired", wrongKey: scenario == "signature", subject: scenario == "subject" ? "invalid" : null));
        await Problem(await client.GetAsync("/api/operation/queue"), status);
        Assert.Equal((0, 0, 0, 0), await h.Counts());
    }

    [Fact]
    public async Task HttpDiagnosticFlowPersistsAuthenticatedActorAndReturnsHistoricalReplay()
    {
        var h = await postgres.Create(); await h.Receive(); h.Clock.Advance();
        await using var app = new ApiFactory(h); using var client = app.Staff();
        var get = await client.GetAsync(Path(h)); Assert.Equal("\"1\"", get.Headers.ETag?.Tag);
        var intent = Guid.NewGuid();
        var start = await client.SendAsync(Command(Path(h, "/diagnostic/start"), 1, intent));
        Assert.Equal(HttpStatusCode.OK, start.StatusCode); Assert.Equal("\"2\"", start.Headers.ETag?.Tag);
        h.Clock.Advance();
        var finish = await client.SendAsync(Command(Path(h, "/diagnostic/finish"), 2, body: new { consumables = new[] { new { h.Consumable.LineId, h.Consumable.ConsumableId, h.Consumable.Quantity } } }));
        Assert.Equal(HttpStatusCode.OK, finish.StatusCode);
        var replay = await client.SendAsync(Command(Path(h, "/diagnostic/start"), 1, intent));
        Assert.Equal(await start.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal("\"2\"", replay.Headers.ETag?.Tag);
        var operation = await h.Queries.Get(h.OrderId);
        Assert.Equal(3, operation.Version); Assert.Equal(1900, operation.Diagnostic!.TotalPriceCents);
        Assert.Equal("staff:11111111-1111-4111-8111-111111111111", operation.History[1].Actor);
        await Problem(await client.SendAsync(Command(Path(h, "/diagnostic/fail"), 3, intent, new { reason = "Falha" })), 409, "intent_conflict");
        await Problem(await client.SendAsync(Command(Path(h, "/diagnostic/start"), 1)), 412, "version_conflict");
        Assert.Equal((1, 3, 3, 3), await h.Counts());
    }

    [Theory]
    [InlineData("missing-match", 428)] [InlineData("weak", 400)] [InlineData("wildcard", 400)]
    [InlineData("multiple", 400)] [InlineData("zero", 400)] [InlineData("bad-key", 400)]
    public async Task CommandHeadersAreValidatedWithoutMutation(string scenario, int status)
    {
        var h = await postgres.Create(); await h.Receive(); await using var app = new ApiFactory(h); using var client = app.Staff();
        using var request = Command(Path(h, "/diagnostic/start"), 1);
        if (scenario == "bad-key") { request.Headers.Remove("Idempotency-Key"); request.Headers.Add("Idempotency-Key", "invalid"); }
        else
        {
            request.Headers.Remove("If-Match");
            var value = scenario switch { "weak" => "W/\"1\"", "wildcard" => "*", "multiple" => "\"1\",\"2\"", "zero" => "\"0\"", _ => null };
            if (value is not null) request.Headers.TryAddWithoutValidation("If-Match", value);
        }
        await Problem(await client.SendAsync(request), status);
        Assert.Equal((1, 1, 1, 1), await h.Counts());
    }

    [Fact]
    public async Task QueriesHistoryOpenApiAndLivenessAreAvailableWithExpectedAccess()
    {
        var h = await postgres.Create(); await h.Receive(); await using var app = new ApiFactory(h); using var client = app.Staff();
        foreach (var path in new[] { "/api/operation/queue", "/api/operation/service-orders", "/api/operation/durations", Path(h, "/history"), "/openapi/v1.json", "/health/live" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
        await Problem(await client.GetAsync($"/api/operation/service-orders/{Guid.NewGuid()}"), 404);
        await Problem(await client.GetAsync("/api/operation/queue?status=Unknown"), 400);
        var schema = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        Assert.Equal("bearer", schema.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer").GetProperty("scheme").GetString());
        var action = schema.GetProperty("paths").GetProperty("/api/operation/service-orders/{id}/diagnostic/start").GetProperty("post");
        Assert.Contains(action.GetProperty("parameters").EnumerateArray(), item => item.GetProperty("name").GetString() == "If-Match" && item.GetProperty("required").GetBoolean());
        using var anonymous = app.CreateClient(); Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health/live")).StatusCode);
        await Problem(await anonymous.GetAsync("/openapi/v1.json"), 401);
    }

    [Fact]
    public async Task ExecutionEndpointsUseBudgetJobIdentityAndDoNotExposeSagaCommands()
    {
        var h = await postgres.Create(); await h.Diagnosed();
        var authorization = h.Context(); var operation = (await h.Store.Load(h.OrderId, default))!;
        await h.Service().AuthorizeExecution(authorization, new(authorization.IntentId, Guid.NewGuid(), 1, operation.Diagnostic!, [new(h.BudgetJobId, h.Job.LineId)]));
        h.Clock.Advance(); await using var app = new ApiFactory(h); using var client = app.Staff();
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Command(Path(h, "/execution/start"), 4))).StatusCode);
        await Problem(await client.SendAsync(Command(Path(h, $"/execution/jobs/{h.Job.CatalogJobId}/done"), 5)), 409);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Command(Path(h, $"/execution/jobs/{h.BudgetJobId}/done"), 5))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Command(Path(h, "/execution/finish"), 6))).StatusCode);
        Assert.Equal(7, (await h.Queries.Get(h.OrderId)).Version);
        foreach (var suffix in new[] { "/receive", "/authorize", "/cancel" })
            await Problem(await client.SendAsync(Command(Path(h, suffix), 7)), 404);
    }

    [Theory]
    [InlineData("{\"consumables\":[],\"priceCents\":0}")]
    [InlineData("{}")]
    [InlineData("{\"consumables\":null}")]
    [InlineData("{\"consumables\":[null]}")]
    public async Task FinishRejectsMalformedOrClientPricedPayloads(string json)
    {
        var h = await postgres.Create(); await h.Receive(); h.Clock.Advance(); await h.Service().StartDiagnostic(h.Context());
        await using var app = new ApiFactory(h); using var client = app.Staff();
        using var request = Command(Path(h, "/diagnostic/finish"), 2); request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        await Problem(await client.SendAsync(request), 400);
        Assert.Equal((1, 2, 2, 2), await h.Counts());
    }

    [Fact]
    public async Task MissingPricingConfigurationReturns503AndKeepsDiagnosticOpen()
    {
        var h = await postgres.Create(); await h.Receive(); h.Clock.Advance(); await h.Service().StartDiagnostic(h.Context());
        await using var app = new ApiFactory(h, prices: false); using var client = app.Staff();
        await Problem(await client.SendAsync(Command(Path(h, "/diagnostic/finish"), 2, body: new { consumables = Array.Empty<object>() })), 503, "pricing_unavailable");
        Assert.Equal((1, 2, 2, 2), await h.Counts());
    }

    [Fact]
    public async Task DiagnosticFailureIsExplicitAndInvalidStateIsConflict()
    {
        var h = await postgres.Create(); await h.Receive(); await using var app = new ApiFactory(h); using var client = app.Staff();
        await Problem(await client.SendAsync(Command(Path(h, "/execution/start"), 1)), 409);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Command(Path(h, "/diagnostic/start"), 1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Command(Path(h, "/diagnostic/fail"), 2, body: new { reason = "Falha técnica" }))).StatusCode);
    }
}


