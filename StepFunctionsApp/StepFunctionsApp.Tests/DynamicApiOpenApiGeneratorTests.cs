using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using StepFlow.DataModel.Entities;
using StepFlow.DataModel.Entities.MetaData;
using StepFlow.DynamicApi;
using StepFunctionsApp.DynamicApi;
using StepFunctionsApp.StepFunctions;
using Xunit;

namespace StepFunctionsApp.Tests
{
    /// <summary>
    /// DynamicApiOpenApiGenerator - OpenAPI 3.0.1 document shape: paths per operation,
    /// bearer security only when tokened, domain components from the store (skipped when
    /// missing), eav query params and success schemas.
    /// </summary>
    public class DynamicApiOpenApiGeneratorTests
    {
        private sealed class FakeDomainStore : IAttributeDomainStore
        {
            private readonly Dictionary<string, AttributeDomain> _domains = new(StringComparer.OrdinalIgnoreCase);

            public string ProviderName => "fake";

            public void Add(AttributeDomain domain) => _domains[domain.AttributeDomainName] = domain;

            public IReadOnlyList<AttributeDomain> GetAll() => _domains.Values.ToList();

            public (AttributeDomain? domain, SchemaDefinition? schema) GetByName(string domainName) =>
                _domains.TryGetValue(domainName, out var d) ? (d, null) : (null, null);

            public void Save(AttributeDomain domain, SchemaDefinition? schema = null) => Add(domain);

            public bool Delete(string domainName) => _domains.Remove(domainName);
        }

        private static DynamicApiDefinition Api(string id, string basePath, params DynamicApiOperation[] ops) => new()
        {
            Id = id, Name = id, NodePath = "Org", BasePath = basePath, Operations = new List<DynamicApiOperation>(ops),
        };

        [Fact]
        public void Build_AddsBearerSecurity_OnlyWhenTokenPresent()
        {
            var open = Api("open", "/a", new DynamicApiOperation { Method = "GET", Path = "", HandlerType = "flow", FlowId = "f" });
            var tokened = Api("tokened", "/b", new DynamicApiOperation { Method = "GET", Path = "", HandlerType = "flow", FlowId = "f" });
            tokened.BearerToken = "s3cret";

            var doc = DynamicApiOpenApiGenerator.Build(new[] { open, tokened }, new FakeDomainStore());

            Assert.Equal("3.0.1", (string)doc["openapi"]!);
            Assert.NotNull(doc["components"]!["securitySchemes"]!["bearerAuth"]);
            Assert.Equal("http", (string)doc["components"]!["securitySchemes"]!["bearerAuth"]!["type"]!);

            var tokenedOp = doc["paths"]!["/b"]!["get"]!;
            Assert.NotNull(tokenedOp["security"]!);
            Assert.NotNull(tokenedOp["responses"]!["401"]);

            var openOp = doc["paths"]!["/a"]!["get"]!;
            Assert.Null(openOp["security"]);
            Assert.Null(openOp["responses"]!["401"]);
        }

        [Fact]
        public void Build_NoSecurityScheme_WhenNoApiHasToken()
        {
            var doc = DynamicApiOpenApiGenerator.Build(
                new[] { Api("open", "/a", new DynamicApiOperation { Method = "GET", Path = "", HandlerType = "flow", FlowId = "f" }) },
                new FakeDomainStore());

            Assert.Null(doc["components"]!["securitySchemes"]);
        }

        [Fact]
        public void Build_AddsDomainComponent_FromStoreAttributes()
        {
            var domains = new FakeDomainStore();
            domains.Add(new AttributeDomain
            {
                AttributeDomainName = "Widget",
                Attributes = new List<EntityAttribute>
                {
                    new() { AttributeName = "name", DataType = AttributeDataType.String, ValidationSchemaJson = "{\"required\": true}" },
                    new() { AttributeName = "count", DataType = AttributeDataType.Number },
                    new() { AttributeName = "active", DataType = AttributeDataType.Boolean, DisplayName = "Is active" },
                },
            });

            var api = Api("w", "/widgets",
                new DynamicApiOperation { Method = "GET", Path = "", HandlerType = "attributeDomain" },
                new DynamicApiOperation { Method = "POST", Path = "", HandlerType = "attributeDomain" });
            api.AttributeDomain = "Widget";

            var doc = DynamicApiOpenApiGenerator.Build(new[] { api }, domains);

            var component = doc["components"]!["schemas"]!["DynamicApi_Widget"];
            Assert.NotNull(component);
            Assert.Equal("string", (string)component!["properties"]!["name"]!["type"]!);
            Assert.Equal("number", (string)component["properties"]!["count"]!["type"]!);
            Assert.Equal("boolean", (string)component["properties"]!["active"]!["type"]!);
            Assert.Equal("Is active", (string)component["properties"]!["active"]!["description"]!);
            Assert.Equal(new[] { "name" }, component["required"]!.Select(t => (string)t!).ToArray());

            // Both GET success and POST request body reference the component.
            var getOp = doc["paths"]!["/widgets"]!["get"]!;
            Assert.Equal("#/components/schemas/DynamicApi_Widget", (string)getOp["responses"]!["200"]!["schema"]!["$ref"]!);
            var postOp = doc["paths"]!["/widgets"]!["post"]!;
            Assert.Equal("#/components/schemas/DynamicApi_Widget", (string)postOp["requestBody"]!["content"]!["application/json"]!["schema"]!["$ref"]!);
        }

        [Fact]
        public void Build_SkipsMissingDomain_FallsBackToObjectSchemas()
        {
            var api = Api("w", "/rows", new DynamicApiOperation { Method = "GET", Path = "", HandlerType = "eav" });
            api.AttributeDomain = "Ghost";

            var doc = DynamicApiOpenApiGenerator.Build(new[] { api }, new FakeDomainStore());

            Assert.Null(doc["components"]!["schemas"]!["DynamicApi_Ghost"]);
            // eav GET success shape is still described: rows + count.
            var getOp = doc["paths"]!["/rows"]!["get"]!;
            var schema = getOp["responses"]!["200"]!["schema"]!;
            Assert.Equal("object", (string)schema["type"]!);
            Assert.Equal("#/components/schemas/EavRow", (string)schema["properties"]!["rows"]!["items"]!["$ref"]!);
        }

        [Fact]
        public void Build_AddsEavGetQueryParams_AndPathTemplateParams()
        {
            var api = Api("w", "/rows",
                new DynamicApiOperation { Method = "GET", Path = "", HandlerType = "eav" },
                new DynamicApiOperation { Method = "PUT", Path = "/{rowKeyId}", HandlerType = "eav" });
            api.AttributeDomain = "Widget";

            var doc = DynamicApiOpenApiGenerator.Build(new[] { api }, new FakeDomainStore());

            var getParams = doc["paths"]!["/rows"]!["get"]!["parameters"]!.Select(p => (string)p!["name"]!).ToArray();
            Assert.Equal(new[] { "entityId", "sort", "page", "limit", "offset", "fields" }, getParams); // full query language for collection mode

            var putOp = doc["paths"]!["/rows/{rowKeyId}"]!["put"]!;
            var pathParam = putOp["parameters"]!.Single(p => (string)p!["in"] == "path");
            Assert.Equal("rowKeyId", (string)pathParam["name"]!);
            Assert.True((bool)pathParam["required"]!);

            // eav POST success is 201, not 200.
            var postApi = Api("p", "/rows2", new DynamicApiOperation { Method = "POST", Path = "", HandlerType = "eav" });
            postApi.AttributeDomain = "Widget";
            var doc2 = DynamicApiOpenApiGenerator.Build(new[] { postApi }, new FakeDomainStore());
            var responses = doc2["paths"]!["/rows2"]!["post"]!["responses"]!;
            Assert.NotNull(responses["201"]);
            Assert.Null(responses["200"]);
        }

        [Fact]
        public void Build_EavLookupOp_DocumentsSingleRowContract()
        {
            var api = Api("l", "/lookup", new DynamicApiOperation { Method = "GET", Path = "/{id}", HandlerType = "eav" });
            api.AttributeDomain = "Widget";

            var doc = DynamicApiOpenApiGenerator.Build(new[] { api }, new FakeDomainStore());
            var op = doc["paths"]!["/lookup/{id}"]!["get"]!;

            // Path param + the fields projection only - no collection query params.
            Assert.Equal(new[] { "id", "fields" }, op["parameters"]!.Select(p => (string)p!["name"]!).ToArray());
            var idParam = op["parameters"]!.Single(p => (string)p!["in"] == "path");
            Assert.True((bool)idParam["required"]!);

            // Description + anyOf success schema: object for a unique match, array on several.
            Assert.Contains("Single-row lookup", (string)op["description"]!, StringComparison.OrdinalIgnoreCase);
            var schema = op["responses"]!["200"]!["schema"]!;
            var anyOf = (JArray)schema["anyOf"]!;
            Assert.Equal(2, anyOf.Count);
            Assert.Equal("#/components/schemas/EavRow", (string)anyOf[0]!["$ref"]!);
            Assert.Equal("array", (string)anyOf[1]!["type"]!);
        }

        [Fact]
        public void Build_EavEntityFilterOp_DocumentsCollectionWithEntityId()
        {
            var api = Api("f", "", new DynamicApiOperation { Method = "GET", Path = "/{org}/comments", HandlerType = "eav" });
            api.AttributeDomain = "Widget";

            var doc = DynamicApiOpenApiGenerator.Build(new[] { api }, new FakeDomainStore());
            var op = doc["paths"]!["/{org}/comments"]!["get"]!;

            // Path param + full query language (entityId is merged in at runtime from the path value).
            Assert.Equal(new[] { "org", "entityId", "sort", "page", "limit", "offset", "fields" }, op["parameters"]!.Select(p => (string)p!["name"]!).ToArray());
            Assert.Contains("filtered by entityId", (string)op["description"]!, StringComparison.OrdinalIgnoreCase);

            // Collection success schema: rows + count.
            var schema = op["responses"]!["200"]!["schema"]!;
            Assert.NotNull(schema["properties"]!["rows"]);
            Assert.NotNull(schema["properties"]!["count"]);
        }
    }
}
