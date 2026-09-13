using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using StepFlow.DynamicApi;
using StepFunctionsApp.DynamicApi;

namespace StepFunctionsApp.Mcp
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // MCP DYNAMIC API TOOLS — manage user-defined REST endpoints attached to workspace
    // nodes. Each operation dispatches to a flow, attribute domain, EAV store or
    // data-exchange profile; published APIs are exposed on the external node ports.
    // ═══════════════════════════════════════════════════════════════════════════════

    [McpServerToolType]
    public class DynamicApiTools
    {
        private readonly IDynamicApiStore _store;

        public DynamicApiTools(IDynamicApiStore store) => _store = store;

        [McpServerTool]
        [Description("List all active dynamic API definitions. Returns id, name, nodePath, basePath, handler summary and published/active flags for each API. nodePathPrefix filters to one workspace node or its subtree (e.g. 'org/project').")]
        public string ListDynamicApis([Description("Optional workspace node path prefix; omit for all APIs.")] string? nodePathPrefix = null)
        {
            var entries = _store.GetAll(nodePathPrefix).Select(a => new
            {
                a.Id,
                a.Name,
                a.NodePath,
                a.BasePath,
                a.AttributeDomain,
                a.IsActive,
                a.IsPublished,
                operations = a.Operations.Select(o => new { o.Method, o.Path, o.HandlerType })
            });
            return JsonConvert.SerializeObject(entries, McpJson.Settings);
        }

        [McpServerTool]
        [Description("Get a dynamic API's full JSON definition (camelCase) including all operations. Accepts the API id. Use this before save_dynamic_api to fetch-then-modify an existing API.")]
        public string GetDynamicApi([Description("API id")] string id)
        {
            var api = _store.GetById(id);
            if (api == null) return Error($"Dynamic API '{id}' not found. Use list_dynamic_apis to see available APIs.");
            return JsonConvert.SerializeObject(api, McpJson.Settings);
        }

        [McpServerTool]
        [Description(
"""
Create a new dynamic API or replace an existing one (upsert). Returns the stored id and whether it was created.
definitionJson is the full definition JSON in camelCase — fetch an existing API first and modify it, or build a new one:
{"name":"orders-api","nodePath":"org/project","basePath":"/orders","attributeDomain":"orders","operations":[{"method":"GET","path":"","handlerType":"eav"},{"method":"POST","path":"","handlerType":"flow","flowId":"order-intake"}]}
nodePath is the workspace node "org" | "org/project" | "org/project/sub". basePath must start with '/' (no trailing '/' unless exactly '/'). handlerType is one of: flow, attributeDomain, eav, dataExchange — each needs its matching reference (flowId / domainName / profileId).
Set bearerToken to require "Authorization: Bearer <token>" on requests; set isPublished to expose the API on external node ports.
""")]
        public string SaveDynamicApi([Description("Full dynamic API definition JSON in camelCase (see tool description for shape)")] string definitionJson)
        {
            DynamicApiDefinition def;
            try
            {
                def = JsonConvert.DeserializeObject<DynamicApiDefinition>(definitionJson, McpJson.Settings);
            }
            catch (Exception ex)
            {
                return Error($"definitionJson is not valid JSON: {ex.Message}");
            }
            if (def == null || string.IsNullOrWhiteSpace(def.Name))
            {
                return Error("definitionJson must contain a non-empty name.");
            }

            try
            {
                var (id, created) = _store.Save(def);
                return JsonConvert.SerializeObject(new { id, created }, McpJson.Settings);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return Error(ex.Message);
            }
        }

        [McpServerTool]
        [Description("Delete a dynamic API by id. Returns whether it existed and was removed.")]
        public string DeleteDynamicApi([Description("API id to remove")] string id)
        {
            var deleted = _store.Delete(id);
            return JsonConvert.SerializeObject(new { deleted }, McpJson.Settings);
        }

        private static string Error(string message) => JsonConvert.SerializeObject(new { error = message });
    }
}
