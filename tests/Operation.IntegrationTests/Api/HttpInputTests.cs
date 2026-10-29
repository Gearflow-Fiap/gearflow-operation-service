using System.Security.Claims;
using GearFlow.Operation.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Operation.IntegrationTests.Api;

// Testes da borda HTTP sem banco, broker ou host externo.
public class HttpInputTests
{
    [Theory]
    [InlineData(1L)]
    [InlineData(42L)]
    [InlineData(long.MaxValue)]
    public void ResponseVersionRoundTripsThroughStrongPrecondition(long version)
    {
        var http = new DefaultHttpContext();
        StrongETag.Write(http.Response, version);
        http.Request.Headers.IfMatch = http.Response.Headers.ETag;
        Assert.Equal(version, StrongETag.RequireIfMatch(http.Request));
        Assert.Equal("no-store", http.Response.Headers.CacheControl);
        Assert.StartsWith("\"", http.Response.Headers.ETag.ToString());
    }

    [Theory]
    [InlineData("anonymous")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("empty")]
    [InlineData("zero")]
    [InlineData("malformed")]
    public void ActorReaderNeverFallsBackToSystemOrAcceptsAmbiguousIdentity(string scenario)
    {
        var claims = new List<Claim>();
        if (scenario != "missing") claims.Add(new("sub", scenario switch { "empty" => "", "zero" => Guid.Empty.ToString(), "malformed" => "not-a-guid", _ => Guid.NewGuid().ToString() }));
        if (scenario == "duplicate") claims.Add(new("sub", Guid.NewGuid().ToString()));
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, scenario == "anonymous" ? null : "test"));
        var error = Assert.Throws<HttpInputException>(() => StaffActor.Read(principal));
        Assert.Equal(401, error.Status); Assert.Equal("invalid_actor", error.Code);
    }

    [Fact]
    public void CommandReadsTrustedSubjectWithoutRewritingFingerprintIdentity()
    {
        const string subject = "{AAAAAAAA-BBBB-4CCC-8DDD-EEEEEEEEEEEE}";
        var intent = Guid.NewGuid(); var order = Guid.NewGuid(); var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", subject)], "test"));
        http.Request.Headers["Idempotency-Key"] = intent.ToString("B"); http.Request.Headers.IfMatch = "\"7\"";
        http.Request.Headers["X-Actor"] = "system:spoof";
        var context = OperationCommandInput.Read(http, order);
        Assert.Equal(intent, context.IntentId); Assert.Equal(order, context.ServiceOrderId); Assert.Equal(7, context.ExpectedVersion);
        Assert.Equal("staff:" + subject, context.Actor);
    }

    [Fact]
    public void ActorReaderUsesTheSameAuthenticatedIdentityRuleAsStaffPolicy()
    {
        var subject = Guid.NewGuid().ToString();
        var principal = new ClaimsPrincipal(new ClaimsIdentity[]
        {
            new(), new([new Claim("sub", subject)], "test")
        });
        Assert.Equal("staff:" + subject, StaffActor.Read(principal));
    }

    [Fact]
    public void MultiplePhysicalHeadersAreRejectedAndIntentErrorTakesPrecedence()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.IfMatch = new StringValues(["\"1\"", "\"2\""]);
        var match = Assert.Throws<HttpInputException>(() => StrongETag.RequireIfMatch(http.Request));
        Assert.Equal(400, match.Status); Assert.Equal("invalid_if_match", match.Code);
        http.Request.Headers["Idempotency-Key"] = new StringValues([Guid.NewGuid().ToString(), Guid.NewGuid().ToString()]);
        var intent = Assert.Throws<HttpInputException>(() => OperationCommandInput.Read(http, Guid.NewGuid()));
        Assert.Equal(400, intent.Status); Assert.Equal("invalid_idempotency_key", intent.Code);
    }
}
