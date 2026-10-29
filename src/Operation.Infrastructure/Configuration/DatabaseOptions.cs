namespace GearFlow.Operation.Infrastructure.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "ConnectionStrings";
    public string Operation { get; set; } = "";
}
