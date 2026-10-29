using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace GearFlow.Operation.Api;

public static class OperationSecurity
{
    public static IServiceCollection AddOperationSecurity(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<JwtOptions>().Bind(config.GetSection(JwtOptions.SectionName));
        services.AddOptions<StaffAuthorizationOptions>().Bind(config.GetSection(StaffAuthorizationOptions.SectionName));
        var jwt = config.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new();
        var staff = config.GetSection(StaffAuthorizationOptions.SectionName).Get<StaffAuthorizationOptions>() ?? new();
        static string Required(string key, string value) => string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"{key} obrigatório.") : value;
        var secret = Required("Jwt:Secret", jwt.Secret);
        if (Encoding.UTF8.GetByteCount(secret) < 32) throw new InvalidOperationException("Jwt:Secret deve ter ao menos 32 bytes.");
        var issuer = Required("Jwt:Issuer", jwt.Issuer);
        var audience = Required("Jwt:Audience", jwt.Audience);
        var claim = Required("Authorization:StaffClaimType", staff.StaffClaimType);
        var value = Required("Authorization:StaffClaimValue", staff.StaffClaimValue);
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.IncludeErrorDetails = false;
            options.TokenValidationParameters = new()
            {
                ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
                ValidateIssuer = true, ValidIssuer = issuer, ValidateAudience = true, ValidAudience = audience,
                ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.Zero, NameClaimType = "sub"
            };
        });
        services.AddAuthorizationBuilder().AddPolicy("Staff", policy => policy.RequireAuthenticatedUser()
            .RequireClaim(claim, value).RequireAssertion(context =>
                StaffActor.HasValidSubject(context.User)));
        return services;
    }
}
