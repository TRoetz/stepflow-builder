using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using StepFunctionsApp.StepFunctions;

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
