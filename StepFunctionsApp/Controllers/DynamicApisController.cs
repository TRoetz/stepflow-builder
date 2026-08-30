using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using StepFlow.DynamicApi;
using StepFunctionsApp.DynamicApi;
using StepFunctionsApp.Workspace;

namespace StepFunctionsApp.Controllers
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // DYNAMIC APIS — CRUD for user-defined REST endpoint definitions. The request
    // surface itself is served by DynamicApiDispatcher under /api/dynamic; this
    // controller only manages the definitions (and validates them before save).
    // Save validation order: name/nodePath -> basePath/operations -> route conflicts.
    // ═══════════════════════════════════════════════════════════════════════════════

    [ApiController]
    public class DynamicApisController : ControllerBase
    {
        private static readonly string[] AllowedMethods = { "GET", "POST", "PUT", "PATCH", "DELETE" };
        private static readonly string[] HandlerTypes = { "flow", "attributeDomain", "eav", "dataExchange" };
        private static readonly Regex TemplateParamRegex = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

        private readonly IDynamicApiStore _store;
        private readonly WorkspaceStore _workspace;

        public DynamicApisController(IDynamicApiStore store, WorkspaceStore workspace)
        {
            _store = store;
            _workspace = workspace;
        }

        // All active APIs, optionally filtered by workspace node (segment-aware prefix) and published state.
        [HttpGet("api/dynamic/apis")]
        public IActionResult List([FromQuery] string? nodePath, [FromQuery] bool? published) =>
            Ok(published == null ? _store.GetAll(nodePath) : _store.GetAll(nodePath).Where(a => a.IsPublished == published.Value));

        // A single API by id (active or not), so inactive ones can be re-saved as active.
        [HttpGet("api/dynamic/apis/{id}")]
        public IActionResult Get(string id)
        {
            var def = _store.GetById(id);
            return def == null ? NotFound(new { error = $"Dynamic API '{id}' not found" }) : Ok(def);
        }

        // Delete by id.
        [HttpDelete("api/dynamic/apis/{id}")]
        public IActionResult Delete(string id) =>
            _store.Delete(id) ? Ok(new { deleted = true, id }) : NotFound(new { error = $"Dynamic API '{id}' not found" });

        // Create or update (explicit id, or same Name+NodePath reuses its id). 201 when created.
        [HttpPost("api/dynamic/apis")]
        public IActionResult Save([FromBody] JObject payload)
        {
            // ── 1. name + nodePath ────────────────────────────────────────────────────
            var name = (string?)payload["name"] ?? "";
            if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { error = "name is required" });

            var nodePath = (string?)payload["nodePath"] ?? "";
            var segments = nodePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length < 1 || segments.Length > 3)
                return BadRequest(new { error = "nodePath must be 'org', 'org/project' or 'org/project/sub'" });
            foreach (var seg in segments)
                if (seg is "." or ".." || seg.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    return BadRequest(new { error = $"nodePath segment '{seg}' is not a valid workspace node name" });

            if (!_workspace.NodeExists(nodePath))
                return NotFound(new { error = $"Workspace node '{nodePath}' not found" });

            // ── 2. basePath + operations ──────────────────────────────────────────────
            var basePath = (string?)payload["basePath"] ?? "/";
            if (!basePath.StartsWith('/') || basePath.Contains(' '))
                return BadRequest(new { error = "basePath must start with '/' and contain no spaces" });

            if (payload["operations"] is not JArray ops || ops.Count == 0)
                return BadRequest(new { error = "operations must be a non-empty array" });

            var def = new DynamicApiDefinition
            {
                Id = (string?)payload["id"] ?? "",
                Name = name.Trim(),
                Description = (string?)payload["description"] ?? "",
                NodePath = nodePath,
                BasePath = DynamicApiMatcher.Normalize(basePath),
                AttributeDomain = string.IsNullOrWhiteSpace((string?)payload["attributeDomain"]) ? null : ((string)payload["attributeDomain"]).Trim(),
                BearerToken = (string?)payload["bearerToken"], // empty/null = open access
                IsActive = payload["isActive"]?.Type == JTokenType.Boolean ? (bool)payload["isActive"]! : true,
                IsPublished = payload["isPublished"]?.Type == JTokenType.Boolean ? (bool)payload["isPublished"]! : false,
            };

            var seenRoutes = new HashSet<(string Method, string Path)>();
            for (int i = 0; i < ops.Count; i++)
            {
                if (ops[i] is not JObject opObj) return BadRequest(new { error = $"operations[{i}] must be an object" });

                var op = new DynamicApiOperation
                {
                    Method = ((string?)opObj["method"] ?? "").Trim().ToUpperInvariant(),
                    Path = (string?)opObj["path"] ?? "",
                    HandlerType = ((string?)opObj["handlerType"] ?? "").Trim(),
                    FlowId = string.IsNullOrWhiteSpace((string?)opObj["flowId"]) ? null : ((string)opObj["flowId"]).Trim(),
                    DomainName = string.IsNullOrWhiteSpace((string?)opObj["domainName"]) ? null : ((string)opObj["domainName"]).Trim(),
                    ProfileId = string.IsNullOrWhiteSpace((string?)opObj["profileId"]) ? null : ((string)opObj["profileId"]).Trim(),
                    Description = (string?)opObj["description"],
                };

                if (!AllowedMethods.Contains(op.Method))
                    return BadRequest(new { error = $"operations[{i}].method must be one of GET, POST, PUT, PATCH, DELETE" });
                if (op.Path != "" && !op.Path.StartsWith('/'))
                    return BadRequest(new { error = $"operations[{i}].path must be empty or start with '/'" });
                var fullPath = DynamicApiMatcher.JoinPaths(basePath, op.Path);
                if (fullPath == "/apis" || fullPath.StartsWith("/apis/", StringComparison.Ordinal))
                    return BadRequest(new { error = "basePath cannot use the reserved '/apis' management prefix" });

                foreach (var seg in DynamicApiMatcher.PathSegments(DynamicApiMatcher.JoinPaths(basePath, op.Path)))
                    if (DynamicApiMatcher.IsTemplate(seg) && !TemplateParamRegex.IsMatch(seg[1..^1]))
                        return BadRequest(new { error = $"operations[{i}].path template parameter '{seg}' must match ^[A-Za-z_][A-Za-z0-9_]*$" });

                var canonicalHandler = HandlerTypes.FirstOrDefault(t => string.Equals(t, op.HandlerType, StringComparison.OrdinalIgnoreCase));
                if (canonicalHandler == null)
                    return BadRequest(new { error = $"operations[{i}].handlerType must be one of flow, attributeDomain, eav, dataExchange" });
                op.HandlerType = canonicalHandler;

                switch (op.HandlerType)
                {
                    case "flow":
                        if (string.IsNullOrWhiteSpace(op.FlowId))
                            return BadRequest(new { error = $"operations[{i}].flowId is required for handlerType 'flow'" });
                        break;
                    case "dataExchange":
                        if (string.IsNullOrWhiteSpace(op.ProfileId))
                            return BadRequest(new { error = $"operations[{i}].profileId is required for handlerType 'dataExchange'" });
                        break;
                    case "attributeDomain":
                        if (string.IsNullOrWhiteSpace(op.DomainName ?? def.AttributeDomain))
                            return BadRequest(new { error = $"operations[{i}] needs a domain: set operations[{i}].domainName or the api-level attributeDomain" });
                        break;
                    case "eav":
                        if (string.IsNullOrWhiteSpace(op.DomainName ?? def.AttributeDomain))
                            return BadRequest(new { error = $"operations[{i}] needs a domain: set operations[{i}].domainName or the api-level attributeDomain" });
                        if (op.Method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                            DynamicApiMatcher.PathSegments(DynamicApiMatcher.JoinPaths(basePath, op.Path)).Count(s => DynamicApiMatcher.IsTemplate(s)) > 1)
                            return BadRequest(new { error = $"operations[{i}].path supports at most one {{param}} for eav GET operations" });
                        break;
                }

                // Duplicate routes inside this payload would be ambiguous at match time.
                var routeKey = (op.Method, DynamicApiMatcher.JoinPaths(basePath, op.Path));
                if (!seenRoutes.Add(routeKey))
                    return BadRequest(new { error = $"Route conflict: {routeKey.Method} {routeKey.Item2} is defined more than once in this API" });

                def.Operations.Add(op);
            }

            // ── 3. Route conflicts with other active APIs (same method + full path) ───
            // Resolve the row this save will update so its own routes don't count as a conflict:
            // an explicit id, or - like SqliteDynamicApiStore.Save - the existing API whose
            // Name+NodePath matches (case-insensitive name), whose id the store reuses.
            var selfId = string.IsNullOrWhiteSpace(def.Id) ? null : def.Id;
            if (selfId == null)
                selfId = _store.GetAll().FirstOrDefault(a =>
                    string.Equals(a.Name, def.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(a.NodePath, def.NodePath ?? "", StringComparison.Ordinal))?.Id;
            foreach (var other in _store.GetAll())
            {
                if (selfId != null && string.Equals(other.Id, selfId, StringComparison.Ordinal)) continue;
                foreach (var oop in other.Operations)
                    foreach (var op in def.Operations)
                        if (string.Equals(oop.Method, op.Method, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(DynamicApiMatcher.JoinPaths(other.BasePath, oop.Path), DynamicApiMatcher.JoinPaths(def.BasePath, op.Path), StringComparison.Ordinal))
                            return Conflict(new { error = $"Route conflict: {op.Method} {DynamicApiMatcher.JoinPaths(def.BasePath, op.Path)} already defined by api '{other.Id}'" });
            }

            var (id, created) = _store.Save(def);
            return created ? StatusCode(201, new { id, created }) : Ok(new { id, created });
        }
    }
}
