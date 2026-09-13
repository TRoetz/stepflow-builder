using System.ComponentModel;
using ModelContextProtocol.Server;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using StepFunctionsApp.Solutions;

namespace StepFunctionsApp.Mcp
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // MCP SOLUTION TOOLS — portable application packages (test -> prod deploy path).
    // export_solution bundles a workspace node's flows + forms + attribute domains +
    // dynamic APIs + data-source schemas into one JSON package; import_solution applies
    // it to this instance (idempotent redeploy: upserts by identity, runs migrations).
    // ═══════════════════════════════════════════════════════════════════════════════

    [McpServerToolType]
    public class SolutionTools
    {
        private readonly SolutionService _solutions;
        private readonly string _defaultNode;

        public SolutionTools(SolutionService solutions, IConfiguration config)
        {
            _solutions = solutions;
            var node = config["Workspace:DefaultNodeName"] ?? "Default";
            _defaultNode = $"{node}/{node}/{node}";
        }

        [McpServerTool]
        [Description(
"""
Export the application attached to a workspace node as one portable solution package (JSON): its flows, every form and attribute domain those flows reference, the node's dynamic APIs, and the SQL schema of every sql:// datasource the flows use. Save the returned JSON to a file — it is the deploy artifact for another instance (import_solution).

seedTables: optional comma-separated list of table names whose rows are included as INSERT OR IGNORE seed statements (use for reference/lookup tables that must exist in prod, e.g. fee_types).
""")]
        public string ExportSolution(
            [Description("Workspace node path org/project/sub to export. Defaults to the configured default node.")] string? nodePath = null,
            [Description("Optional comma-separated table names to seed (rows dumped as INSERT OR IGNORE), e.g. 'fee_types'.")] string? seedTables = null,
            [Description("Package name for the manifest. Defaults to the sub-project name (last path segment).")]
            string? name = null,
            [Description("Package version for the manifest. Defaults to '1'.")] string? version = null)
        {
            try
            {
                var seeds = string.IsNullOrWhiteSpace(seedTables)
                    ? null
                    : seedTables.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var package = _solutions.Export(string.IsNullOrWhiteSpace(nodePath) ? _defaultNode : nodePath.Trim(), seeds, name, version);
                return JsonConvert.SerializeObject(package, McpJson.Settings);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Error(ex.Message);
            }
        }

        [McpServerTool]
        [Description(
"""
Import a solution package onto this instance — the test -> prod deploy step. Flows are saved to the target node and registered for immediate execution; schema definitions, attribute domains, forms and dynamic APIs are upserted by identity (re-importing is an idempotent redeploy); data-source migrations run in order against each sql:// datasource's bound file (the file is created when missing — configure appsettings SqlDataSources first so logical names like 'fees' point at the right database). Returns a report of everything applied.
""")]
        public string ImportSolution(
            [Description("The full solution package JSON as produced by export_solution.")] string packageJson,
            [Description("Optional workspace node path to deploy into (org/project/sub). Defaults to the package's manifest.sourceNodePath.")] string? targetNodePath = null)
        {
            try
            {
                SolutionPackage? pkg;
                try { pkg = JsonConvert.DeserializeObject<SolutionPackage>(packageJson); }
                catch (Exception ex) { return Error($"packageJson is not valid JSON: {ex.Message}"); }
                if (pkg == null) return Error("packageJson did not deserialize to a solution package.");

                var report = _solutions.Import(pkg, targetNodePath);
                return JsonConvert.SerializeObject(report, McpJson.Settings);
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentNullException or InvalidOperationException)
            {
                return Error(ex.Message);
            }
        }

        private static string Error(string message) =>
            JsonConvert.SerializeObject(new { error = message }, McpJson.Settings);
    }
}
