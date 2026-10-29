using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Operation.IntegrationTests.Api;

[Collection("Postgres")]
public class EndpointContractTests(PostgresFixture postgres)
{
    private static IReadOnlyList<RouteEndpoint> BusinessRoutes(IServiceProvider services)
    {
        var routes = services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(route => route.RoutePattern.RawText?.StartsWith("/api/", StringComparison.Ordinal) == true).ToArray();
        Assert.NotEmpty(routes);
        return routes;
    }

    private static string SchemaPath(RouteEndpoint route) => Regex.Replace(route.RoutePattern.RawText!, @"\{([^}:]+)(?::[^}]+)?\}", "{$1}").TrimEnd('/');
    private static string RequestPath(RouteEndpoint route) => Regex.Replace(route.RoutePattern.RawText!, @"\{[^}]+\}", "22222222-2222-4222-8222-222222222222");
    private static JsonElement Operation(JsonElement schema, RouteEndpoint route, string method) =>
        schema.GetProperty("paths").EnumerateObject().Single(path => path.Name.TrimEnd('/') == SchemaPath(route)).Value.GetProperty(method.ToLowerInvariant());

    [Fact]
    public async Task EveryBusinessRouteHasExplicitDocumentationAndStaffPolicy()
    {
        var h = await postgres.Create(); await using var api = new ApiFactory(h); using var client = api.CreateClient();
        foreach (var route in BusinessRoutes(api.Services))
        {
            Assert.False(string.IsNullOrWhiteSpace(route.Metadata.GetMetadata<IEndpointSummaryMetadata>()?.Summary), route.RoutePattern.RawText);
            Assert.False(string.IsNullOrWhiteSpace(route.Metadata.GetMetadata<IEndpointDescriptionMetadata>()?.Description), route.RoutePattern.RawText);
            Assert.Null(route.Metadata.GetMetadata<IAllowAnonymous>());
            Assert.Contains(route.Metadata.GetOrderedMetadata<IAuthorizeData>(), policy => policy.Policy == "Staff");
        }
    }

    [Fact]
    public async Task EveryBusinessRouteRejectsAnonymousAndCustomerBeforeAnyBusinessEffect()
    {
        var h = await postgres.Create(); await using var api = new ApiFactory(h); using var client = api.CreateClient();
        foreach (var route in BusinessRoutes(api.Services))
        foreach (var method in route.Metadata.GetRequiredMetadata<HttpMethodMetadata>().HttpMethods)
        foreach (var customer in new[] { false, true })
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), RequestPath(route));
            if (method is "POST" or "PUT") request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
            if (customer) request.Headers.Authorization = new("Bearer", ApiFactory.Token("customer"));
            using var response = await client.SendAsync(request);
            Assert.Equal(customer ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized, response.StatusCode);
        }
        Assert.Equal((0, 0, 0, 0), await h.Counts());
    }

    [Fact]
    public async Task OpenApiDocumentsEveryBusinessMethodWithSecuritySuccessAndProblemResponses()
    {
        var h = await postgres.Create(); await using var api = new ApiFactory(h); using var client = api.Staff();
        var schema = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        Assert.DoesNotContain(ApiFactory.Secret, schema.GetRawText());
        foreach (var route in BusinessRoutes(api.Services))
        foreach (var method in route.Metadata.GetRequiredMetadata<HttpMethodMetadata>().HttpMethods)
        {
            var operation = Operation(schema, route, method);
            Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("summary").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("description").GetString()));
            Assert.Contains(operation.GetProperty("security").EnumerateArray(), security => security.TryGetProperty("Bearer", out _));
            var responses = operation.GetProperty("responses");
            var success = method == "POST" && SchemaPath(route) == "/api/catalog/jobs" ? "201" : "200";
            Assert.True(responses.GetProperty(success).GetProperty("content").GetProperty("application/json").GetProperty("schema").EnumerateObject().Any());
            foreach (var status in new[] { "400", "401", "403", "500" })
            {
                var response = responses.GetProperty(status);
                Assert.False(string.IsNullOrWhiteSpace(response.GetProperty("description").GetString()));
                var content = response.GetProperty("content").GetProperty("application/problem+json");
                Assert.True(content.TryGetProperty("schema", out _));
                Assert.Equal(int.Parse(status), content.GetProperty("example").GetProperty("status").GetInt32());
            }
            if (success == "201") Assert.False(responses.TryGetProperty("200", out _));
        }
    }

    [Fact]
    public async Task OpenApiHeadersAndErrorsFollowEachRouteContractWithoutRequiringCatalogIntent()
    {
        var h = await postgres.Create(); await using var api = new ApiFactory(h); using var client = api.Staff();
        var schema = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        foreach (var route in BusinessRoutes(api.Services))
        foreach (var method in route.Metadata.GetRequiredMetadata<HttpMethodMetadata>().HttpMethods)
        {
            var path = SchemaPath(route); var operation = Operation(schema, route, method);
            var command = path.StartsWith("/api/operation/", StringComparison.Ordinal) && method == "POST";
            var versioned = command || method is "PUT" or "DELETE";
            var parameters = operation.TryGetProperty("parameters", out var array) ? array.EnumerateArray().ToArray() : [];
            foreach (var name in new[] { "If-Match", "Idempotency-Key" })
            {
                var matches = parameters.Where(parameter => parameter.GetProperty("name").GetString() == name).ToArray();
                if (name == "If-Match" ? versioned : command)
                {
                    var parameter = Assert.Single(matches);
                    Assert.Equal("header", parameter.GetProperty("in").GetString()); Assert.True(parameter.GetProperty("required").GetBoolean());
                    Assert.True(parameter.TryGetProperty("example", out _));
                }
                else Assert.Empty(matches);
            }
            var responses = operation.GetProperty("responses");
            if (versioned)
                foreach (var status in new[] { "404", "412", "428" }) Assert.True(responses.TryGetProperty(status, out _));
            if (command) Assert.True(responses.TryGetProperty("409", out _));
            if (path.EndsWith("/diagnostic/finish", StringComparison.Ordinal)) Assert.True(responses.TryGetProperty("503", out _));
        }
        var finished = schema.GetProperty("paths").GetProperty("/api/operation/service-orders/{id}/diagnostic/finish").GetProperty("post");
        Assert.Empty(finished.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("example").GetProperty("consumables").EnumerateArray());
    }

    [Fact]
    public async Task CatalogExampleProducesTheDocumentedStatusLocationAndVersionHeaders()
    {
        var h = await postgres.Create(); await using var api = new ApiFactory(h); using var client = api.Staff();
        var schema = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var post = schema.GetProperty("paths").EnumerateObject().Single(path => path.Name.TrimEnd('/') == "/api/catalog/jobs").Value.GetProperty("post");
        var example = post.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("example");
        using var created = await client.PostAsJsonAsync("/api/catalog/jobs/", example);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location); Assert.NotNull(created.Headers.ETag);
        Assert.True(post.GetProperty("responses").GetProperty("201").GetProperty("headers").TryGetProperty("Location", out _));
        using var read = await client.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode); Assert.Equal(created.Headers.ETag, read.Headers.ETag);
        using var missingVersion = await client.PutAsJsonAsync(created.Headers.Location, example);
        Assert.Equal((HttpStatusCode)428, missingVersion.StatusCode);
        var problem = await missingVersion.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("if_match_required", problem.GetProperty("code").GetString());
    }
}
