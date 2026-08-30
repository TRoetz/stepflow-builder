using Microsoft.Extensions.Options;
using StepFlow.DynamicApi.Host;

namespace StepFlow.DynamicApi.Host;

// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// HOST ENTRY POINT - explicit Main (deliberately NOT top-level statements): the
// shared test project references both this app and StepFunctionsApp, and a second
// global Program class would make bare `Program` ambiguous in every existing test.
// HostProgram doubles as the WebApplicationFactory marker for DynamicApiHost tests.
// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

public sealed class HostProgram
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.Configure<EngineOptions>(builder.Configuration.GetSection(EngineOptions.SectionName));
        builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection(SyncOptions.SectionName));
        builder.Services.AddHttpClient(EngineOptions.ClientName, (sp, client) =>
        {
            var engine = sp.GetRequiredService<IOptions<EngineOptions>>().Value;
            client.BaseAddress = new Uri(engine.BaseUrl.TrimEnd('/') + "/"); // must be absolute
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, engine.TimeoutSeconds));
        });
        builder.Services.AddSingleton<CatalogService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<CatalogService>()); // IHostedService: initial pull + periodic polling
        builder.Services.AddSingleton<HostDynamicApiDispatcher>();

        var app = builder.Build();

        app.Map("/health", (CatalogService catalog) =>
        {
            if (!catalog.IsLoaded) return Results.Json(new { status = "waiting-for-catalog" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            return Results.Ok(new { status = "ready", apisLoaded = catalog.Apis.Count, lastSyncUtc = catalog.LastSyncUtc });
        });

        // OpenAPI document for the dynamic APIs - proxied from the engine (its generator needs the attribute-domain store).
        app.Map("/api/dynamic/openapi.json", async (HttpContext context, IHttpClientFactory httpFactory) =>
        {
            var client = httpFactory.CreateClient(EngineOptions.ClientName);
            try
            {
                using var response = await client.GetAsync("api/dynamic/openapi.json", context.RequestAborted);
                context.Response.StatusCode = (int)response.StatusCode;
                context.Response.ContentType = "application/json";
                await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !context.RequestAborted.IsCancellationRequested))
            {
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status502BadGateway;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync($"{{\"error\":\"engine unavailable: {ex.Message}\"}}", context.RequestAborted);
                }
            }
        });

        app.Map("/api/dynamic/{**rest}", async (HttpContext context, HostDynamicApiDispatcher dispatcher) => await dispatcher.HandleAsync(context));

        await app.RunAsync();
    }
}
