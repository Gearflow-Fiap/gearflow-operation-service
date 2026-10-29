using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace GearFlow.Operation.Api;

public static class OperationSecurity
{
    public static IServiceCollection AddOperationSecurity(this IServiceCollection services, IConfiguration config)
    {
        string Required(string key) => string.IsNullOrWhiteSpace(config[key]) ? throw new InvalidOperationException($"{key} obrigatório.") : config[key]!;
        var secret = Required("Jwt:Secret");
        if (Encoding.UTF8.GetByteCount(secret) < 32) throw new InvalidOperationException("Jwt:Secret deve ter ao menos 32 bytes.");
        var issuer = Required("Jwt:Issuer");
        var audience = Required("Jwt:Audience");
        var claim = Required("Authorization:StaffClaimType");
        var value = Required("Authorization:StaffClaimValue");
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
                context.User.FindAll("sub").Count() == 1 && Guid.TryParse(context.User.FindFirstValue("sub"), out var id) && id != Guid.Empty));
        return services;
    }
}
