using System.Text.Json.Serialization;
using GearFlow.Operation.Api;
using GearFlow.Operation.Application;
using GearFlow.Operation.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOperationSecurity(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<OperationErrors>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
    options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    options.SerializerOptions.RespectRequiredConstructorParameters = true;
});
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.AddOpenApi(options => options.AddDocumentTransformer<OperationOpenApi>());
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<IDbContextFactory<OperationDbContext>>(_ => new OperationContextFactory(
    builder.Configuration.GetConnectionString("Operation") ?? throw new InvalidOperationException("ConnectionStrings:Operation obrigatório.")));
builder.Services.AddScoped<PostgresOperationStore>();
builder.Services.AddScoped<IOperationUnitOfWork>(services => services.GetRequiredService<PostgresOperationStore>());
builder.Services.AddScoped<IOperationReadStore>(services => services.GetRequiredService<PostgresOperationStore>());
builder.Services.AddScoped<OperationQueries>();
builder.Services.AddScoped<OperationService>();
// Falha explícita enquanto catálogo local, credencial Inventory e regra financeira não forem acordados.
builder.Services.AddSingleton<IPriceCatalog, UnconfiguredPriceCatalog>();
builder.Services.AddScoped(services =>
{
    var configured = builder.Configuration["Pricing:ConsumableMode"];
    ConsumablePricingMode? mode = Enum.TryParse<ConsumablePricingMode>(configured, out var parsed) && Enum.IsDefined(parsed) ? parsed : null;
    return new DiagnosticPricer(services.GetRequiredService<IPriceCatalog>(), mode, services.GetRequiredService<TimeProvider>());
});
var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages(async status => await OperationErrors.Write(status.HttpContext, status.HttpContext.Response.StatusCode, "http_error"));
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" })).AllowAnonymous();
app.MapOpenApi().RequireAuthorization("Staff");
app.MapOperationEndpoints();
app.Run();

public partial class Program;
