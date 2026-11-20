using System.ComponentModel;
using ModelContextProtocol.Server;
using Newtonsoft.Json;
using StepFunctionsApp.StepFunctions;

namespace StepFunctionsApp.Mcp
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // MCP FORM DEFINITION TOOLS
    // CRUD over IFormDefinitionStore — the same store the React form builder and
    // FormCaptureService use. Saving a bound form (attributeDomainName set) is what
    // makes a new HumanTask "form" completion flow possible headlessly: the engine
    // validates submissions against the named AttributeDomain and persists EAV rows.
    // ═══════════════════════════════════════════════════════════════════════════════

    [McpServerToolType]
    public class FormTools
    {
        private readonly IFormDefinitionStore _store;

        public FormTools(IFormDefinitionStore store)
        {
            _store = store;
        }


        [McpServerTool]
        [Description("List all saved form definitions (every version of every form). Returns compact metadata per saved version: formId, version, isCurrentVersion, title, attributeDomainName. Use get_form_definition for the full page definition.")]
        public string ListFormDefinitions()
        {
            var forms = _store.GetAll().Select(f => new
            {
                f.FormId,
                f.Version,
                f.IsCurrentVersion,
                f.Title,
                f.AttributeDomainName
            });
            return JsonConvert.SerializeObject(forms, McpJson.Settings);
        }

        [McpServerTool]
        [Description("Get one saved form definition as full JSON: formId, version, isCurrentVersion, title, description, attributeDomainName, schemaDefinitionName and the Page UIPage (with RootElements). Defaults to the current version; pass version to pin an exact saved version.")]
        public string GetFormDefinition(
            [Description("Form id")] string formId,
            [Description("Optional exact version label (e.g. \"2\"); defaults to the current version when omitted.")] string? version = null)
        {
            try
            {
                var def = string.IsNullOrWhiteSpace(version) ? _store.Get(formId) : _store.Get(formId, version);
                if (def == null)
                {
                    var suffix = string.IsNullOrWhiteSpace(version) ? "" : $" at version '{version}'";
                    return Error($"Form '{formId}' not found{suffix}. Use list_form_definitions to see saved forms.");
                }
                return JsonConvert.SerializeObject(def, McpJson.Settings);
            }
            catch (ArgumentException ex)
            {
                return Error(ex.Message); // invalid form id / version label
            }
        }

        [McpServerTool]
        [Description(
"""
Save a new form definition or replace an existing one with the same formId+version (upsert). Returns { status: "saved", formId, version }.

Definition shape (camelCase JSON string):
  formId:                 required; unique per form.
  version?:               optional label, defaults to "1". Saving again with the same formId+version replaces that version in place; other saved versions are kept.
  isCurrentVersion?:      optional, defaults to true. The current version is what flow steps resolve when they do not pin one; setting it clears the flag on every other version of this form.
  title:                  human-readable form title.
  description?:           optional longer description.
  attributeDomainName?:   name of a saved AttributeDomain (see list_attribute_domains). When set, the form is "bound": submissions are validated against the domain's attributes and captured values persist as EAV rows — this is what makes a new HumanTask flow with completion type "form" work headlessly. Omit for an unbound form.
  schemaDefinitionName?:  optional linked schema definition name.
  page:                   required UIPage object per uidata-schema.json; must contain a RootElements array (the form's fields/layout).

Example definitionJson:
{"formId":"order-intake","title":"Order Intake","attributeDomainName":"CouncilFee","page":{"RootElements":[{"type":"field","name":"amount","label":"Amount"}]}}
""")]
        public string SaveFormDefinition(
            [Description("Full FormDefinition as a JSON string: { \"formId\": \"...\", \"version?\": \"1\", \"isCurrentVersion?\": true, \"title\": \"...\", \"description?\": \"...\", \"attributeDomainName?\": \"...\", \"schemaDefinitionName?\": \"...\", \"page\": { ...UIPage with RootElements } }")] string definitionJson)
        {
            FormDefinition def;
            try
            {
                def = JsonConvert.DeserializeObject<FormDefinition>(definitionJson, McpJson.Settings);
            }
            catch (Exception ex)
            {
                return Error($"definition is not valid JSON: {ex.Message}");
            }
            if (def == null) return Error("definition must be a JSON object.");

            try
            {
                _store.Save(def);
            }
            catch (ArgumentException ex)
            {
                return Error(ex.Message); // missing FormId/Version, or Page without RootElements
            }
            return JsonConvert.SerializeObject(new { status = "saved", formId = def.FormId, version = def.Version }, McpJson.Settings);
        }

        [McpServerTool]
        [Description("Delete a saved form definition — every saved version of the form. Returns an error when no such form exists.")]
        public string DeleteFormDefinition([Description("Form id")] string formId)
        {
            try
            {
                return _store.Delete(formId)
                    ? JsonConvert.SerializeObject(new { status = "deleted", formId }, McpJson.Settings)
                    : Error($"Form '{formId}' not found. Use list_form_definitions to see saved forms.");
            }
            catch (ArgumentException ex)
            {
                return Error(ex.Message); // invalid form id
            }
        }

        private static string Error(string message) => JsonConvert.SerializeObject(new { error = message });
    }
}
