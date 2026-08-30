using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFlow.DataModel.Entities;
using StepFlow.DataModel.Entities.MetaData;
using StepFlow.DynamicApi;
using StepFunctionsApp.StepFunctions;

namespace StepFunctionsApp.DynamicApi;

// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++
// DYNAMIC API OPENAPI GENERATOR - builds an OpenAPI 3.0.1 document covering every
// active dynamic API: one path entry per operation (basePath + opPath, without the
// /api/dynamic prefix), bearer security only where a token is set, and one schema
// component per distinct attribute domain referenced by attributeDomain/eav
// operations (DynamicApi_{DomainName}) plus a static EavRow component. Domains that
// don't exist in the store are skipped; their operations fall back to free-form
// object schemas so the spec always describes what is routable.
// +++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++

public static class DynamicApiOpenApiGenerator
{
    public static JObject Build(IReadOnlyList<DynamicApiDefinition> apis, IAttributeDomainStore domains)
    {
        var doc = new JObject
        {
            ["openapi"] = "3.0.1",
            ["info"] = new JObject { ["title"] = "StepFlow Dynamic APIs", ["version"] = "1.0" },
            ["servers"] = new JArray(new JObject { ["url"] = "/api/dynamic" }),
            ["paths"] = new JObject()
        };

        var schemas = new JObject();
        bool anyToken = false;

        foreach (var api in apis.OrderBy(a => a.NodePath, StringComparer.Ordinal).ThenBy(a => a.Name, StringComparer.Ordinal))
        {
            if (!string.IsNullOrEmpty(api.BearerToken)) anyToken = true;

            foreach (var op in api.Operations)
            {
                var pathKey = DynamicApiMatcher.JoinPaths(api.BasePath, op.Path);
                var method = op.Method.ToLowerInvariant();
                var effectiveDomain = !string.IsNullOrWhiteSpace(op.DomainName) ? op.DomainName : api.AttributeDomain;

                // Collect the domain component once per distinct effective domain.
                string? domainComponent = null;
                if ((op.HandlerType == "attributeDomain" || op.HandlerType == "eav") && !string.IsNullOrWhiteSpace(effectiveDomain))
                    domainComponent = EnsureDomainComponent(domains, schemas, effectiveDomain!);

                var operation = new JObject { ["tags"] = new JArray(api.Name) };
                operation["summary"] = string.IsNullOrWhiteSpace(op.Description) ? $"{op.Method} {pathKey}" : op.Description;

                // Path parameters from template segments + eav GET query params.
                var parameters = new JArray();
                foreach (var segment in DynamicApiMatcher.PathSegments(pathKey))
                    if (DynamicApiMatcher.IsTemplate(segment))
                        parameters.Add(new JObject
                        {
                            ["name"] = segment[1..^1],
                            ["in"] = "path",
                            ["required"] = true,
                            ["schema"] = new JObject { ["type"] = "string" }
                        });
                bool eavLookup = IsEavGetLookup(api, op);
                if (op.HandlerType == "eav" && op.Method.Equals("GET", StringComparison.OrdinalIgnoreCase))
                {
                    var routeTemplates = DynamicApiMatcher.PathSegments(DynamicApiMatcher.JoinPaths(api.BasePath, op.Path)).Count(s => DynamicApiMatcher.IsTemplate(s));
                    if (eavLookup)
                    {
                        operation["description"] = "Single-row lookup by EntityId (falls back to RowKeyId): 404 when nothing matches, an object for a unique match, an array when the entityId matched several rows.";
                        parameters.Add(new JObject { ["name"] = "fields", ["in"] = "query", ["required"] = false, ["schema"] = new JObject { ["type"] = "string" }, ["description"] = "Comma-separated row fields to project; rowKeyId is always kept" });
                    }
                    else
                    {
                        if (routeTemplates > 0) operation["description"] = "Collection filtered by entityId = the path parameter value, with the full query language applied.";
                        parameters.Add(new JObject { ["name"] = "entityId", ["in"] = "query", ["required"] = false, ["schema"] = new JObject { ["type"] = "string" }, ["description"] = "Filter rows where entityId equals this value" });
                        parameters.Add(new JObject { ["name"] = "sort", ["in"] = "query", ["required"] = false, ["schema"] = new JObject { ["type"] = "string" }, ["description"] = "Comma-separated fields; '-' prefix descends (e.g. capturedAtUtc,-entityId)" });
                        parameters.Add(new JObject { ["name"] = "page", ["in"] = "query", ["required"] = false, ["schema"] = new JObject { ["type"] = "integer" }, ["description"] = "1-based page number; mutually exclusive with offset" });
                        parameters.Add(new JObject { ["name"] = "limit", ["in"] = "query", ["required"] = false, ["schema"] = new JObject { ["type"] = "integer", ["default"] = 100 } });
                        parameters.Add(new JObject { ["name"] = "offset", ["in"] = "query", ["required"] = false, ["schema"] = new JObject { ["type"] = "integer" }, ["description"] = "Mutually exclusive with page" });
                        parameters.Add(new JObject { ["name"] = "fields", ["in"] = "query", ["required"] = false, ["schema"] = new JObject { ["type"] = "string" }, ["description"] = "Comma-separated row fields to project; rowKeyId is always kept" });
                    }
                }
                if (parameters.Count > 0) operation["parameters"] = parameters;

                // Request body for write methods.
                if (op.Method is "POST" or "PUT" or "PATCH")
                {
                    JObject bodySchema = op.HandlerType switch
                    {
                        "attributeDomain" => DomainRefOrObject(domainComponent),
                        "eav" => new JObject { ["type"] = "object", ["description"] = "Row values; optional top-level entityId/entityType" },
                        _ => new JObject { ["type"] = "object", ["description"] = "Free-form input (path params and query are merged in)" }
                    };
                    operation["requestBody"] = new JObject
                    {
                        ["required"] = true,
                        ["content"] = new JObject { ["application/json"] = new JObject { ["schema"] = bodySchema } }
                    };
                }

                // Responses: success (201 for eav POST) + 401 when tokened + 404/500.
                var responses = new JObject();
                string successCode = op.HandlerType == "eav" && op.Method.Equals("POST", StringComparison.OrdinalIgnoreCase) ? "201" : "200";
                responses[successCode] = new JObject { ["description"] = eavLookup ? "Matched row(s): an object for a unique match, an array when the entityId matched several rows" : SuccessDescription(op), ["schema"] = SuccessSchema(op, domainComponent, eavLookup) };
                if (!string.IsNullOrEmpty(api.BearerToken))
                    responses["401"] = new JObject { ["description"] = "Invalid or missing bearer token" };
                responses["404"] = new JObject { ["description"] = "Resource not found" };
                responses["500"] = new JObject { ["description"] = "Internal server error" };
                operation["responses"] = responses;

                if (!string.IsNullOrEmpty(api.BearerToken))
                    operation["security"] = new JArray(new JObject { ["bearerAuth"] = new JArray() });

                var pathItem = doc["paths"][pathKey] as JObject ?? new JObject();
                pathItem[method] = operation;
                doc["paths"][pathKey] = pathItem;
            }
        }

        schemas["EavRow"] = new JObject
        {
            ["type"] = "object",
            ["properties"] = new JObject
            {
                ["rowKeyId"] = new JObject { ["type"] = "string" },
                ["entityId"] = new JObject { ["description"] = "Entity identifier (any JSON scalar)" },
                ["entityType"] = new JObject { ["type"] = "string" },
                ["sourceTaskId"] = new JObject { ["type"] = "string" },
                ["capturedAtUtc"] = new JObject { ["type"] = "string", ["format"] = "date-time" },
                ["values"] = new JObject { ["type"] = "object" }
            }
        };

        var components = new JObject();
        if (anyToken)
            components["securitySchemes"] = new JObject { ["bearerAuth"] = new JObject { ["type"] = "http", ["scheme"] = "bearer" } };
        components["schemas"] = schemas;
        doc["components"] = components;

        return doc;
    }

    /// <summary>Adds the DynamicApi_{DomainName} component when the domain exists in the store; null (and no component) otherwise.</summary>
    private static string? EnsureDomainComponent(IAttributeDomainStore domains, JObject schemas, string domainName)
    {
        var componentName = $"DynamicApi_{domainName}";
        if (schemas[componentName] != null) return componentName;

        var (domain, _) = domains.GetByName(domainName);
        if (domain == null) return null; // skip the component - operations fall back to free-form object schemas

        var properties = new JObject();
        var required = new JArray();
        foreach (var attr in domain.Attributes ?? new List<EntityAttribute>())
        {
            var schema = TypeSchema(attr.DataType);
            var description = !string.IsNullOrWhiteSpace(attr.DisplayName) ? attr.DisplayName : attr.Description;
            if (!string.IsNullOrEmpty(description)) schema["description"] = description;
            properties[attr.AttributeName] = schema;
            if (IsRequired(attr.ValidationSchemaJson)) required.Add(attr.AttributeName);
        }

        var component = new JObject { ["type"] = "object", ["properties"] = properties };
        if (required.Count > 0) component["required"] = required;
        schemas[componentName] = component;
        return componentName;
    }

    private static JObject DomainRefOrObject(string? domainComponent) =>
        domainComponent != null ? new JObject { ["$ref"] = $"#/components/schemas/{domainComponent}" } : new JObject { ["type"] = "object" };

    private static string SuccessDescription(DynamicApiOperation op) =>
        (op.HandlerType, op.Method.ToUpperInvariant()) switch
        {
            ("eav", "GET") => "Matching rows with total count",
            ("eav", "POST") => "Created row including assigned rowKeyId",
            ("attributeDomain", "GET") => "Current attribute domain definition",
            _ => "Operation succeeded"
        };

    private static JToken SuccessSchema(DynamicApiOperation op, string? domainComponent, bool eavLookup)
    {
        var method = op.Method.ToUpperInvariant();
        switch (op.HandlerType)
        {
            case "attributeDomain":
                return method == "GET" ? DomainRefOrObject(domainComponent) : new JObject { ["type"] = "object" };

            case "eav":
                return method switch
                {
                    // Collection -> { rows: [...], count }; lookup -> object for a unique match, array on multiple.
                    "GET" => eavLookup
                        ? new JObject
                        {
                            ["anyOf"] = new JArray(
                                new JObject { ["$ref"] = "#/components/schemas/EavRow" },
                                new JObject { ["type"] = "array", ["items"] = new JObject { ["$ref"] = "#/components/schemas/EavRow" } })
                        }
                        : new JObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JObject
                            {
                                ["rows"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["$ref"] = "#/components/schemas/EavRow" } },
                                ["count"] = new JObject { ["type"] = "integer" }
                            }
                        },
                    "POST" or "PUT" or "PATCH" => new JObject { ["$ref"] = "#/components/schemas/EavRow" },
                    _ => new JObject { ["type"] = "object" } // DELETE -> { status, rowKeyId }
                };

            default: // flow | dataExchange
                return new JObject { ["type"] = "object" };
        }
    }

    /// <summary>Static twin of EavGetMapper's runtime mode for an eav GET op (save-time validation caps path params at one).</summary>
    private static bool IsEavGetLookup(DynamicApiDefinition api, DynamicApiOperation op) =>
        op.HandlerType == "eav" && op.Method.Equals("GET", StringComparison.OrdinalIgnoreCase)
        && DynamicApiMatcher.PathSegments(DynamicApiMatcher.JoinPaths(api.BasePath, op.Path)).Count(s => DynamicApiMatcher.IsTemplate(s)) > 0
        && DynamicApiMatcher.PathSegments(op.Path).Length <= 1;

    /// <summary>OpenAPI schema for an attribute type (ordinals per AttributeDataType: String=0 … Array=5).</summary>
    private static JObject TypeSchema(AttributeDataType dataType) => dataType switch
    {
        AttributeDataType.String => new() { ["type"] = "string" },
        AttributeDataType.Boolean => new() { ["type"] = "boolean" },
        AttributeDataType.Number => new() { ["type"] = "number" },
        AttributeDataType.Date => new() { ["type"] = "string", ["format"] = "date-time" },
        AttributeDataType.Object => new() { ["type"] = "object" },
        AttributeDataType.Array => new() { ["type"] = "array" },
        _ => new() // unknown future type - leave schema open
    };

    /// <summary>True when the validation schema JSON parses and declares "required": true.</summary>
    private static bool IsRequired(string? validationSchemaJson)
    {
        if (string.IsNullOrWhiteSpace(validationSchemaJson)) return false;
        try
        {
            var parsed = JToken.Parse(validationSchemaJson);
            return parsed is JObject obj && obj["required"]?.Type == JTokenType.Boolean && (bool)obj["required"]!;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
