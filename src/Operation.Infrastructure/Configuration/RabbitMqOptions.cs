using GearFlow.Operation.Infrastructure.Messaging;
using Microsoft.Extensions.Options;

namespace GearFlow.Operation.Infrastructure.Configuration;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";
    public string Host { get; set; } = "";
    public ushort Port { get; set; } = 5672;
    public string VirtualHost { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    public bool IsComplete() => !string.IsNullOrWhiteSpace(Host) && Port > 0 && !string.IsNullOrWhiteSpace(VirtualHost)
        && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);

    public OperationRabbitOptions ToTransportOptions()
    {
        if (!IsComplete()) throw new OptionsValidationException(Options.DefaultName, typeof(RabbitMqOptions), ["RabbitMq: configuração obrigatória incompleta com Messaging:Enabled=true."]);
        return new(Host, Port, VirtualHost, Username, Password);
    }
}
