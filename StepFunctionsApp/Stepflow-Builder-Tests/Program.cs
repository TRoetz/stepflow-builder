using Microsoft.AspNetCore.Mvc;
using StepFunctionsApp.StepFunctions;
using Stepflow_Builder_Tests.Controllers;

namespace Stepflow_Builder_Tests;

/// <summary>
/// Standalone host for the fake test APIs (Controllers/FakeDataController.cs + FakeCommerceController.cs).
/// Dev runs bind http://localhost:5095 so flow JSONs can reference it directly; tests host this same
/// entry point via WebApplicationFactory&lt;Stepflow_Builder_Tests.Program&gt;. Classic Main style (no top-level
/// statements) keeps the entry-point type namespaced, so test code referencing both apps never sees an
/// ambiguous global Program.
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Dev runs bind the fixed port that flow JSONs reference (http://localhost:5095/api/fake/...).
        builder.WebHost.UseUrls("http://localhost:5095");

        builder.Services.AddControllers().AddNewtonsoftJson();

        // In-memory state for the fake commerce APIs (inventory, payments, receipts, email outbox).
        builder.Services.AddSingleton<FakeCommerceStore>();

        // Engine services backing the /api/tests self-test endpoints.
        builder.Services.AddSingleton<DuckDbTransformService>();
        builder.Services.AddSingleton<ScriptExecutionService>();
        builder.Services.AddSingleton<RuleEngineService>();
        builder.Services.AddSingleton<MicrosoftRulesEngineService>();

        var app = builder.Build();
        app.MapControllers();
        app.Run();
    }
}
