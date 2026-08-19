using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using StepFunctionsApp.StepFunctions;
using StepFunctionsApp.Controllers;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace StepFunctionsApp;

public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
                webBuilder.UseUrls("http://localhost:5001");
            });
}
public class Startup
{
    private readonly IConfiguration _config;

    public Startup(IConfiguration config) => _config = config;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllersWithViews()
            .AddNewtonsoftJson();
        services.AddRazorPages();

        services.AddHttpClient();

        // Register EAV Registry Service
        var eavRegistry = new EavRegistryService();
        eavRegistry.Initialize("eav_registry.json");
        services.AddSingleton(eavRegistry);

        // StepFlow Services
        services.AddSingleton<RuleEngineService>();
        services.AddSingleton<MicrosoftRulesEngineService>();
        services.AddSingleton<DuckDbTransformService>();
        services.AddSingleton<ScriptExecutionService>();
        services.AddSingleton<AiDecisionService>();
        
        services.AddSingleton<IResourceInvoker, CompositeResourceInvoker>();
        services.AddSingleton<StepFunctionInterpreter>();
        services.AddSingleton<BpmnConverter>();

        // Register StepFunctionService as both singleton (for Controller injection) and HostedService (to run background executions)
        services.AddSingleton<StepFunctionService>();
        services.AddHostedService(provider => provider.GetRequiredService<StepFunctionService>());

        // Register Lazy<StepFunctionService> for CompositeResourceInvoker circular dependency resolution
        services.AddTransient<Lazy<StepFunctionService>>(provider =>
            new Lazy<StepFunctionService>(() => provider.GetRequiredService<StepFunctionService>()));

        // Durable flow-state store (disk by default; "redis" via the FlowState section in appsettings.json)
        services.Configure<FlowStateOptions>(_config.GetSection(FlowStateOptions.SectionName));
        services.AddSingleton<IFlowStateStore>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<FlowStateOptions>>().Value;
            return options.Provider.Equals("redis", StringComparison.OrdinalIgnoreCase)
                ? new RedisFlowStateStore(options.RedisConnection, provider.GetService<ILogger<RedisFlowStateStore>>())
                : new DiskFlowStateStore(options.DiskPath, provider.GetService<ILogger<DiskFlowStateStore>>());
        });

        // MCP (Model Context Protocol) endpoint for AI harnesses — see Mcp/FlowTools.cs.
        services.AddMcpServer()
            .WithHttpTransport()
            .WithTools<StepFunctionsApp.Mcp.FlowTools>();
    }
    public void Configure(IApplicationBuilder app, IHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        
        // Serve static files (JS, CSS) from dist/ directory
        var distPath = Path.Combine(Directory.GetCurrentDirectory(), "dist");
        if (Directory.Exists(distPath))
        {
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(distPath),
                RequestPath = ""
            });
        }
        
        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            // MCP (Model Context Protocol) endpoint for AI harnesses (Streamable HTTP).
            endpoints.MapMcp("/mcp");

            // Health check: liveness + flow-state store status (operators use this to detect a dead backend)
            endpoints.Map("/api/health", async context =>
            {
                var stepService = context.RequestServices.GetRequiredService<StepFunctionService>();
                var store = context.RequestServices.GetRequiredService<IFlowStateStore>();
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonConvert.SerializeObject(new
                {
                    status = "healthy",
                    runningExecutions = stepService.RunningExecutionCount,
                    registeredFlows = stepService.ListStateMachines().Count,
                    flowStateProvider = store.ProviderName
                }));
            });
            
            endpoints.MapControllerRoute(
                name: "default",
                pattern: "{controller}/{action=Index}/{id?}");
            
            // Serve Step Functions Builder on root path
            var builderHtml = GetBuilderHtml(distPath);
            endpoints.Map("/", context =>
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.WriteAsync(builderHtml);
                return Task.CompletedTask;
            });
        });
    }

    private string GetBuilderHtml(string distPath)
    {
        string indexPath = Path.Combine(distPath, "index.html");
        if (File.Exists(indexPath))
        {
            return File.ReadAllText(indexPath);
        }
        
        return "<html><body>Builder HTML not found - please run 'npm run build' first</body></html>";
    }
}
