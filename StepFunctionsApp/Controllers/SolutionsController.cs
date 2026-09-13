using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using StepFunctionsApp.Solutions;

namespace StepFunctionsApp.Controllers
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // SOLUTIONS — portable application packages (test -> prod deploy path).
    //   GET  /api/solutions/export?nodePath=org/project/sub[&seedTables=a,b]
    //        → the full solution package JSON for that workspace node.
    //   POST /api/solutions/import   { "package": {...}, "targetNodePath"? }
    //        → imports the package (flows + metadata upserts + data-source migrations) and returns a report.
    // ═══════════════════════════════════════════════════════════════════════════════

    [ApiController]
    [Route("api/solutions")]
    public class SolutionsController : ControllerBase
    {
        private readonly SolutionService _solutions;
        private readonly string _defaultNode;

        public SolutionsController(SolutionService solutions, IConfiguration config)
        {
            _solutions = solutions;
            var node = config["Workspace:DefaultNodeName"] ?? "Default";
            _defaultNode = $"{node}/{node}/{node}";
        }

        /// <summary>Exports the application attached to a workspace node as a portable solution package.</summary>
        [HttpGet("export")]
        public IActionResult Export([FromQuery] string? nodePath, [FromQuery] string? seedTables, [FromQuery] string? name, [FromQuery] string? version)
        {
            try
            {
                var seeds = string.IsNullOrWhiteSpace(seedTables)
                    ? null
                    : seedTables.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var package = _solutions.Export(string.IsNullOrWhiteSpace(nodePath) ? _defaultNode : nodePath.Trim(), seeds, name, version);
                return Ok(package);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        public sealed class ImportRequest
        {
            public SolutionPackage? Package { get; set; }
            /// <summary>Workspace node to deploy into. Defaults to the package's manifest.sourceNodePath.</summary>
            public string? TargetNodePath { get; set; }
        }

        /// <summary>Imports a solution package onto this instance (idempotent redeploy) and returns a report.</summary>
        [HttpPost("import")]
        public IActionResult Import([FromBody] ImportRequest request)
        {
            try
            {
                if (request?.Package == null) return BadRequest(new { error = "body.package is required" });
                var report = _solutions.Import(request.Package, request.TargetNodePath);
                return Ok(report);
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentNullException or InvalidOperationException)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
