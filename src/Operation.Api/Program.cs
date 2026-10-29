using GearFlow.Operation.Api;
using GearFlow.Operation.Application;
using GearFlow.Operation.Infrastructure;

if (args is ["--health-probe"])
{
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        using var response = await client.GetAsync("http://127.0.0.1:8080/health/ready");
        Environment.ExitCode = response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (HttpRequestException) { Environment.ExitCode = 1; }
    catch (OperationCanceledException) { Environment.ExitCode = 1; }
    return;
}
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddOperationApplication(provider => provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsSnapshot<PricingOptions>>().Value.Mode);
builder.Services.AddOperationInfrastructure(builder.Configuration);
builder.AddOperationApi();
var app = builder.Build();
app.UseRouting();
app.UseMiddleware<OperationRequestLogging>();
app.UseExceptionHandler();
app.UseStatusCodePages(async status => await OperationErrors.Write(status.HttpContext, status.HttpContext.Response.StatusCode, "http_error"));
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" })).AllowAnonymous();
OperationReadiness.Map(app);
app.MapOperationDocumentation();
app.MapOperationEndpoints();
app.MapCatalogEndpoints();
app.Run();

public partial class Program;
