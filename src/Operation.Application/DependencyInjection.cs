using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GearFlow.Operation.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddOperationApplication(this IServiceCollection services,
        Func<IServiceProvider, ConsumablePricingMode?> pricingMode)
    {
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.AddScoped<OperationQueries>();
        services.AddScoped<OperationService>();
        services.AddScoped(provider => new DiagnosticPricer(provider.GetRequiredService<IPriceCatalog>(),
            pricingMode(provider), provider.GetRequiredService<TimeProvider>()));
        return services;
    }
}
