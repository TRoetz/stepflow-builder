using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using StepFlow.DataModel.Entities;
using StepFlow.DataModel.Entities.MetaData;
using StepFunctionsApp.StepFunctions;

Console.WriteLine("+++ StepFlow.Metadata demo +++");
Console.WriteLine("Attribute domains, schema definitions and the two storage providers\n");

var ws = Path.Combine(Path.GetTempPath(), "stepflow-demo", "metadata");
if (Directory.Exists(ws)) Directory.Delete(ws, true);
Directory.CreateDirectory(ws);

// ── 1. Build an attribute domain + linked schema in code ─────────────────────
var customer = new AttributeDomain
{
    AttributeDomainId = 1,
    Version = "1",
    AttributeDomainName = "customer",
    Description = "A retail customer record",
    IsCurrentVersion = true,
    Attributes = new List<EntityAttribute>
    {
        new() { EntityAttributeId = 1, PrimaryKey = true, AttributeName = "id",   DataType = AttributeDataType.String },
        new() { EntityAttributeId = 2,              AttributeName = "name", DataType = AttributeDataType.String },
        new() { EntityAttributeId = 3,              AttributeName = "age",  DataType = AttributeDataType.Number },
    }
};

var schema = new SchemaDefinition
{
    SchemaDefinitionId = 10,
    SchemaDefinitionName = "customer-schema",
    Version = "1",
    Description = "JSON schema for customer intake forms"
};

// ── 2. JSON file provider: save + read back ───────────────────────────────────
var jsonStore = new JsonFileAttributeDomainStore(
    Path.Combine(ws, "attribute_domains.json"), NullLogger<JsonFileAttributeDomainStore>.Instance);

jsonStore.Save(customer, schema);
Console.WriteLine($"[json] provider={jsonStore.ProviderName}");
foreach (var d in jsonStore.GetAll())
    Console.WriteLine($"  domain={d.AttributeDomainName} v{d.Version} ({d.Attributes.Count} attributes) current={d.IsCurrentVersion}");

// Case-insensitive lookup returns the domain plus its linked schema definition.
var (domain, linkedSchema) = jsonStore.GetByName("CUSTOMER");
Console.WriteLine($"  GetByName(\"CUSTOMER\") -> {domain?.AttributeDomainName}, linked schema: {linkedSchema?.SchemaDefinitionName}\n");

// ── 3. Upsert v2 with a new attribute (whole-schema replace semantics) ───────
customer.Version = "2";
customer.Attributes.Add(new EntityAttribute { EntityAttributeId = 4, AttributeName = "email", DataType = AttributeDataType.String });
jsonStore.Save(customer);

var (domainV2, _) = jsonStore.GetByName("customer");
Console.WriteLine($"[json] after upsert v{domainV2?.Version}: {string.Join(", ", domainV2!.Attributes.Select(a => a.AttributeName))}\n");

// ── 4. SQLite provider: same model, different storage family ─────────────────
var dbPath = Path.Combine(ws, "metadata.db");
using var sqliteStore = new SqliteAttributeDomainStore(dbPath, NullLogger<SqliteAttributeDomainStore>.Instance);
sqliteStore.Save(customer, schema);

Console.WriteLine($"[sqlite] provider={sqliteStore.ProviderName}");
foreach (var d in sqliteStore.GetAll())
    Console.WriteLine($"  domain={d.AttributeDomainName} v{d.Version} ({d.Attributes.Count} attributes)");

var (sd, ss) = sqliteStore.GetByName("Customer");
Console.WriteLine($"  GetByName(\"Customer\") -> {sd?.AttributeDomainName}, linked schema: {ss?.SchemaDefinitionName}\n");

// ── 5. Shared infra: engine exception + key-preserving contract resolver ─────
try
{
    throw new StepEngineException("Demo_NotFound", "attribute 'phone' not found in domain");
}
catch (StepEngineException ex)
{
    Console.WriteLine($"[infra] StepEngineException -> code={ex.ErrorCode}, message=\"{ex.Message}\"");
}

var json = JsonConvert.SerializeObject(
    new EntityAttribute { AttributeName = "SampleAttr", DisplayName = "Display Name" },
    new JsonSerializerSettings { ContractResolver = new KeyPreservingCamelCaseContractResolver() });
Console.WriteLine($"[infra] KeyPreservingCamelCaseContractSerializer -> {json}\n");

Console.WriteLine("Metadata demo complete.");
