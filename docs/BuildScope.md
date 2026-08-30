- Files review:
 - C:\Source\stepflow-builder\StepFunctionsApp\DataExchange *.cs
 Note:
 - ActionType.cs: Transformation, EnrichmentLookup, Dispatch
 - MergeStrategy.cs: OverwriteExisting, AddNewOnly
 - TransformType.cs: Direct, Literal, Concatenate
 - ActionStage.cs: Ingestion, Validation, DataTreatment, Enrichment, PreRouting, Dispatch
 - RuleType.cs: Validation, Calculation
 - LookupType.cs: Api, Database, File
 - DataSourceMediumType.cs: File, Api, Database
 - AttributeDataType.cs: String, Integer, Long, Decimal, Boolean, DateTime, Guid

 — Backend executor + API
 Proposed New file DataExchange/DataExchangeExecutor.cs (namespace StepFunctionsApp.DataExchange):
 - class DataExchangeExecutor : IActionInvocation? No — a standalone service. Inject ILogger<DataExchangeExecutor>.
 - Methods:
   - Task<JObject> ExecuteAsync(DataExchangeProfile profile, JObject input) → full pipeline result
   - Task<JArray> ProcessRowsAsync(profile, JArray rows) → transformed rows (used by flows)
   - Internal per-stage processing:
     - Ingestion: if input has "rows" use it; otherwise load from file path in MediumConfigurationJson (CSV via CSVReader.ReadDynamicCsvFile, XML via ReadXmlFile, JSON via parse); apply ImportSchemaMap to map external→internal columns.
     - Validation stage: run Rules of type Validation against each row → collect violations; blocking failures mark the row as rejected.
     - DataTreatment: apply SchemaMaps (TransformType Direct/Literal/Concatenate + MergeStrategy).
     - Enrichment: for each Lookup (LookupType.Api): render QueryOrBodyTemplate with row values, HTTP GET/POST to LookupEndpoint, extract ValueFieldToReturn from response JSON, write into OutputTargetAttributeId via MergeStrategy.
     - PreRouting: Rules of type Calculation → produce routing value; or Parameters-based routing.
     - Dispatch: for each Action (ActionType.Dispatch): render body template / send row to ActionEndpoint.ActionEndpointURL; collect per-row results.
   - Row representation: JObject with attribute IDs as keys? Or names? EntityAttribute has AttributeId + Name — use Name as the key in rows (more readable), map via schema.

 So the executor should be pragmatic: implement enough of the pipeline to actually run end-to-end against real sample data (CSV import → transform → lookup enrichment via fake API → dispatch), not every enum member's full semantics. Keep it honest and documented about
 what's implemented vs stubbed… no, per my contract no stubs — everything you wire up must work.

 Let me define the executor scope precisely:
 - Ingestion: rows from input JSON ("rows" array) or file (CSV via CSVReader.ReadDynamicCsvFile / XML via ReadXmlFile / JSON). Apply ImportSchemaMap (external column → internal attribute name mapping).
 - Validation: Rules with RuleType.Validation, Expression as boolean expression over row fields — implement a small evaluator? Their comment says "SQL CASE statement or boolean expression" and mentions CachedSqliteRuleProcessor. Implementing a full SQL expression
   engine is out of scope; but I can evaluate expressions via DuckDB! The app already has DuckDbTransformService with ExecuteQuery… actually simpler: use SQLite (Microsoft.Data.Sqlite) — register row fields as parameters, wrap the expression in SELECT CASE WHEN <expr>
   THEN 1 ELSE 0 END. That's exactly "SQL boolean expression" and matches their CachedSqliteRuleProcessor concept.
 - DataTreatment: SchemaMaps with TransformType Direct/Literal/Concatenate + MergeStrategy.
 - Enrichment: Lookups of LookupType.Api — render QueryOrBodyTemplate (replace {Field} tokens), HTTP call, extract ValueFieldToReturn (dot-path into JSON response), merge via strategy.
 - PreRouting: Calculation rules → produce a routing value stored on the row; route to ActionEndpoint by matching Parameters["Route"]? Keep it simple: each Action in the Dispatch stage has an optional "When" expression (SQL boolean) — if present, only rows where it's
   true get dispatched there. Hmm, that adds a field not in their model… Action.Parameters is Dictionary<string,string> — I can use Parameters["Filter"] as an optional SQL boolean expression. That stays within their model.
 - Dispatch: Actions of ActionType.Dispatch → POST row JSON to ActionEndpoint.ActionEndpointURL (or GET if Method parameter says so); collect status + response snippet per row; batch option via Parameters["Batch"]="true" sending all rows in one call.

 Result shape (JObject):...

 Controller: new Controllers/DataExchangeController.cs:
 - GET api/data-exchange/profiles → list profiles (from a profile store)
 - POST api/data-exchange/profiles → save profile (JObject payload → DataExchangeProfile via Newtonsoft), persist to disk (flow-state style: dataexchange-profiles/<id>.json)
 - DELETE api/data-exchange/profiles/{id}
 - POST api/data-exchange/execute {profileId, input} or {profile inline, input} → run executor, return result
 - GET api/data-exchange/monitor?executionId=... — hmm, execution is synchronous here; monitoring = list of recent executions. Keep a simple in-memory + disk log: each execute appends to dataexchange-executions.json (id, profileId, startedUtc, durationMs, success,
   rowsIn, rowsOut). GET api/data-exchange/executions → last 50.

 Profile store: where? Follow the FlowState pattern — check how flows are persisted… FlowsController uses StepFunctionService.RegisterStateMachine; flow definitions live in memory + probably disk under flow-state/. For data exchange profiles, simplest and most robust:
 JSON files under dataexchange/ directory (like human-tasks/). Create a small DataExchangeProfileStore class or inline it in the controller.

— Frontend panel
 - StepFlow-UI/src/services/dataExchangeService.ts: typed API client (listProfiles, saveProfile, deleteProfile, execute, listExecutions) hitting /api/data-exchange/*.
 - App.tsx: add a "Data Exchange" tab/panel in the shell. Need to read App.tsx's structure first (32.8KB — I'll do that at execution time). Panel contents:
   - Profile list + create/edit form (name, description, medium type, config JSON)
   - Pipeline editor: stages with actions/rules/lookups/schema maps — full visual pipeline editing is a big lift; pragmatic v1: structured JSON editor (Monaco is already a dependency!) for the profile definition + a "Run" button that POSTs sample input and shows
     per-stage results. Given Monaco is in deps, a schema-guided JSON editor with validation is honest and useful, not a stub.
   - Execution monitor: table of recent executions with status/rows/duration; click → detail view (stage messages, rejected rows).


— Sample data + E2E test flow
 - Create sample CSV (e.g., agrichemical spray events) + XML + large JSON under smoke/ or dataexchange-samples/.
 - Register a Step Functions flow that calls the fake APIs? Wait — the earlier user request in this conversation was about C# test APIs for fake calls (weather, gold price, USD/NZD, CSV import). That was an earlier turn… actually looking at history: the first user
   message in this archived conversation is "Run frontend test suite + typecheck/build" and then "add some C# test apis to fake calls in a test project so we can do tests like fetching weather and gold prices..." — hmm wait, that second message appears in HISTORY as
   ¶user. So there were two prior requests: (1) run tests+build [done], (2) add C# fake test APIs for weather/gold/FX/CSV [that was the big middle section I can only see partially].
