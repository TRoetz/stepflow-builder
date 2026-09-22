# Stepflow Components Test

Standalone use-case demos for the seven published `StepFlow.*` NuGet packages in
`..\nupkgs`. Each demo is a self-contained console app that exercises one package's
public surface and prints what it did. No project references — everything is consumed
as the published `.nupkg` artifacts via the local feed declared in [nuget.config](nuget.config).

## Prerequisites

- .NET SDK 10 (`net10.0`)
- The packages at `C:\Source\stepflow-builder\nupkgs` (all v1.0.0)
- All transitive dependencies are already in the global NuGet cache, so restore works offline

## Build & run

```powershell
dotnet build Stepflow-Components-Test.slnx

# one demo:
dotnet run --project Demos.StepFlow.Asl/Demos.StepFlow.Asl.csproj

# all of them:
foreach ($p in Get-ChildItem -Directory "Demos.*") { dotnet run --project $p.FullName }
```

Each demo writes its scratch files (SQLite DBs, JSON stores, dispatched CSV/JSON) under
`%TEMP%\stepflow-demo\<name>` and recreates them on every run.

## Demos

| Project | Package | What it shows |
|---|---|---|
| `Demos.StepFlow.Metadata` | StepFlow.Metadata | Attribute domains + entity attributes in both store families (JSON file, SQLite), schema definitions, rules, the shared `StepEngineException` / camelCase-serializer infra. |
| `Demos.StepFlow.Transform` | StepFlow.Transform | In-memory DuckDB: raw SQL queries, filter/project/aggregate via the JObject API, inline data + CSV loading, sort/deduplicate/lookup, table introspection. |
| `Demos.StepFlow.Eav` | StepFlow.Eav | Entity registry (persisted to JSON), attribute mapping with type coercion, row store append/update/patch/delete, and the `EavQuery` GET query language (filter/sort/page/limit/fields). |
| `Demos.StepFlow.Forms` | StepFlow.Forms | Form definitions bound to an attribute domain + schema in both stores (JSON file, SQLite), versioning with current-version switching, and contract validation with type coercion. |
| `Demos.StepFlow.DataExchange` | StepFlow.DataExchange | A full profile built from the entity models: CSV ingestion → Logic stage (validation reject + calculation) → Transformation stage (schema map) → Dispatch to `file://` CSV/JSON, then prints the execution report and the files that landed on disk. |
| `Demos.StepFlow.DynamicApi.Core` | StepFlow.DynamicApi.Core | Boots an ASP.NET Core minimal host with a dynamic API catalog (APIs as data), wires matcher + auth + input merging + EAV GET mapping into a catch-all endpoint, then self-tests over real HTTP: 401 without token, path-param lookup, query language, body+query merge on POST, 404 and 405+Allow. |
| `Demos.StepFlow.Asl` | StepFlow.Asl (top-level; references all others) | The full engine wired via DI: a flow using `transform://jsonata`, `transform://csharp`, `sql://` (SQLite), `eav://` and named rules, a human-task gate (Suspended → `CompleteHumanTaskAsync` → Succeeded), disk checkpointing + stored-execution listing. |

## Package quirks observed while writing these demos

Documented inline in the demo code; all are v1.0.0 behaviors of the packages themselves:

- **Transform**: `DuckDbTransformService.ExecuteQuery(sql, parameters)` binds each dict key as a *named* DuckDB parameter (`$key`), which never matches positional `$1` or named `?amount` placeholders — verified empirically. Use parameterless SQL through that overload (or raw `duckdb.net.data.full` for real binding).
- **Eav**: `EavQuery` filters/sort compare the *string* form of values ordinally, so booleans must be queried as `.NET` casing (`?vip=True`) and numeric sort is lexicographic (`"80"` > `"300"`).
- **DynamicApi.Core**: `DynamicApiInput.ReadRawBodyAsync` only reads bodies whose Content-Type contains `application/json`; the `Allow` response header surfaces on the client side in the content-header collection.
