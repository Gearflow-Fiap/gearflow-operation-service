namespace GearFlow.Operation.Infrastructure.Configuration;

public sealed class InventoryOptions
{
    public const string SectionName = "Inventory";
    public string? BaseUrl { get; set; }
    public string? BearerToken { get; set; }
    public bool AllowInsecureHttp { get; set; }

    public bool HasAllowedAddress() => string.IsNullOrWhiteSpace(BaseUrl) ||
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
        (uri.Scheme == "https" || uri.Scheme == "http" && (uri.IsLoopback || AllowInsecureHttp));
}
