# StepFlow.Eav

Entity-attribute-value (EAV) dataset component: a registry of entity contracts, a JSON row store with idempotent appends, bridging to attribute domains, and the shared `eav` GET query language. This is what backs `eav://<entity>` resources in ASL flows and EAV-backed dynamic APIs.

## Package

| | |
|---|---|
| **Id** | `StepFlow.Eav` |
| **Version** | 1.0.0 |
| **Target framework** | net10.0 (uses the ASP.NET Core shared framework for `IQueryCollection`) |

## Dependencies

Project references:

- `StepFlow.Metadata` — attribute-domain models + stores used by the bridging code

NuGet:

- `Microsoft.Extensions.Logging.Abstractions` 10.0.5
- `Newtonsoft.Json` 13.0.4

## What's inside

| Type | Role |
|---|---|
| `EavEntityDefinition`, `EavAttributeDefinition` (`EavModels.cs`) | The entity contract: name, description, ordered attributes with data types and constraints. |
| `EavRegistryService` | In-memory registry of entity contracts persisted to a JSON file (`eav_registry.json`). Case-insensitive by entity name. |
| `EavRowStore` | One JSON array per domain under an `eav-data/` directory; append / update / patch / remove plus list, with idempotent appends (re-appending the same `rowKeyId` is a no-op). |
| `IEavEntityProvider`, `CompositeEavEntityProvider` | The rule-addressable entity surface: registry ∪ attribute domains (domain store wins on name collision). `FromAttributeDomain` converts a domain into an entity contract; the composite provider is what ASL's `eav://` resources resolve against. |
| `EavMapper` | Maps a dynamic JSON payload onto an entity contract (`Map`) and casts tokens to declared EAV types (`CastToEavType`). |
| `EavQuery` | The shared GET query language: parse `IQueryCollection` into options, then filter/sort/page/field-shape rows. Used by both the builder's REST endpoints and dynamic-API EAV GETs. |

## Usage

```xml
<PackageReference Include="StepFlow.Eav" Version="1.0.0" />
```

```csharp
using StepFunctionsApp.StepFunctions;   // original namespace kept for compatibility

// Initialize before registration so other services see a loaded registry at startup
var registry = new EavRegistryService();
registry.Initialize("eav_registry.json");          // load existing contracts (no-op if absent)
services.AddSingleton(registry);

var rows = new EavRowStore();
rows.Initialize("eav-data");                       // directory holding one JSON file per domain
services.AddSingleton(rows);

registry.RegisterEntity(new EavEntityDefinition
{
    EntityName = "council_fee",
    Attributes = new List<EavAttributeDefinition>
    {
        // DataType: "string" | "number" | "boolean" | "date"
        new() { AttributeName = "member_id", DataType = "string", IsRequired = true },
        new() { AttributeName = "amount",    DataType = "number" }
    }
});

rows.AppendRow("council_fee", new EavRow
{
    RowKeyId = "fee-001",                          // idempotency key — re-appends are skipped
    Values   = JObject.Parse("""{ "member_id": "m-7", "amount": 42.5 }""")
});

// Shared query language (same parsing as the builder's REST + dynamic APIs).
// Any non-reserved query param is a filter; reserved: sort (-prefix for desc), page | offset, limit, fields.
var options = EavQuery.Parse(queryCollection);     // e.g. ?amount=42.5&sort=-created&limit=20
var (page, total) = EavQuery.Apply(rows.ListRows("council_fee"), options);
```

## Notes

- Row storage is plain JSON on disk (`eav-data/{domain}.json`) — easy to back up and diff; the solution-package export/import in the builder ships these rows as first-class sections.
- `EavQuery.Parse` takes ASP.NET's `IQueryCollection`; if you're not in an ASP.NET host, build the options manually or wrap your own query string in a `QueryCollection`.
- Types live in the original `StepFunctionsApp.StepFunctions` namespace — the package split moved files, not namespaces.
