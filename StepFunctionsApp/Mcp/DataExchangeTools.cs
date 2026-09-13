using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFlow.DataModel.Entities.DataSource;
using StepFunctionsApp.DataExchange;

namespace StepFunctionsApp.Mcp
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // MCP DATA EXCHANGE TOOLS — manage customer data-exchange pipeline profiles.
    // Profiles are file-based (workspace tree + legacy dir); the JSON round-trips with
    // the REST API and React UI, so a profile saved here is immediately executable.
    // ═══════════════════════════════════════════════════════════════════════════════

    [McpServerToolType]
    public class DataExchangeTools
    {
        private readonly DataExchangeProfileStore _profiles;
        private readonly DataExchangeExecutor _executor;

        public DataExchangeTools(DataExchangeProfileStore profiles, DataExchangeExecutor executor)
        {
            _profiles = profiles;
            _executor = executor;
        }

        [McpServerTool]
        [Description("List all data-exchange pipeline profiles. Returns id, name, subProjectPath and stage count for each profile.")]
        public string ListDataExchangeProfiles()
        {
            var entries = _profiles.ScanAll().Select(e => new
            {
                id = DataExchangeProfileStore.ResolveId(e.Profile),
                e.Profile.DataExchangeProfileName,
                e.SubProjectPath,
                stages = e.Profile.Pipeline?.PipelineStages?.Count ?? 0
            });
            return JsonConvert.SerializeObject(entries, McpJson.Settings);
        }

        [McpServerTool]
        [Description("Get a data-exchange profile's full JSON definition (camelCase). Accepts the profile id or its name. Use this before save_data_exchange_profile to fetch-then-modify an existing profile.")]
        public string GetDataExchangeProfile([Description("Profile id or name")] string idOrName)
        {
            var profile = _profiles.Get(idOrName);
            if (profile == null) return Error($"Data-exchange profile '{idOrName}' not found. Use list_data_exchange_profiles to see available profiles.");
            return JsonConvert.SerializeObject(profile, McpJson.Settings);
        }

        [McpServerTool]
        [Description(
"""
Create a new data-exchange profile or replace an existing one with the same id (upsert). Returns the stored id.
profileJson is the full profile JSON in camelCase — fetch an existing profile first and modify it, or build a new one:
{"dataExchangeProfileName":"orders-import","isActive":true,"dataSource":{"mediumType":"File","mediumConfigurationJson":"{\"path\":\"C:/data/orders.csv\"}","importSchema":{"attributeDomainName":"orders"}},"pipeline":{"pipelineStages":[...]}}
mediumType is one of: Api, ApiOAuth, Database, File. Omitting profileId derives it from a slug of the name; set it explicitly for stable dataexchange:// URIs.
""")]
        public string SaveDataExchangeProfile(
            [Description("Full profile JSON in camelCase (see tool description for shape)")] string profileJson,
            [Description("Optional workspace sub-project path to file the profile under, e.g. 'org/project/sub'. Omit to leave it unassigned at the workspace root.")] string? subProjectPath = null)
        {
            DataExchangeProfile profile;
            try
            {
                profile = JsonConvert.DeserializeObject<DataExchangeProfile>(profileJson, McpJson.Settings);
            }
            catch (Exception ex)
            {
                return Error($"profileJson is not valid JSON: {ex.Message}");
            }
            if (profile == null || string.IsNullOrWhiteSpace(profile.DataExchangeProfileName))
            {
                return Error("profileJson must contain a non-empty dataExchangeProfileName.");
            }

            var id = _profiles.Save(profile, subProjectPath);
            return JsonConvert.SerializeObject(new { id }, McpJson.Settings);
        }

        [McpServerTool]
        [Description("Delete a data-exchange profile by id or name. Returns whether it existed and was removed.")]
        public string DeleteDataExchangeProfile([Description("Profile id or name")] string idOrName)
        {
            var deleted = _profiles.Delete(idOrName);
            return JsonConvert.SerializeObject(new { deleted }, McpJson.Settings);
        }

        [McpServerTool]
        [Description(
"""
Run a data-exchange profile synchronously and return its output. Accepts the profile id or name.
input is an optional JSON object string with inline source rows (e.g. {"rows":[{"col1":"v1"}]}); omit it to let the profile's own data source supply the input.
""")]
        public async System.Threading.Tasks.Task<string> RunDataExchangeProfile(
            [Description("Profile id or name")] string idOrName,
            [Description("Optional execution input as a JSON object string with inline rows. Omit to use the profile's configured data source.")] string? inputJson = null)
        {
            var profile = _profiles.Get(idOrName);
            if (profile == null) return Error($"Data-exchange profile '{idOrName}' not found. Use list_data_exchange_profiles to see available profiles.");

            JObject? input;
            try
            {
                input = string.IsNullOrWhiteSpace(inputJson) ? null : JObject.Parse(inputJson);
            }
            catch (Exception ex)
            {
                return Error($"input is not valid JSON: {ex.Message}");
            }

            var result = await _executor.ExecuteAsync(DataExchangeProfileStore.ResolveId(profile), input);
            return JsonConvert.SerializeObject(result ?? new JObject(), McpJson.Settings);
        }

        private static string Error(string message) => JsonConvert.SerializeObject(new { error = message });
    }
}
