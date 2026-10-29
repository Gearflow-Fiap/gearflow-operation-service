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

namespace Operation.IntegrationTests.Support;

internal sealed class ApiFactory(IntegrationHarness harness, bool prices = true) : WebApplicationFactory<Program>
{
    internal const string Secret = "operation_test_signing_key_only_32_bytes_minimum";
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Secret", Secret).UseSetting("Jwt:Issuer", "operation-tests").UseSetting("Jwt:Audience", "operation-api");
        builder.UseSetting("Authorization:StaffClaimType", "actor_type").UseSetting("Authorization:StaffClaimValue", "staff");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDbContextFactory<OperationDbContext>>();
            services.AddSingleton<IDbContextFactory<OperationDbContext>>(harness.Factory);
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(harness.Clock);
            if (prices)
            {
                services.RemoveAll<DiagnosticPricer>();
                services.AddSingleton(new DiagnosticPricer(new IntegrationCatalog(harness.Clock), ConsumablePricingMode.PerUnitV1, harness.Clock));
            }
        });
    }

    internal static string Token(string actorType = "staff", string? subject = null, string audience = "operation-api", bool expired = false, bool wrongKey = false)
    {
        var now = DateTime.UtcNow;
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("operation-tests", audience,
            [new Claim("sub", subject ?? "11111111-1111-4111-8111-111111111111"), new Claim("actor_type", actorType)],
            now.AddHours(-1), expired ? now.AddMinutes(-1) : now.AddMinutes(10),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(wrongKey ? Secret + "wrong" : Secret)), SecurityAlgorithms.HmacSha256)));
    }
    internal HttpClient Staff() { var client = CreateClient(); client.DefaultRequestHeaders.Authorization = new("Bearer", Token()); return client; }
}

