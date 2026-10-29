using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace Operation.IntegrationTests.Api;

[Collection("Postgres")]
public class DocumentationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task DevelopmentShowsScalarAndSchemaButBusinessEndpointsStillRequireStaff()
    {
        var h = await postgres.Create();
        await using var baseApi = new ApiFactory(h);
        await using var api = baseApi.WithWebHostBuilder(builder => builder.UseEnvironment("Development"));
        using var client = api.CreateClient();
        var page = await client.GetAsync("/scalar/v1");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.StartsWith("text/html", page.Content.Headers.ContentType!.ToString());
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("GearFlow Operation API", html);
        Assert.DoesNotContain(ApiFactory.Secret, html);

        var schema = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        Assert.True(schema.GetProperty("paths").TryGetProperty("/api/operation/queue", out _));
        Assert.Equal("bearer", schema.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer").GetProperty("scheme").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/catalog/jobs/")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.Token("customer"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/catalog/jobs/")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.Token());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/catalog/jobs/")).StatusCode);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Testing")]
    public async Task NonDevelopmentKeepsSchemaProtectedAndDoesNotExposeScalar(string environment)
    {
        var h = await postgres.Create();
        await using var baseApi = new ApiFactory(h);
        await using var api = baseApi.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        using var client = api.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/scalar/v1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.Token("customer"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.Token());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/openapi/v1.json")).StatusCode);
    }
}
