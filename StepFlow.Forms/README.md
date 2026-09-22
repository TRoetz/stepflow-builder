# StepFlow.Forms

UI Forms component: versioned form definitions (JSON-configured pages, optionally bound to an attribute domain) with pluggable persistence (JSON file or SQLite), plus the transport-agnostic **validation/coercion engine** that turns raw submitted values into a clean, contract-conforming document.

## Package

| | |
|---|---|
| **Id** | `StepFlow.Forms` |
| **Version** | 1.0.0 |
| **Target framework** | net10.0 |

## Dependencies

Project references:

- `StepFlow.Metadata` — the `EntityAttribute` contract type used by validation, and schema-definition stores for bound forms

NuGet:

- `Microsoft.Data.Sqlite` 10.0.11
- `Microsoft.Extensions.Logging.Abstractions` 10.0.5
- `Newtonsoft.Json` 13.0.4

## What's inside

| Type | Role |
|---|---|
| `FormDefinition` | A versioned form: `FormId`, `Version`, `IsCurrentVersion`, title/description, optional `AttributeDomainName` binding, and the full UIPage JSON (`Page`). |
| `IFormDefinitionStore` | Contract: `GetAll()`, `Get(formId)`, `Get(formId, version)`, `Save(def)` (upsert by FormId+Version; current-version flag management), delete. |
| `JsonFileFormDefinitionStore` | File-based provider (one registry file). Default in the builder. |
| `SqliteFormDefinitionStore` | SQLite provider (shared database file). |
| `FormDataOptions` | Options section (`"FormData"`): `Provider` = `"json"` \| `"sqlite"`, plus paths for each provider. |
| `FormValidationService` | The engine: given a submitted values object and an attribute contract, coerces each value to its declared type and enforces the constraint schema (`required`, `minimum`/`maximum`, `minLength`/`maxLength`, `pattern`). Returns the cleaned document + per-field errors. No ASP.NET dependency — usable from any host. |

## Usage

```xml
<PackageReference Include="StepFlow.Forms" Version="1.0.0" />
```

```csharp
using StepFunctionsApp.StepFunctions;   // original namespace kept for compatibility
using StepFlow.DataModel.Entities.MetaData;  // EntityAttribute contract type (from StepFlow.Metadata)

// 1) Register a store provider
services.AddSingleton<IFormDefinitionStore>(sp =>
    new JsonFileFormDefinitionStore("forms", sp.GetRequiredService<ILogger<JsonFileFormDefinitionStore>>()));

var forms = app.Services.GetRequiredService<IFormDefinitionStore>();

// 2) Save / load versioned definitions
forms.Save(new FormDefinition
{
    FormId   = "council-fee-capture",
    Version  = "1",
    IsCurrentVersion = true,
    Title    = "Council fee capture",
    AttributeDomainName = "council_fee",     // binds validation to that domain's contract
    Page     = JObject.Parse("""{ "rootElements": [ /* UIPage per uidata-schema.json */ ] }""")
});

var current = forms.Get("council-fee-capture");   // resolves the current version

// 3) Validate + coerce a submission (transport-agnostic — call from any controller, MCP tool, or worker).
// values is a flat object keyed by attribute name; absent optional attributes are skipped in the result.
EntityAttribute[] contract = /* attributes of the bound domain */;
var valuesToken = JObject.Parse("""{ "member_id": "m-7", "amount": "42.5" }""");

var (coerced, errors) = FormValidationService.Validate(valuesToken, contract);
if (errors.Count > 0)
{
    // errors: attribute name -> message; reject the submission
}
else
{
    // coerced: clean JObject ready to persist (e.g. as an EAV row via StepFlow.Eav)
}
```

## Notes

- **Bound vs unbound forms**: a form with `AttributeDomainName` set validates submissions against that domain's `EntityAttribute[]` contract; unbound forms skip validation and pass values through.
- The builder's `FormCaptureController` is the reference integration: it loads the form, resolves its domain contract from `StepFlow.Metadata`, calls `FormValidationService.Validate`, then appends the coerced document to an EAV row store (`StepFlow.Eav`).
- Types live in the original `StepFunctionsApp.StepFunctions` namespace — the package split moved files, not namespaces.
