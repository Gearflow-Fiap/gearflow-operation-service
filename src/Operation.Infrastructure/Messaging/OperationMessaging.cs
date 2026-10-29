using System.Text.Json.Serialization;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using W = GearFlow.Contracts.Operation.V1;

namespace GearFlow.Operation.Infrastructure.Messaging;

public sealed class OperationCommandConsumer<T>(OperationCommandHandler handler) : IConsumer<T> where T : class
{
    public Task Consume(ConsumeContext<T> context) => handler.Handle(context.Message, context.MessageId, context.CorrelationId, context.CancellationToken);
}
public sealed record OperationRabbitOptions(string Host, ushort Port, string VirtualHost, string Username, string Password);
public static class OperationMessaging
{
    public static IServiceCollection AddOperationMessaging(this IServiceCollection services, OperationRabbitOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Host) || options.Port == 0 || string.IsNullOrWhiteSpace(options.VirtualHost) ||
            string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrWhiteSpace(options.Password)) throw new ArgumentException("Configuração RabbitMQ incompleta.");
        services.AddScoped<OperationCommandHandler>(); services.AddScoped<IOperationPublisher, MassTransitOperationPublisher>(); services.AddScoped<OperationOutboxDispatcher>();
        services.AddMassTransit(bus =>
        {
            bus.AddConsumer<OperationCommandConsumer<W.StartDiagnostic>>(); bus.AddConsumer<OperationCommandConsumer<W.StartExecution>>();
            bus.AddConsumer<OperationCommandConsumer<W.CancelOperation>>(); bus.AddConsumer<OperationCommandConsumer<W.UpdateOperationVehicle>>(); bus.AddConsumer<OperationCommandConsumer<W.DeactivateOperation>>();
            bus.UsingRabbitMq((context, cfg) =>
            {
                cfg.Host(options.Host, options.Port, options.VirtualHost, host => { host.Username(options.Username); host.Password(options.Password); });
                cfg.ConfigureJsonSerializerOptions(json => { json.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)); json.RespectRequiredConstructorParameters = true; json.NumberHandling = JsonNumberHandling.Strict; return json; });
                Configure<W.StartDiagnostic>(cfg, context, "operation-start-diagnostic"); Configure<W.StartExecution>(cfg, context, "operation-start-execution");
                Configure<W.CancelOperation>(cfg, context, "operation-cancel-operation"); Configure<W.UpdateOperationVehicle>(cfg, context, "operation-update-vehicle");
                Configure<W.DeactivateOperation>(cfg, context, "operation-deactivate");
            });
        });
        return services;
    }
    private static void Configure<T>(IRabbitMqBusFactoryConfigurator cfg, IBusRegistrationContext context, string queue) where T : class =>
        cfg.ReceiveEndpoint(queue, endpoint =>
        {
            endpoint.PrefetchCount = 8; endpoint.ConcurrentMessageLimit = 4;
            endpoint.UseMessageRetry(retry => { retry.Intervals(100, 500, 1000); retry.Ignore<PermanentMessageException>(); });
            endpoint.ConfigureConsumer<OperationCommandConsumer<T>>(context);
        });
}

