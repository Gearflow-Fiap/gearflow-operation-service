using GearFlow.Operation.Application;
using GearFlow.Operation.Infrastructure.Configuration;
using GearFlow.Operation.Infrastructure.Messaging;
using GearFlow.Operation.Infrastructure.Persistence;
using GearFlow.Operation.Infrastructure.Pricing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GearFlow.Operation.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOperationInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Operation), "ConnectionStrings:Operation obrigatório.").ValidateOnStart();
        // Absence remains supported: pricing fails with 503 only when external prices are needed.
        // URL validation is lazy, as before, when the client is resolved.
        services.AddOptions<InventoryOptions>().Bind(configuration.GetSection(InventoryOptions.SectionName))
            .Validate(options => options.HasAllowedAddress(), "Inventory:BaseUrl requer HTTPS ou HTTP explicitamente permitido, sem credenciais, query ou fragmento.");
        services.AddOptions<MessagingOptions>().Bind(configuration.GetSection(MessagingOptions.SectionName));
        services.AddOptions<RabbitMqOptions>().Bind(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton<IDbContextFactory<OperationDbContext>>(provider => new OperationContextFactory(
            provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.Operation));
        services.AddScoped<PostgresOperationStore>();
        services.AddScoped<IOperationUnitOfWork>(provider => provider.GetRequiredService<PostgresOperationStore>());
        services.AddScoped<IOperationReadStore>(provider => provider.GetRequiredService<PostgresOperationStore>());
        services.AddScoped<MessagingDiagnostics>();
        services.AddScoped<IJobCatalog, PostgresJobCatalog>();
        services.AddSingleton<IInventoryCredential, ConfiguredInventoryCredential>();
        services.AddHttpClient<InventoryPriceClient>((provider, http) =>
        {
            var options = provider.GetRequiredService<IOptionsMonitor<InventoryOptions>>().CurrentValue;
            if (!string.IsNullOrWhiteSpace(options.BaseUrl)) http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(5);
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.AddScoped<IPriceCatalog, CompositePriceCatalog>();

        var messaging = configuration.GetSection(MessagingOptions.SectionName).Get<MessagingOptions>() ?? new();
        if (messaging.Enabled)
        {
            var rabbit = configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>() ?? new();
            services.AddOperationMessaging(rabbit.ToTransportOptions());
        }
        return services;
    }
}
