using System.Net;
using GearFlow.Operation.Api;
using GearFlow.Operation.Application;
using GearFlow.Operation.Infrastructure;
using GearFlow.Operation.Infrastructure.Configuration;
using GearFlow.Operation.Infrastructure.Messaging;
using GearFlow.Operation.Infrastructure.Persistence;
using GearFlow.Operation.Infrastructure.Pricing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Operation.IntegrationTests.Api;

public class ConfigurationTests
{
    private static Dictionary<string, string?> Defaults() => new()
    {
        ["Jwt:Secret"] = ApiFactory.Secret, ["Jwt:Issuer"] = "operation-tests", ["Jwt:Audience"] = "operation-api",
        ["Authorization:StaffClaimType"] = "actor_type", ["Authorization:StaffClaimValue"] = "staff",
        ["ConnectionStrings:Operation"] = "Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused;Timeout=1;Pooling=false",
        ["Telemetry:BacklogEnabled"] = "false", ["Telemetry:ExportEnabled"] = "false", ["Messaging:Enabled"] = "false"
    };

    private sealed class ConfiguredApi(Dictionary<string, string?> settings) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            foreach (var (key, value) in settings) builder.UseSetting(key, value ?? "");
        }
    }

    [Fact]
    public async Task OptionalIntegrationsDoNotPreventLiveHostWithUnavailableDatabase()
    {
        var settings = Defaults();
        settings["Otlp:Endpoint"] = "invalid-secret-endpoint";
        settings["OTEL_EXPORTER_OTLP_PROTOCOL"] = "invalid";
        settings["RabbitMq:Port"] = "invalid";
        settings["Messaging:DispatchEnabled"] = "true";
        await using var api = new ConfiguredApi(settings);
        using var client = api.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.DoesNotContain(api.Services.GetServices<IHostedService>(), service => service is OutboxWorker or BacklogWorker);
        using var scope = api.Services.CreateScope();
        Assert.Throws<PricingUnavailableException>(() => scope.ServiceProvider.GetRequiredService<DiagnosticPricer>().PolicyId);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<InventoryPriceClient>());
    }

    [Theory]
    [InlineData("Jwt:Secret", "short", "Jwt:Secret")]
    [InlineData("Jwt:Issuer", "", "Jwt:Issuer")]
    [InlineData("Jwt:Audience", "", "Jwt:Audience")]
    [InlineData("Authorization:StaffClaimType", "", "Authorization:StaffClaimType")]
    [InlineData("Authorization:StaffClaimValue", "", "Authorization:StaffClaimValue")]
    [InlineData("ConnectionStrings:Operation", "", "ConnectionStrings:Operation")]
    public async Task MandatoryConfigurationFailsBeforeServingRequests(string key, string value, string expected)
    {
        var settings = Defaults(); settings[key] = value;
        await using var api = new ConfiguredApi(settings);
        var error = Assert.ThrowsAny<Exception>(() => api.CreateClient());
        Assert.Contains(expected, error.ToString());
        Assert.DoesNotContain(ApiFactory.Secret, error.ToString());
        Assert.DoesNotContain(settings["ConnectionStrings:Operation"] is { Length: > 0 } connection ? connection : "not-a-real-connection", error.ToString());
    }

    [Fact]
    public void CompositionPreservesScopedTransactionStoreAndSingletonClockAndFactory()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(Defaults()).Build();
        var services = new ServiceCollection(); services.AddLogging();
        var clock = new IntegrationClock(); services.AddSingleton<TimeProvider>(clock);
        services.AddOperationApplication(_ => ConsumablePricingMode.PerUnitV1).AddOperationInfrastructure(config);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var first = provider.CreateScope(); using var second = provider.CreateScope();
        var a = first.ServiceProvider; var b = second.ServiceProvider;
        Assert.Same(a.GetRequiredService<PostgresOperationStore>(), a.GetRequiredService<IOperationUnitOfWork>());
        Assert.Same(a.GetRequiredService<PostgresOperationStore>(), a.GetRequiredService<IOperationReadStore>());
        Assert.NotSame(a.GetRequiredService<PostgresOperationStore>(), b.GetRequiredService<PostgresOperationStore>());
        Assert.Same(a.GetRequiredService<OperationService>(), a.GetRequiredService<OperationService>());
        Assert.NotSame(a.GetRequiredService<OperationService>(), b.GetRequiredService<OperationService>());
        Assert.NotSame(a.GetRequiredService<OperationQueries>(), b.GetRequiredService<OperationQueries>());
        Assert.NotSame(a.GetRequiredService<DiagnosticPricer>(), b.GetRequiredService<DiagnosticPricer>());
        Assert.Same(clock, a.GetRequiredService<TimeProvider>());
        Assert.Same(a.GetRequiredService<IDbContextFactory<OperationDbContext>>(), b.GetRequiredService<IDbContextFactory<OperationDbContext>>());
        Assert.Same(a.GetRequiredService<IInventoryCredential>(), b.GetRequiredService<IInventoryCredential>());
        Assert.Equal("per-unit-away-from-zero-v1", a.GetRequiredService<DiagnosticPricer>().PolicyId);
        Assert.IsType<PostgresJobCatalog>(a.GetRequiredService<IJobCatalog>());
        Assert.IsType<CompositePriceCatalog>(a.GetRequiredService<IPriceCatalog>());
    }

    [Fact]
    public void InventoryCredentialObservesConfigurationReload()
    {
        var settings = Defaults(); settings["Inventory:BearerToken"] = "first-local-token";
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection(); services.AddLogging(); services.AddOperationInfrastructure(config);
        using var provider = services.BuildServiceProvider();
        var credential = provider.GetRequiredService<IInventoryCredential>();
        Assert.Equal("first-local-token", credential.GetBearerToken());
        config["Inventory:BearerToken"] = "rotated-local-token"; config.Reload();
        Assert.Equal("rotated-local-token", credential.GetBearerToken());
    }

    [Theory]
    [InlineData(null, false, true)]
    [InlineData("https://inventory.example/api", false, true)]
    [InlineData("http://127.0.0.1:5000", false, true)]
    [InlineData("http://inventory.example", false, false)]
    [InlineData("http://inventory.example", true, true)]
    [InlineData("https://user:secret@inventory.example", false, false)]
    [InlineData("https://inventory.example/?token=secret", false, false)]
    [InlineData("https://inventory.example/#secret", false, false)]
    [InlineData("relative", false, false)]
    public void InventoryAddressValidationRemainsLazyAndSanitized(string? address, bool allowHttp, bool valid)
    {
        var settings = Defaults(); settings["Inventory:BaseUrl"] = address; settings["Inventory:AllowInsecureHttp"] = allowHttp.ToString();
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection(); services.AddLogging(); services.AddOperationInfrastructure(config);
        using var provider = services.BuildServiceProvider();
        if (valid) Assert.NotNull(provider.GetRequiredService<InventoryPriceClient>());
        else
        {
            var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<InventoryPriceClient>());
            Assert.Contains("Inventory:BaseUrl", error.Message); Assert.DoesNotContain("secret", error.Message);
        }
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("unknown", null)]
    [InlineData("99", null)]
    [InlineData("PerUnitV1", ConsumablePricingMode.PerUnitV1)]
    [InlineData("LegacyLinePriceV1", ConsumablePricingMode.LegacyLinePriceV1)]
    public void PricingRequiresAnExplicitKnownPolicy(string? text, ConsumablePricingMode? expected) =>
        Assert.Equal(expected, new PricingOptions { ConsumableMode = text }.Mode);

    [Fact]
    public void RabbitConfigurationIsRequiredOnlyWhenMessagingIsEnabled()
    {
        var settings = Defaults(); settings["Messaging:Enabled"] = "true"; settings["RabbitMq:Password"] = "sensitive-password";
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var error = Assert.Throws<OptionsValidationException>(() => new ServiceCollection().AddOperationInfrastructure(config));
        Assert.Contains("RabbitMq", error.Message); Assert.DoesNotContain("sensitive-password", error.ToString());
        settings["RabbitMq:Host"] = "localhost"; settings["RabbitMq:VirtualHost"] = "test"; settings["RabbitMq:Username"] = "test";
        config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddOperationInfrastructure(config);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IOperationPublisher));
        var transport = config.GetSection("RabbitMq").Get<RabbitMqOptions>()!.ToTransportOptions();
        Assert.Equal((ushort)5672, transport.Port);
    }

    [Theory]
    [InlineData("https://collector.example", "grpc", false, true)]
    [InlineData("http://localhost:4318", "http/protobuf", false, true)]
    [InlineData("http://collector.example", "grpc", false, false)]
    [InlineData("http://collector.example", "grpc", true, true)]
    [InlineData("https://user:secret@collector.example", "grpc", false, false)]
    [InlineData("https://collector.example/?secret=secret", "grpc", false, false)]
    [InlineData("https://collector.example", "unsupported", false, false)]
    public void OtlpValidationOnlyRunsWhenExportIsEnabled(string endpoint, string protocol, bool allowHttp, bool valid)
    {
        var options = new OtlpOptions { Endpoint = endpoint, Protocol = protocol, Headers = "authorization=secret" };
        Assert.Null(options.Validate(new TelemetryOptions()));
        var telemetry = new TelemetryOptions { ExportEnabled = true, AllowInsecureHttp = allowHttp };
        if (valid) Assert.Equal(endpoint, options.Validate(telemetry)!.OriginalString);
        else Assert.DoesNotContain("secret", Assert.Throws<OptionsValidationException>(() => options.Validate(telemetry)).ToString());
    }

    [Fact]
    public void OtlpUsesExplicitEndpointBeforeEnvironmentFallback()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "https://fallback.example", ["OTEL_EXPORTER_OTLP_HEADERS"] = "local-test-header"
        }).Build();
        Assert.Equal("https://fallback.example", OtlpOptions.Read(config).Endpoint);
        Assert.Equal("grpc", OtlpOptions.Read(config).Protocol);
        config["Otlp:Endpoint"] = "https://explicit.example";
        Assert.Equal("https://explicit.example", OtlpOptions.Read(config).Endpoint);
        Assert.Equal("local-test-header", OtlpOptions.Read(config).Headers);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void HostedWorkersRespectIndependentActivationFlags(bool messaging, bool dispatch, bool backlog)
    {
        var settings = Defaults();
        settings["Messaging:Enabled"] = messaging.ToString(); settings["Messaging:DispatchEnabled"] = dispatch.ToString();
        settings["Telemetry:BacklogEnabled"] = backlog.ToString();
        var builder = WebApplication.CreateBuilder(); builder.Configuration.AddInMemoryCollection(settings);
        builder.AddOperationApi();
        var workers = builder.Services.Where(service => service.ServiceType == typeof(IHostedService)).ToArray();
        Assert.Equal(messaging && dispatch, workers.Any(service => service.ImplementationType == typeof(OutboxWorker)));
        Assert.Equal(backlog, workers.Any(service => service.ImplementationType == typeof(BacklogWorker)));
    }

    [Fact]
    public async Task JwtMinimumLengthCountsUtf8BytesAndStillEnforcesStaffPolicy()
    {
        var settings = Defaults(); settings["Jwt:Secret"] = new string('é', 16);
        await using var api = new ConfiguredApi(settings);
        using var client = api.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/catalog/jobs/")).StatusCode);
    }
}
