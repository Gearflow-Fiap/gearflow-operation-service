namespace GearFlow.Operation.Infrastructure.Configuration;

public sealed class MessagingOptions
{
    public const string SectionName = "Messaging";
    public bool Enabled { get; set; }
    public bool DispatchEnabled { get; set; }
}
