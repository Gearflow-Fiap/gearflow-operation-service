namespace GearFlow.Operation.Api;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Secret { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
}

public sealed class StaffAuthorizationOptions
{
    public const string SectionName = "Authorization";
    public string StaffClaimType { get; set; } = "";
    public string StaffClaimValue { get; set; } = "";
}
