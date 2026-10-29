using System.Text.Json.Serialization;
using GearFlow.Operation.Api;
using GearFlow.Operation.Application;
using GearFlow.Operation.Infrastructure.Persistence;
using GearFlow.Operation.Infrastructure.Pricing;
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
builder.Services.AddScoped<IJobCatalog, PostgresJobCatalog>();
builder.Services.AddSingleton<IInventoryCredential, ConfiguredInventoryCredential>();
builder.Services.AddHttpClient<InventoryPriceClient>(http =>
{
    var address = builder.Configuration["Inventory:BaseUrl"];
    if (!string.IsNullOrWhiteSpace(address))
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && (uri.IsLoopback || builder.Configuration.GetValue<bool>("Inventory:AllowInsecureHttp")))))
            throw new InvalidOperationException("Inventory:BaseUrl requer HTTPS ou HTTP explicitamente permitido.");
        http.BaseAddress = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
    }
    http.Timeout = TimeSpan.FromSeconds(5);
}).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<IPriceCatalog, CompositePriceCatalog>();
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
app.MapCatalogEndpoints();
app.Run();

public partial class Program;
