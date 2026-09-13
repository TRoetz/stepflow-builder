using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.DataExchange;
using StepFunctionsApp.StepFunctions;
using StepFunctionsApp.Workspace;

namespace StepFunctionsApp.Controllers
{
    /// <summary>
    /// Workspace hierarchy API: org → project → sub-project tree with per-node ACLs, plus flow and
    /// data-exchange-profile access under the selected sub-project. All handlers map failures to
    /// BadRequest({ error }) (FlowsController convention); unknown nodes return 404.
    /// </summary>
    [ApiController]
    public class WorkspaceController : ControllerBase
    {
        private readonly WorkspaceStore _store;
        private readonly IWorkspaceAccessService _access;
        private readonly DataExchangeProfileStore _profiles;
        private readonly StepFunctionService _stepService;

        public WorkspaceController(
            WorkspaceStore store,
            IWorkspaceAccessService access,
            DataExchangeProfileStore profiles,
            StepFunctionService stepService)
        {
            _store = store;
            _access = access;
            _profiles = profiles;
            _stepService = stepService;
        }

        // ── Tree ────────────────────────────────────────────────────────────────────────────────

        /// <summary>Full tree: orgs → projects → sub-projects (each with flow refs + profile ids), plus unassignedProfiles.</summary>
        [HttpGet("api/workspace")]
        public IActionResult GetTree()
        {
            try
            {
                var organizedProfileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var orgs = new List<OrgNodeDto>();

                foreach (var org in _store.ListOrgs())
                {
                    var orgDto = new OrgNodeDto { Name = org };
                    foreach (var project in _store.ListProjects(org))
                    {
                        var projectDto = new ProjectNodeDto { Name = project };
                        foreach (var sub in _store.ListSubProjects(org, project))
                        {
                            var subPath = WorkspaceStore.Join(org, project, sub);
                            var subDto = new SubProjectNodeDto
                            {
                                Name = sub,
                                Flows = _store.ListFlows(subPath)
                                    .Select(f => new FlowRefDto { Id = f.Id, Name = f.Meta.Name })
                                    .ToList(),
                                ProfileIds = _store.ListProfiles(subPath).ToList()
                            };
                            foreach (var profileId in subDto.ProfileIds) organizedProfileIds.Add(profileId);
                            projectDto.SubProjects.Add(subDto);
                        }
                        orgDto.Projects.Add(projectDto);
                    }
                    orgs.Add(orgDto);
                }

                // Profiles not found under any sub-project (unassigned bucket + legacy leftovers).
                var unassignedProfiles = _profiles.LoadAll()
                    .Select(p => DataExchangeProfileStore.ResolveId(p))
                    .Where(id => !organizedProfileIds.Contains(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return Ok(new { orgs, unassignedProfiles });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // ── Node CRUD ───────────────────────────────────────────────────────────────────────────

        /// <summary>Creates a node: { name, parentPath? } → { path }. Parent must exist when given.</summary>
        [HttpPost("api/workspace/nodes")]
        public IActionResult CreateNode([FromBody] JObject payload)
        {
            try
            {
                var name = payload["name"]?.ToString();
                if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { error = "'name' is required" });

                var parentPath = payload["parentPath"]?.ToString();
                if (!string.IsNullOrWhiteSpace(parentPath) && !_store.NodeExists(parentPath))
                    return NotFound(new { error = $"Parent node '{parentPath}' not found" });

                var path = _store.CreateNode(name, string.IsNullOrWhiteSpace(parentPath) ? null : parentPath);
                return Ok(new { path });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>Renames a node in place: { path, newName } → { path: newPath }. Children + ACLs move with it.</summary>
        [HttpPost("api/workspace/nodes/rename")]
        public IActionResult RenameNode([FromBody] JObject payload)
        {
            try
            {
                var path = payload["path"]?.ToString();
                var newName = payload["newName"]?.ToString();
                if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(newName))
                    return BadRequest(new { error = "'path' and 'newName' are required" });
                if (!_store.NodeExists(path)) return NotFound(new { error = $"Workspace node '{path}' not found" });

                var newPath = _store.RenameNode(path, newName);
                return Ok(new { path = newPath });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>Recursively deletes a node: ?path=… → 404 when missing.</summary>
        [HttpDelete("api/workspace/nodes")]
        public IActionResult DeleteNode([FromQuery] string? path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return BadRequest(new { error = "'path' is required" });
                if (!_store.NodeExists(path)) return NotFound(new { error = $"Workspace node '{path}' not found" });

                _store.DeleteNode(path);
                return Ok(new { path, deleted = true });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // ── Access control ──────────────────────────────────────────────────────────────────────

        /// <summary>Local + effective grants for a node: ?path=… → 404 unknown node.</summary>
        [HttpGet("api/workspace/access")]
        public IActionResult GetAccess([FromQuery] string? path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return BadRequest(new { error = "'path' is required" });
                if (!_store.NodeExists(path)) return NotFound(new { error = $"Workspace node '{path}' not found" });

                var local = _store.LoadAcl(path).Entries;
                var effective = _access.GetEffectiveGrants(path);
                return Ok(new { local, effective });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>Saves a node's local ACL: ?path=… with body { entries: [{ principal, role }] } → saved local.</summary>
        [HttpPut("api/workspace/access")]
        public IActionResult SaveAccess([FromQuery] string? path, [FromBody] JObject payload)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return BadRequest(new { error = "'path' is required" });
                if (!_store.NodeExists(path)) return NotFound(new { error = $"Workspace node '{path}' not found" });

                var entries = (payload["entries"] as JArray)?
                    .Select(t => new AccessEntry
                    {
                        Principal = t["principal"]?.ToString() ?? "",
                        Role = t["role"]?.ToString() ?? ""
                    })
                    .ToList() ?? new List<AccessEntry>();

                _store.SaveAcl(path, entries);
                return Ok(new { local = _store.LoadAcl(path).Entries });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // ── Flows under a sub-project ───────────────────────────────────────────────────────────

        /// <summary>Lists flows of a sub-project (id/name/description/timestamps).</summary>
        [HttpGet("api/workspace/subprojects/{org}/{project}/{sub}/flows")]
        public IActionResult ListFlows(string org, string project, string sub)
        {
            try
            {
                var subPath = WorkspaceStore.Join(org, project, sub);
                if (!_store.NodeExists(subPath)) return NotFound(new { error = $"Sub-project '{sub}' not found" });

                return Ok(_store.ListFlows(subPath).Select(f => new
                {
                    id = f.Id,
                    name = f.Meta.Name,
                    description = f.Meta.Description,
                    createdAt = f.Meta.CreatedAt,
                    updatedAt = f.Meta.UpdatedAt
                }));
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>
        /// Saves a flow to a sub-project. Payload mirrors POST /api/flows: 'states' or nested
        /// definition.states, optional startAt, required name, optional id. Persists normalized
        /// camelCase { startAt, states } as flow.json + meta.json and registers the state machine so
        /// it is immediately executable (upsert by name — same as the existing endpoint).
        /// </summary>
        [HttpPost("api/workspace/subprojects/{org}/{project}/{sub}/flows")]
        public IActionResult SaveFlow(string org, string project, string sub, [FromBody] JObject payload)
        {
            try
            {
                var subPath = WorkspaceStore.Join(org, project, sub);
                if (!_store.NodeExists(subPath)) return NotFound(new { error = $"Sub-project '{sub}' not found" });

                var name = payload["name"]?.ToString();
                if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { error = "'name' is required" });
                var description = payload["description"]?.ToString();

                // Parse definition — same shapes as POST /api/flows.
                var statesObj = payload["states"] as JObject ?? payload["definition"]?["states"] as JObject;
                if (statesObj == null)
                    return BadRequest(new { error = "Payload must contain 'states' dictionary (Amazon States Language format)" });

                var startAt = payload["startAt"]?.ToString()
                    ?? payload["definition"]?["startAt"]?.ToString()
                    ?? statesObj.Properties().FirstOrDefault()?.Name ?? "";

                // Persist the normalized camelCase document exactly as the UI exports it (canvas layout included when present).
                var definitionDoc = new JObject { ["startAt"] = startAt, ["states"] = statesObj };
                if (payload["canvas"] is JToken canvas && canvas.Type == JTokenType.Object)
                    definitionDoc["canvas"] = canvas;
                var definitionJson = definitionDoc.ToString(Newtonsoft.Json.Formatting.None);
                var (id, created) = _store.SaveFlow(subPath, payload["id"]?.ToString(), name, description, definitionJson);

                // Register so the flow is immediately executable (upsert by name — same as POST /api/flows).
                var definition = new StateMachineDefinition
                {
                    StartAt = startAt,
                    States = statesObj.ToObject<Dictionary<string, StateDefinition>>()!
                };
                _stepService.RegisterStateMachine(name, definition, description, id);

                return Ok(new { id, created });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>Gets a single flow: { id, name, description, createdAt, updatedAt, definition }. 404 when missing.</summary>
        [HttpGet("api/workspace/subprojects/{org}/{project}/{sub}/flows/{flowId}")]
        public IActionResult GetFlow(string org, string project, string sub, string flowId)
        {
            try
            {
                var subPath = WorkspaceStore.Join(org, project, sub);
                if (!_store.NodeExists(subPath)) return NotFound(new { error = $"Sub-project '{sub}' not found" });

                var definitionJson = _store.LoadFlowDefinition(subPath, flowId);
                if (definitionJson == null) return NotFound(new { error = $"Flow '{flowId}' not found" });

                var meta = _store.ListFlows(subPath).FirstOrDefault(f => f.Id == flowId).Meta;
                return Ok(new
                {
                    id = flowId,
                    name = meta.Name,
                    description = meta.Description,
                    createdAt = meta.CreatedAt,
                    updatedAt = meta.UpdatedAt,
                    definition = JObject.Parse(definitionJson)
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>Deletes a flow from a sub-project. 404 when missing.</summary>
        [HttpDelete("api/workspace/subprojects/{org}/{project}/{sub}/flows/{flowId}")]
        public IActionResult DeleteFlow(string org, string project, string sub, string flowId)
        {
            try
            {
                var subPath = WorkspaceStore.Join(org, project, sub);
                if (!_store.NodeExists(subPath)) return NotFound(new { error = $"Sub-project '{sub}' not found" });

                if (!_store.DeleteFlow(subPath, flowId)) return NotFound(new { error = $"Flow '{flowId}' not found" });
                return Ok(new { id = flowId, deleted = true });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
