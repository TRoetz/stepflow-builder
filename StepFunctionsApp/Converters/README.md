# SSIS-to-StepFunctions Converter

This directory contains tools and resources for converting SSIS packages into Step Functions workflows.

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Conversion Pipeline                   │
│                                                          │
│  ┌──────────────┐    ┌──────────────┐    ┌────────────┐ │
│  │ SSIS Package │──▶│ Pattern Match │──▶│ State Map   │ │
│  │ (XML)        │    │ Registry      │    │ Definition │ │
│  └──────────────┘    └──────────────┘    └────────────┘ │
│                                                          │
│  ┌──────────────┐    ┌──────────────┐    ┌────────────┐ │
│  │ Manifest     │◀──│ Builder       │◀──│ Resources   │ │
│  │ Tracking     │    │ Execution    │    │ Patterns   │ │
│  └──────────────┘    └──────────────┘    └────────────┘ │
└─────────────────────────────────────────────────────────┘
```

## Components

### ResourceRegistry.cs
Centralized resource definitions for all available services:
- `transform://` - DuckDB data transformations
- `rule://` - Business validation and correction rules
- `http://` - External API integrations
- `flow://` - Sub-workflows
- `internal://` - Built-in operations

Usage:
```csharp
// Check if resource exists
if (ResourceRegistry.Exists("transform://query")) {
    var resource = ResourceRegistry.Get("transform://query");
}
```

### WorkflowPatterns.cs
Reusable workflow pattern templates:
- Load/Validate/Transform patterns
- Error handling patterns
- Parallel processing patterns
- Conditional branching patterns

Usage:
```csharp
var pattern = WorkflowPatterns.Get("load-data-pattern");
if (pattern != null) {
    // Apply pattern template
}
```

### ConversionBuilder.cs
Main builder class that orchestrates the conversion process:
1. Extracts tasks from SSIS XML
2. Matches each task to a pattern
3. Creates Step Functions state definitions
4. Generates manifest with mappings

Usage:
```csharp
var result = ConversionBuilder.Build(
    "path/to/package.dtsx",
    "output-flow-id"
);

if (result.Success) {
    Console.WriteLine(result.WorkflowJson);
}
```

### ConversionManifest.json
Tracks all conversions with:
- Source package information
- Task-to-state mappings
- Validation results
- Rollback instructions

## Adding New Resources

Edit `ResourceRegistry.cs` or use:
```csharp
ResourceRegistry.Register(
    "custom-resource-id",
    "resource-type",
    "description",
    new { /* schema */ }
);
```

## Adding New Patterns

Create JSON files in `Converters/Patterns/` directory:
```json
{
  "Name": "my-pattern",
  "Description": "Pattern description",
  "Template": {
    "Type": "Task",
    "Resource": "transform://query",
    "Parameters": {}
  },
  "Metadata": {
    "Phase": "load",
    "Direction": "inbound"
  }
}
```

## Best Practices

1. **Always check resource availability** before using
2. **Document transformation logic** in manifest
3. **Version your conversions** for rollback capability
4. **Use patterns consistently** across projects
5. **Validate converted workflows** before deployment

## Example Conversion Flow

```csharp
// Step 1: Build conversion
var result = ConversionBuilder.Build("REG_INT_BTP.dtsx", "bank-processing-v1");

// Step 2: Validate result
if (result.Success) {
    // Step 3: Save workflow
    File.WriteAllText(
        "output.json",
        result.WorkflowJson
    );
    
    // Step 4: Update manifest
    File.WriteAllText(
        "conversion_manifest.json",
        JsonSerializer.Serialize(result.Mappings)
    );
}
```
