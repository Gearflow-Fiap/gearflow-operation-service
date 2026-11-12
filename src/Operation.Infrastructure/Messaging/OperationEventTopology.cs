using MassTransit;
using W = GearFlow.Contracts.Operation.V1;

namespace GearFlow.Operation.Infrastructure.Messaging;

/// <summary>Altera apenas os treze eventos do Operation; comandos e tipos do framework usam o padrão existente.</summary>
public sealed class OperationEventEntityNameFormatter(IEntityNameFormatter fallback) : IEntityNameFormatter
{
    public string FormatEntityName<T>() => W.OperationEventNames.All.TryGetValue(typeof(T), out var name)
        ? name : fallback.FormatEntityName<T>();
}

public static class OperationEventTopology
{
    public static void ConfigureOperationEventNames(this IRabbitMqBusFactoryConfigurator configuration) =>
        configuration.MessageTopology.SetEntityNameFormatter(new OperationEventEntityNameFormatter(configuration.MessageTopology.EntityNameFormatter));
}
