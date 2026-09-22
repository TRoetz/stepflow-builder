using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.StepFunctions;

Console.WriteLine("+++ StepFlow.Eav demo +++");
Console.WriteLine("Entity registry, payload mapping (type coercion), row store CRUD and the eav query language\n");

var ws = Path.Combine(Path.GetTempPath(), "stepflow-demo", "eav");
if (Directory.Exists(ws)) Directory.Delete(ws, true);
Directory.CreateDirectory(ws);

// ── 1. Registry: define an entity in code, persisted to eav_registry.json ────
var registry = new EavRegistryService();
registry.Initialize(Path.Combine(ws, "eav_registry.json"));

registry.RegisterEntity(new EavEntityDefinition
{
    EntityName = "customer",
    Description = "A retail customer record (EAV)",
    Attributes = new List<EavAttributeDefinition>
    {
        new() { AttributeName = "id",   DataType = "string",  JsonPathMapping = "id" },
        new() { AttributeName = "name", DataType = "string",  IsRequired = true, JsonPathMapping = "name" },
        new() { AttributeName = "age",  DataType = "number",  JsonPathMapping = "age" },
        new() { AttributeName = "vip",  DataType = "boolean", DefaultValue = false, JsonPathMapping = "vip" },
    }
});

Console.WriteLine($"[registry] entities: {string.Join(", ", registry.GetAllEntities().Select(e => e.EntityName))}");
var entity = registry.GetEntity("CUSTOMER"); // case-insensitive
Console.WriteLine($"  GetEntity(\"CUSTOMER\") -> {entity?.EntityName}: " +
                  string.Join(", ", entity!.Attributes.Select(a => $"{a.AttributeName}:{a.DataType}{(a.IsRequired ? "*" : "")}")));

// ── 2. Map a loose JSON payload to strict EAV (type coercion) ────────────────
var mapped = registry.MapPayloadToEav("customer", JToken.Parse("""{"id":"42","name":"Alice","age":"31"}"""));
Console.WriteLine("[map] payload {\"id\":\"42\",\"name\":\"Alice\",\"age\":\"31\"} ->");
foreach (var kv in mapped)
    Console.WriteLine($"  {kv.Key} = {kv.Value} ({kv.Value?.GetType().Name})\n");

// ── 3. Row store: append / update / patch / delete / list ────────────────────
var rows = new EavRowStore(NullLogger<EavRowStore>.Instance);
rows.Initialize(Path.Combine(ws, "eav-data"));

void Append(string key, object entityId, JObject values) =>
    rows.AppendRow("customer", new EavRow { RowKeyId = key, EntityId = entityId, EntityType = "customer", Values = values });

Append("r1", 42, JObject.FromObject(mapped));
Append("r2", 7,  JObject.Parse("""{"id":"7","name":"Bob","age":58,"vip":true}"""));
Append("r3", 99, JObject.Parse("""{"id":"99","name":"Carol","age":24}"""));

Console.WriteLine($"[store] domains: {string.Join(", ", rows.ListDomains())}");
Console.WriteLine($"[store] customer rows after append: {rows.ListRows("customer").Count}\n");

// Update replaces the whole value set; patch merges into it.
rows.UpdateRow("customer", "r2", JObject.Parse("""{"id":"7","name":"Bobby","age":58,"vip":true}"""));
rows.PatchRow("customer", "r3", new JObject { ["vip"] = true });

var r2 = rows.ListRows("customer").First(r => r.RowKeyId == "r2");
var r3 = rows.ListRows("customer").First(r => r.RowKeyId == "r3");
Console.WriteLine($"[store] after UpdateRow(r2): name={r2.Values["name"]}");
Console.WriteLine($"[store] after PatchRow(r3): vip={r3.Values["vip"]}, age kept={r3.Values["age"]}\n");

// ── 4. The eav query language (same surface as the engine's /api/eav/{domain}/rows) ─
void Query(string label, string queryString)
{
    var dict = new Dictionary<string, StringValues>();
    foreach (var pair in queryString.Split('&', StringSplitOptions.RemoveEmptyEntries))
    {
        var idx = pair.IndexOf('=');
        var k = Uri.UnescapeDataString(pair[..idx]);
        var v = Uri.UnescapeDataString(pair[(idx + 1)..]);
        dict[k] = v;
    }
    var qc = new QueryCollection(dict);

    var options = EavQuery.Parse(qc);
    var (page, total) = EavQuery.Apply(rows.ListRows("customer"), options);
    Console.WriteLine($"[query] ?{queryString}");
    Console.WriteLine($"  -> total={total}, page:");
    foreach (var row in page)
        Console.WriteLine($"    {row.ToString(Formatting.None)}");
    Console.WriteLine();
}

Query("all, sorted by age desc", "sort=-age");
Query("filter vip (values compared ordinal -> .NET 'True')", "vip=True");
Query("filter + sort + limit", "entityId=42&limit=10");
Query("field projection (rowKeyId always kept)", "fields=name,vip&sort=name");

// ── 5. Single-row lookup: exact EntityId first, RowKeyId fallback ────────────
var byEntity = EavQuery.Lookup(rows.ListRows("customer"), "42");
Console.WriteLine($"[lookup] id=42 -> {byEntity.Count} row(s): {string.Join(", ", byEntity.Select(r => r.RowKeyId))}");
var byKey = EavQuery.Lookup(rows.ListRows("customer"), "r3");
Console.WriteLine($"[lookup] key=r3 -> {byKey.Count} row(s): {string.Join(", ", byKey.Select(r => r.EntityId.ToString()))}\n");

// ── 6. Delete ────────────────────────────────────────────────────────────────
rows.RemoveRow("customer", "r1");
Console.WriteLine($"[store] after RemoveRow(r1): {rows.ListRows("customer").Count} rows remain\n");

Console.WriteLine("EAV demo complete.");
