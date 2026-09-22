# StepFlow.Transform

DuckDB-backed transform service shared by ASL flows and DataExchange pipelines. Executes a `transform` block (SQL over the input payload) and returns the result as JSON — no other component depends on it, so it is safe to use standalone for ad-hoc data shaping.

## Package

| | |
|---|---|
| **Id** | `StepFlow.Transform` |
| **Version** | 1.0.0 |
| **Target framework** | net10.0 |

## Dependencies

No project references (leaf package). NuGet:

- `DuckDB.NET.Data.Full` 1.0.2 — embedded DuckDB engine
- `Microsoft.Extensions.Logging.Abstractions` 10.0.5
- `Newtonsoft.Json` 13.0.4

## What's inside

| Type / method group | Role |
|---|---|
| `DuckDbTransformService.ExecuteTransform(JObject)` | The pipeline entry point: runs a transform operation (`query`, `filter`, `project`, `aggregate`) and returns `{ rows, rowCount }`. Same shape used by ASL `transform://duckdb` tasks and DataExchange stages. |
| `ExecuteQuery` / `ExecuteNonQuery` | Raw SQL against the in-memory database (with parameters). |
| `LoadJsonData` / `LoadCsvFile` / `LoadParquetFile` / `LoadCsvString` | Load data into named tables (`CREATE OR REPLACE`). |
| `Filter` / `Project` / `Aggregate` / `Sort` / `GetTableSample` / `GetTableSchema` / `ListTables` | Convenience table operations and introspection. |

Behavior notes:

- The service holds **one long-lived in-memory connection** for its lifetime — register it as a singleton.
- Tables loaded via `data` (or file sources) persist as named tables (`CREATE OR REPLACE`) until overwritten, so reusing a table name replaces its contents. There is no internal concurrency guard: serialize calls that share table names if you run transforms in parallel.

## Usage

```xml
<PackageReference Include="StepFlow.Transform" Version="1.0.0" />
```

```csharp
using StepFunctionsApp.StepFunctions;   // original namespace kept for compatibility

services.AddSingleton<DuckDbTransformService>();

// ...
var duckDb = app.Services.GetRequiredService<DuckDbTransformService>();

// Input contract (same shape used by ASL `transform://duckdb` tasks and DataExchange stages):
//   operation: "query" | "filter" | "project" | "aggregate"
//   sql / table / where / columns / groupBy — per operation
//   data + table  — optional inline rows to load first
var result = duckDb.ExecuteTransform(new JObject
{
    ["operation"] = "query",
    ["table"]     = "items",
    ["data"]      = new JArray(
        new JObject { ["name"] = "a", ["qty"] = 2 },
        new JObject { ["name"] = "b", ["qty"] = 5 }),
    ["sql"]       = "SELECT name, qty * 2 AS doubled FROM items"
});
// result: { "rows": [ ... ], "rowCount": 2 }
```

## Notes

- Types live in the original `StepFunctionsApp.StepFunctions` namespace — the package split moved files, not namespaces.
- The DuckDB native binary ships with `DuckDB.NET.Data.Full`; no external database server is required.
