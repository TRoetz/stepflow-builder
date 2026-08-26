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
using Newtonsoft.Json.Serialization;
using StepFunctionsApp.DataExchange;
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
            .AddNewtonsoftJson(options =>
                options.SerializerSettings.ContractResolver = new KeyPreservingCamelCaseContractResolver());
        services.AddRazorPages();

        services.AddHttpClient();

        // Register EAV Registry Service
        var eavRegistry = new EavRegistryService();
        eavRegistry.Initialize("eav_registry.json");
        services.AddSingleton(eavRegistry);
        // Rule-addressable EAV entities = registry ∪ attribute domains (domain store wins on name collision).
        services.AddSingleton<IEavEntityProvider, CompositeEavEntityProvider>();
        // Register SSH host inventory (curated remote hosts for ssh:// resources)
        var sshHostStore = new SshHostStore();
        sshHostStore.Initialize("ssh_hosts.json");
        services.AddSingleton(sshHostStore);

        // Data Exchange subsystem - customer file -> internal schema pipeline (profiles + executor)
        services.Configure<DataExchangeOptions>(_config.GetSection(DataExchangeOptions.SectionName));
        services.AddSingleton(provider => new DataExchangeProfileStore(
            provider.GetRequiredService<IOptions<DataExchangeOptions>>().Value.ProfilesDirectory));
        services.AddSingleton<DataExchangeExecutor>();
        services.AddSingleton<DataExchangeExecutionLog>();
        services.AddHostedService<DataExchangeFileMonitorService>();

        // StepFlow Services
        services.AddSingleton<RuleEngineService>();
        services.AddSingleton<MicrosoftRulesEngineService>();
        services.AddSingleton<DuckDbTransformService>();
        services.AddSingleton<ScriptExecutionService>();
        services.AddSingleton<AiDecisionService>();
        services.AddSingleton<SshCommandService>();
        services.AddSingleton<FetchRemoteFilesService>();
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

        // Human task subsystem — disk store, completion providers and the polling monitor (see StepFunctions/HumanTasks.cs)
        services.Configure<HumanTaskOptions>(_config.GetSection(HumanTaskOptions.SectionName));
        services.AddSingleton<IHumanTaskStore>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<HumanTaskOptions>>().Value;
            return new DiskHumanTaskStore(options.DiskPath, provider.GetService<ILogger<DiskHumanTaskStore>>());
        });
        services.AddSingleton<ApiCompletionProvider>();
        services.AddSingleton<FileMonitorCompletionProvider>();
        services.AddSingleton<IHumanTaskCompletionProvider>(provider => provider.GetRequiredService<ApiCompletionProvider>());
        services.AddSingleton<IHumanTaskCompletionProvider>(provider => provider.GetRequiredService<FileMonitorCompletionProvider>());
        services.AddHostedService<HumanTaskCompletionMonitorService>();

        // Form capture subsystem — provider-backed form definitions + attribute domains (FormData section in appsettings.json)
        services.Configure<FormDataOptions>(_config.GetSection(FormDataOptions.SectionName));
        services.AddSingleton<IFormDefinitionStore>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<FormDataOptions>>().Value;
            return options.Provider.Equals("sqlite", StringComparison.OrdinalIgnoreCase)
                ? new SqliteFormDefinitionStore(options.DatabasePath, provider.GetService<ILogger<SqliteFormDefinitionStore>>())
                : new JsonFileFormDefinitionStore("forms", provider.GetService<ILogger<JsonFileFormDefinitionStore>>());
        });
        services.AddSingleton<IAttributeDomainStore>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<FormDataOptions>>().Value;
            return options.Provider.Equals("sqlite", StringComparison.OrdinalIgnoreCase)
                ? new SqliteAttributeDomainStore(options.DatabasePath, provider.GetService<ILogger<SqliteAttributeDomainStore>>())
                : new JsonFileAttributeDomainStore("attribute_domains.json", provider.GetService<ILogger<JsonFileAttributeDomainStore>>());
        });
        services.AddSingleton<ISchemaDefinitionStore>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<FormDataOptions>>().Value;
            return options.Provider.Equals("sqlite", StringComparison.OrdinalIgnoreCase)
                ? new SqliteSchemaDefinitionStore(options.DatabasePath, provider.GetService<ILogger<SqliteSchemaDefinitionStore>>())
                : new JsonFileSchemaDefinitionStore(options.SchemasFile, provider.GetService<ILogger<JsonFileSchemaDefinitionStore>>());
        });
        // Captured form rows stay file-based (eav-data/) per the ask — mirrors the EAV registry registration above.
        var eavRowStore = new EavRowStore();
        eavRowStore.Initialize("eav-data");
        services.AddSingleton(eavRowStore);

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
        
        // Serve wwwroot (standalone form-capture page) even when dist/ doesn't exist.
        app.UseStaticFiles();
        
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
                var formStore = context.RequestServices.GetRequiredService<IFormDefinitionStore>();
                var domainStore = context.RequestServices.GetRequiredService<IAttributeDomainStore>();
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonConvert.SerializeObject(new
                {
                    status = "healthy",
                    runningExecutions = stepService.RunningExecutionCount,
                    registeredFlows = stepService.ListStateMachines().Count,
                    flowStateProvider = store.ProviderName,
                    formDataProvider = formStore.ProviderName + "/" + domainStore.ProviderName
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

    /// <summary>
    /// camelCase property names, but leaves dictionary keys (state names) untouched.
    /// </summary>
    private sealed class KeyPreservingCamelCaseContractResolver : CamelCasePropertyNamesContractResolver
    {
        protected override string ResolveDictionaryKey(string key) => key;
    }
}
