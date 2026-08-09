using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

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
            });
}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddControllersWithViews();
        services.AddRazorPages();
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
