# StepFlow.Metadata

Shared metadata layer for StepFlow components: the attribute-domain and schema-definition **models** (`StepFlow.DataModel.Entities.*`), their **persistence stores** (JSON file or SQLite), and a few cross-cutting infrastructure types used by every other package. Leaf package — it references nothing else in the family.

## Package

| | |
|---|---|
| **Id** | `StepFlow.Metadata` |
| **Version** | 1.0.0 |
| **Target framework** | net10.0 |

## Dependencies

No project references (leaf package). NuGet:

- `Microsoft.Data.Sqlite` 10.0.11 — SQLite persistence provider
- `Microsoft.Extensions.Logging.Abstractions` 10.0.5
- `Newtonsoft.Json` 13.0.4

## What's inside

| Type / folder | Role |
|---|---|
| `MetaData/`, `DataSource/`, `UIData/`, `Enums.cs` | All DataModel entity types: `AttributeDomain`, `EntityAttribute`, `SchemaDefinition`, `Rule`, data-source models, UI-data models. Namespace `StepFlow.DataModel.Entities.*`. |
| `IAttributeDomainStore` + `JsonFileAttributeDomainStore` / `SqliteAttributeDomainStore` | CRUD for attribute domains (the EAV "contracts"). JSON provider stores one registry file; SQLite provider uses a shared database file. |
| `ISchemaDefinitionStore` + `JsonFileSchemaDefinitionStore` / `SqliteSchemaDefinitionStore` | Same pattern for pinned schema definitions referenced by forms and domains. |
| `AttributeDomainEntry`, `AttributeDomainMapping` | Wire/POCO mapping between the store entry shape and the domain models. |
| `StepFlowDataDb` | Static helpers that open/prepare the shared SQLite database (connection string, table bootstrap). |
| `SharedInfrastructure.cs` | `KeyPreservingCamelCaseContractResolver` (global namespace) — camelCase JSON that preserves dictionary keys; `StepEngineException` (`StepFunctionsApp.StepFunctions`) — the engine's standard exception type. |

## Usage

```xml
<PackageReference Include="StepFlow.Metadata" Version="1.0.0" />
```

Typical consumers register one provider pair and reuse them:

```csharp
using StepFlow.DataModel.Entities.MetaData;   // models
using StepFunctionsApp.StepFunctions;          // shared infra types

// JSON-file providers (default in the builder)
services.AddSingleton<IAttributeDomainStore>(sp =>
    new JsonFileAttributeDomainStore("attribute_domains.json", sp.GetRequiredService<ILogger<JsonFileAttributeDomainStore>>()));
services.AddSingleton<ISchemaDefinitionStore>(sp =>
    new JsonFileSchemaDefinitionStore("schema_definitions.json", sp.GetRequiredService<ILogger<JsonFileSchemaDefinitionStore>>()));

// …or the SQLite providers (one shared database file)
services.AddSingleton<IAttributeDomainStore>(sp =>
    new SqliteAttributeDomainStore("stepflow_data.db", sp.GetRequiredService<ILogger<SqliteAttributeDomainStore>>()));
```

If you only need the **models** (e.g. to build your own EAV or form contracts), reference this package and use `AttributeDomain` / `EntityAttribute` directly — no store required.

## Notes

- This is where `StepEngineException` and `KeyPreservingCamelCaseContractResolver` live now; they were previously internal to the builder app and are public here so all packages (and your code) can share them.
- The JSON providers are file-per-store; the SQLite providers expect a single shared db path — pick one provider family per deployment, don't mix them for the same data.
