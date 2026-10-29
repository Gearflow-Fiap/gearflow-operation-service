using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GearFlow.Operation.Application;
using GearFlow.Operation.Domain;
using GearFlow.Operation.Infrastructure.Persistence;
using Xunit;

namespace Operation.IntegrationTests.Api;

[Collection("Postgres")]
public class HttpInputContractTests(PostgresFixture postgres)
{
    private static async Task Problem(HttpResponseMessage response, int status, string code)
    {
        using (response)
        {
            Assert.Equal(status, (int)response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(code, problem.GetProperty("code").GetString());
            Assert.True(problem.TryGetProperty("traceId", out _));
            Assert.False(problem.TryGetProperty("exception", out _));
        }
    }

    [Fact]
    public async Task EveryVersionedWriteRejectsInvalidPreconditionsBeforeChangingState()
    {
        var h = await postgres.Create(); await h.Receive();
        await using var api = new ApiFactory(h); using var client = api.Staff();
        var catalog = new PostgresJobCatalog(h.Factory, h.Clock);
        var job = await catalog.Create(new("Serviço", "", 100), "staff:seed", default);
        var root = $"/api/operation/service-orders/{h.OrderId}";
        var routes = new (HttpMethod Method, string Path, object? Body, bool Operation)[]
        {
            (HttpMethod.Post, root + "/diagnostic/start", null, true),
            (HttpMethod.Post, root + "/diagnostic/finish", new { consumables = Array.Empty<object>() }, true),
            (HttpMethod.Post, root + "/diagnostic/fail", new { reason = "Motivo" }, true),
            (HttpMethod.Post, root + "/execution/start", null, true),
            (HttpMethod.Post, root + $"/execution/jobs/{h.BudgetJobId}/done", null, true),
            (HttpMethod.Post, root + "/execution/finish", null, true),
            (HttpMethod.Put, $"/api/catalog/jobs/{job.Id}", new { name = "Outro", description = "", priceCents = 200 }, false),
            (HttpMethod.Delete, $"/api/catalog/jobs/{job.Id}", null, false)
        };
        string?[] invalid = [null, "", "*", "W/\"1\"", "1", "\"0\"", "\"-1\"", "\"+1\"", "\"01\"", "\" 1\"", "\"1.0\"", "\"9223372036854775808\"", "\"1\",\"2\""];
        foreach (var route in routes)
        foreach (var tag in invalid)
        {
            using var request = new HttpRequestMessage(route.Method, route.Path);
            if (route.Body is not null) request.Content = JsonContent.Create(route.Body);
            if (route.Operation) request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            if (tag is not null) request.Headers.TryAddWithoutValidation("If-Match", tag);
            await Problem(await client.SendAsync(request), string.IsNullOrEmpty(tag) ? 428 : 400, string.IsNullOrEmpty(tag) ? "if_match_required" : "invalid_if_match");
        }
        Assert.Equal((1, 1, 1, 1), await h.Counts());
        Assert.Equal(job, await catalog.Find(job.Id, default));
    }

    [Fact]
    public async Task OperationIntentValidationPrecedesVersionAndDoesNotAffectCatalogRules()
    {
        var h = await postgres.Create(); await h.Receive();
        await using var api = new ApiFactory(h); using var client = api.Staff();
        foreach (var key in new string?[] { null, "", "invalid", Guid.Empty.ToString(), Guid.NewGuid() + "," + Guid.NewGuid() })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/operation/service-orders/{h.OrderId}/diagnostic/start");
            if (key is not null) request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
            await Problem(await client.SendAsync(request), 400, "invalid_idempotency_key");
        }
        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/catalog/jobs/") { Content = JsonContent.Create(new { name = "Serviço", description = "", priceCents = 0 }) };
        create.Headers.TryAddWithoutValidation("If-Match", "invalid"); create.Headers.TryAddWithoutValidation("Idempotency-Key", "invalid");
        using var created = await client.SendAsync(create); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var job = (await created.Content.ReadFromJsonAsync<CatalogJob>())!;
        Assert.Equal("staff:11111111-1111-4111-8111-111111111111", job.UpdatedBy);
        using var update = new HttpRequestMessage(HttpMethod.Put, $"/api/catalog/jobs/{job.Id}") { Content = JsonContent.Create(new { name = "Atualizado", description = "", priceCents = 1 }) };
        update.Headers.TryAddWithoutValidation("If-Match", "\"1\""); update.Headers.TryAddWithoutValidation("Idempotency-Key", "invalid");
        using var changed = await client.SendAsync(update); Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal("\"2\"", changed.Headers.ETag!.Tag);
        Assert.Equal((1, 1, 1, 1), await h.Counts());
    }

    [Fact]
    public async Task MalformedRequestsPreserveErrorCodesAndLeaveNoPartialState()
    {
        var h = await postgres.Create(); await h.Receive(); await h.Service().StartDiagnostic(h.Context());
        await using var api = new ApiFactory(h); using var client = api.Staff();
        var path = $"/api/operation/service-orders/{h.OrderId}";
        var samples = new List<(string Suffix, string Body, string Code)>
        {
            ("/diagnostic/finish", "{}", "invalid_request"),
            ("/diagnostic/finish", "{\"consumables\":null}", "invalid_consumables"),
            ("/diagnostic/finish", "{\"consumables\":[null]}", "invalid_consumables"),
            ("/diagnostic/finish", "{\"consumables\":[],\"actor\":\"system\"}", "invalid_request"),
            ("/diagnostic/finish", JsonSerializer.Serialize(new { consumables = Enumerable.Repeat(new { lineId = Guid.NewGuid(), consumableId = Guid.NewGuid(), quantity = 1 }, DiagnosticSnapshot.MaximumLines + 1) }), "invalid_consumables"),
            ("/diagnostic/finish", JsonSerializer.Serialize(new { consumables = new[] { new { lineId = Guid.Empty, consumableId = Guid.NewGuid(), quantity = 1 } } }), "invalid_request"),
            ("/diagnostic/finish", JsonSerializer.Serialize(new { consumables = new[] { new { lineId = Guid.NewGuid(), consumableId = Guid.NewGuid(), quantity = 0 } } }), "invalid_request"),
            ("/diagnostic/fail", "{\"reason\":null}", "invalid_request"),
            ("/diagnostic/fail", "{\"reason\":\"   \"}", "invalid_request"),
            ("/diagnostic/fail", JsonSerializer.Serialize(new { reason = new string('x', 2001) }), "invalid_request")
        };
        foreach (var sample in samples)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path + sample.Suffix) { Content = new StringContent(sample.Body, Encoding.UTF8, "application/json") };
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); request.Headers.TryAddWithoutValidation("If-Match", "\"2\"");
            await Problem(await client.SendAsync(request), 400, sample.Code);
        }
        Assert.Equal((1, 2, 2, 2), await h.Counts());
        Assert.Null((await h.Queries.Get(h.OrderId)).Diagnostic);
    }
    [Fact]
    public async Task JwtSubjectPolicyRejectsAmbiguityAndPersistsOnlyAuthenticatedActor()
    {
        var h = await postgres.Create(); await h.Receive();
        await using var api = new ApiFactory(h); using var client = api.CreateClient();
        var path = $"/api/operation/service-orders/{h.OrderId}/diagnostic/start";
        string Token(IEnumerable<Claim> subjects, string profile = "staff")
        {
            var now = DateTime.UtcNow;
            return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("operation-tests", "operation-api",
                subjects.Append(new Claim("actor_type", profile)), now.AddMinutes(-1), now.AddMinutes(10),
                new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ApiFactory.Secret)), SecurityAlgorithms.HmacSha256)));
        }
        Claim[][] invalid = [[], [new("sub", "")], [new("sub", Guid.Empty.ToString())], [new("sub", "invalid")],
            [new("sub", Guid.NewGuid().ToString()), new("sub", Guid.NewGuid().ToString())]];
        foreach (var subjects in invalid)
        {
            client.DefaultRequestHeaders.Authorization = new("Bearer", Token(subjects));
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); request.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
            await Problem(await client.SendAsync(request), subjects.Length > 1 ? 401 : 403, "http_error");
        }
        Assert.Equal((1, 1, 1, 1), await h.Counts());
        const string subject = "AAAAAAAA-BBBB-4CCC-8DDD-EEEEEEEEEEEE";
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token([new("sub", subject)], "customer"));
        await Problem(await client.GetAsync("/api/operation/queue"), 403, "http_error");
        client.DefaultRequestHeaders.Authorization = new("Bearer", Token([new("sub", subject)]));
        using var valid = new HttpRequestMessage(HttpMethod.Post, path);
        valid.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); valid.Headers.TryAddWithoutValidation("If-Match", "\"1\"");
        valid.Headers.Add("X-Actor", "system:spoof");
        using var response = await client.SendAsync(valid); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("staff:" + subject, (await h.Queries.Get(h.OrderId)).History.Last().Actor);
        Assert.Equal((1, 2, 2, 2), await h.Counts());
    }

    [Fact]
    public async Task CatalogRequestsKeepExistingNormalizationLimitsAndPreconditionOrder()
    {
        var h = await postgres.Create(); await using var api = new ApiFactory(h); using var client = api.Staff();
        var path = "/api/catalog/jobs/";
        foreach (var definition in new object[]
        {
            new { name = (string?)null, description = "", priceCents = 0 },
            new { name = "Nome", description = (string?)null, priceCents = 0 },
            new { name = new string('x', 129), description = "", priceCents = 0 },
            new { name = "Nome", description = new string('x', 2001), priceCents = 0 },
            new { name = "Nome", description = "", priceCents = -1 },
            new { name = "Nome", priceCents = 0 },
            new { name = "Nome", description = "", priceCents = 0, actor = "system" }
        }) await Problem(await client.PostAsJsonAsync(path, definition), 400, "invalid_request");
        var name = new string('n', 128); var description = new string('d', 2000);
        using var created = await client.PostAsJsonAsync(path, new { name = "  " + name + "  ", description, priceCents = 0 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var job = (await created.Content.ReadFromJsonAsync<CatalogJob>())!;
        Assert.Equal(name, job.Name); Assert.Equal(description, job.Description);
        await Problem(await client.PutAsJsonAsync(path + job.Id, new { name = "", description = "", priceCents = -1 }), 428, "if_match_required");
        var jobs = (await client.GetFromJsonAsync<PageResult<CatalogJob>>(path))!;
        Assert.Single(jobs.Items); Assert.Equal(1, jobs.Items.Single().Version);
    }

}
