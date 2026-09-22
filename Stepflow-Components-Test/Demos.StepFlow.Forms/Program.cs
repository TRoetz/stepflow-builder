using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFlow.DataModel.Entities;
using StepFlow.DataModel.Entities.MetaData;
using StepFunctionsApp.StepFunctions;

Console.WriteLine("+++ StepFlow.Forms demo +++");
Console.WriteLine("Form definitions (JSON + SQLite stores, versioning) and contract validation with type coercion\n");

var ws = Path.Combine(Path.GetTempPath(), "stepflow-demo", "forms");
if (Directory.Exists(ws)) Directory.Delete(ws, true);
Directory.CreateDirectory(ws);

// ── 1. Build a form definition bound to an attribute domain + schema ─────────
var page = JObject.Parse("""
{
  "title": "Customer intake",
  "RootElements": [
    { "id": 1, "type": "TextBox",   "label": "Name", "attribute": "name" },
    { "id": 2, "type": "NumberBox", "label": "Age",  "attribute": "age" }
  ]
}
""");

var form = new FormDefinition
{
    FormId = "customer-intake",
    Version = "1",
    IsCurrentVersion = true,
    Title = "Customer Intake",
    Description = "Collects a retail customer's name and age",
    AttributeDomainName = "customer",
    SchemaDefinitionName = "customer-schema",
    Page = page
};

// ── 2. JSON file store: save + read back (current version) ───────────────────
var jsonStore = new JsonFileFormDefinitionStore(ws, NullLogger<JsonFileFormDefinitionStore>.Instance);
jsonStore.Save(form);

Console.WriteLine($"[json] provider={jsonStore.ProviderName}");
foreach (var f in jsonStore.GetAll())
    Console.WriteLine($"  form={f.FormId} v{f.Version} current={f.IsCurrentVersion} title=\"{f.Title}\"");

var loaded = jsonStore.Get("customer-intake");
Console.WriteLine($"  Get(\"customer-intake\") -> v{loaded?.Version}, bound domain: {loaded?.AttributeDomainName}\n");

// ── 3. Versioning: save v2, read a specific version back ─────────────────────
var formV2 = new FormDefinition
{
    FormId = "customer-intake",
    Version = "2",
    IsCurrentVersion = true,
    Title = "Customer Intake (v2)",
    Description = "Adds an email field",
    AttributeDomainName = "customer",
    SchemaDefinitionName = "customer-schema",
    Page = page
};
jsonStore.Save(formV2);

Console.WriteLine($"[json] after saving v2:");
Console.WriteLine($"  Get(\"customer-intake\")        -> v{jsonStore.Get("customer-intake")?.Version} (current)");
Console.WriteLine($"  Get(\"customer-intake\", \"1\")   -> v{jsonStore.Get("customer-intake", "1")?.Version}\n");

// ── 4. SQLite store: same model, different storage family ────────────────────
using var sqliteStore = new SqliteFormDefinitionStore(Path.Combine(ws, "forms.db"), NullLogger<SqliteFormDefinitionStore>.Instance);
sqliteStore.Save(formV2);

Console.WriteLine($"[sqlite] provider={sqliteStore.ProviderName}");
foreach (var f in sqliteStore.GetAll())
    Console.WriteLine($"  form={f.FormId} v{f.Version} current={f.IsCurrentVersion}\n");

// ── 5. Validation: coerce a submitted values object against the attribute contract ─
EntityAttribute[] Contract(params (string Name, AttributeDataType Type)[] attrs) =>
    attrs.Select((a, i) => new EntityAttribute { EntityAttributeId = i + 1, AttributeName = a.Name, DataType = a.Type }).ToArray();

var contract = Contract(("name", AttributeDataType.String), ("age", AttributeDataType.Number));

// Valid submission: age arrives as a string and is coerced to a number.
var (coerced, errors) = FormValidationService.Validate(
    JToken.Parse("""{"name":"Alice","age":"31"}"""), contract);
Console.WriteLine($"[validate] {{\"name\":\"Alice\",\"age\":\"31\"}} ->");
Console.WriteLine($"  coerced: {coerced.ToString(Formatting.None)} (age type={coerced["age"]?.Type})");
Console.WriteLine($"  errors: {(errors.Count == 0 ? "none" : string.Join("; ", errors.Select(e => $"{e.Key}: {e.Value}")))}\n");

// Invalid submission: wrong type + missing required value.
var (bad, badErrors) = FormValidationService.Validate(
    JToken.Parse("""{"name":42,"age":"not-a-number"}"""), contract);
Console.WriteLine($"[validate] {{\"name\":42,\"age\":\"not-a-number\"}} ->");
foreach (var e in badErrors) Console.WriteLine($"  {e.Key}: {e.Value}");

// Unbound form (null contract): any JSON object passes through as-is.
var (passthrough, passthroughErrors) = FormValidationService.Validate(
    JToken.Parse("""{"anything":"goes"}"""), null);
Console.WriteLine($"\n[validate] unbound form -> {passthrough.ToString(Formatting.None)}, errors: {passthroughErrors.Count}\n");

// ── 6. Delete ────────────────────────────────────────────────────────────────
jsonStore.Delete("customer-intake");
Console.WriteLine($"[json] after Delete(\"customer-intake\"): {jsonStore.GetAll().Count} forms remain\n");

Console.WriteLine("Forms demo complete.");
