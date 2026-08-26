using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using StepFlow.DataModel.Entities;
using StepFlow.DataModel.Entities.MetaData;
using StepFunctionsApp.Controllers;
using StepFunctionsApp.StepFunctions;

namespace StepFunctionsApp.Tests
{
    /// <summary>
    /// SchemaDefinition store across both providers (json + sqlite): round-trip, versioned upsert
    /// semantics, latest-version resolution, delete, save validation — plus the controller's 400/409/404 paths.
    /// </summary>
    public class SchemaDefinitionStoreTests : IDisposable
    {
        private readonly string _dir;

        public SchemaDefinitionStoreTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "stepflow-schemastore-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private string SubDir(string name) => Directory.CreateDirectory(Path.Combine(_dir, name)).FullName;

        // ── helpers ────────────────────────────────────────────────────────────────

        private static SchemaDefinition SampleSchema(string name = "Order", string version = "1") => new()
        {
            SchemaDefinitionName = name,
            Version = version,
            Description = "Order schema",
            Definition = """{"type":"object","properties":{"amount":{"type":"number"}}}"""
        };

        private void ForEachSchemaStore(Action<ISchemaDefinitionStore> test)
        {
            test(new JsonFileSchemaDefinitionStore(Path.Combine(SubDir("schemas-json"), "schema_definitions.json"), NullLogger<JsonFileSchemaDefinitionStore>.Instance));
            test(new SqliteSchemaDefinitionStore(Path.Combine(SubDir("schemas-sqlite"), "schemas.db"), NullLogger<SqliteSchemaDefinitionStore>.Instance));
        }

        // ── schema definition store ────────────────────────────────────────────────

        [Fact]
        public void SchemaStore_SaveGetRoundTrip()
        {
            ForEachSchemaStore(store =>
            {
                var def = SampleSchema();
                store.Save(def);

                var loaded = store.Get("Order", "1");
                Assert.NotNull(loaded);
                Assert.Equal("Order schema", loaded!.Description);
                Assert.Equal(def.Definition, loaded.Definition);
                // Case-insensitive name lookup (mirrors the attribute domain stores).
                Assert.NotNull(store.Get("order"));
            });
        }

        [Fact]
        public void SchemaStore_GetAllReturnsEverySavedVersion()
        {
            ForEachSchemaStore(store =>
            {
                store.Save(SampleSchema());
                store.Save(SampleSchema(version: "2"));
                store.Save(SampleSchema(name: "Invoice", version: "1"));

                Assert.Equal(3, store.GetAll().Count);
                Assert.Equal(2, store.GetAll().Where(s => s.SchemaDefinitionName == "Order").Count());
            });
        }

        [Fact]
        public void SchemaStore_SaveIsUpsertByNameAndVersion()
        {
            ForEachSchemaStore(store =>
            {
                store.Save(SampleSchema());
                var updated = SampleSchema(); // same (name, "1") pair
                updated.Description = "Renamed in place";
                store.Save(updated);

                Assert.Single(store.GetAll());
                Assert.Equal("Renamed in place", store.Get("Order", "1")!.Description);
            });
        }

        [Fact]
        public void SchemaStore_GetWithoutVersionResolvesLatest()
        {
            ForEachSchemaStore(store =>
            {
                store.Save(SampleSchema());
                var v2 = SampleSchema(version: "2");
                v2.Description = "Second edition";
                store.Save(v2);

                // Get(name) resolves the highest saved version.
                Assert.Equal("2", store.Get("Order")!.Version);
                Assert.Equal("Second edition", store.Get("Order")!.Description);

                // Older versions remain retrievable by exact pair.
                Assert.Equal("1", store.Get("Order", "1")!.Version);
            });
        }

        [Fact]
        public void SchemaStore_DeleteRemovesOnlyTheNamedVersion()
        {
            ForEachSchemaStore(store =>
            {
                store.Save(SampleSchema());
                store.Save(SampleSchema(version: "2"));

                Assert.False(store.Delete("Order", "9")); // never saved
                Assert.True(store.Delete("Order", "1"));

                Assert.Null(store.Get("Order", "1"));
                Assert.Equal("2", store.Get("Order")!.Version); // latest now resolves to v2
            });
        }

        [Fact]
        public void SchemaStore_SaveValidatesNameVersionAndDefinition()
        {
            ForEachSchemaStore(store =>
            {
                var noName = SampleSchema();
                noName.SchemaDefinitionName = "";
                Assert.Throws<ArgumentException>(() => store.Save(noName));

                var noVersion = SampleSchema();
                noVersion.Version = "  ";
                Assert.Throws<ArgumentException>(() => store.Save(noVersion));

                var badJson = SampleSchema();
                badJson.Definition = "{not json";
                Assert.Throws<ArgumentException>(() => store.Save(badJson));

                var emptyDef = SampleSchema();
                emptyDef.Definition = "";
                Assert.Throws<ArgumentException>(() => store.Save(emptyDef));

                Assert.Empty(store.GetAll()); // nothing persisted by the rejected saves
            });
        }

        [Fact]
        public void SchemaStore_ProviderNames()
        {
            var json = new JsonFileSchemaDefinitionStore(Path.Combine(SubDir("schemas-json"), "schema_definitions.json"), NullLogger<JsonFileSchemaDefinitionStore>.Instance);
            var sqlite = new SqliteSchemaDefinitionStore(Path.Combine(SubDir("schemas-sqlite"), "schemas.db"), NullLogger<SqliteSchemaDefinitionStore>.Instance);
            Assert.Equal("json", json.ProviderName);
            Assert.Equal("sqlite", sqlite.ProviderName);
        }

        // ── controller: 400 / 409 / 404 paths ──────────────────────────────────────

        [Fact]
        public void Controller_Save_InvalidDefinition_ReturnsBadRequest()
        {
            var store = new JsonFileSchemaDefinitionStore(Path.Combine(SubDir("schemas-json"), "schema_definitions.json"), NullLogger<JsonFileSchemaDefinitionStore>.Instance);
            var domains = new JsonFileAttributeDomainStore(Path.Combine(SubDir("domains-json"), "attribute_domains.json"), NullLogger<JsonFileAttributeDomainStore>.Instance);
            var controller = new SchemaDefinitionsController(store, domains);

            var noName = SampleSchema();
            noName.SchemaDefinitionName = "";
            Assert.IsType<BadRequestObjectResult>(controller.Save(noName));

            var badJson = SampleSchema();
            badJson.Definition = "{not json";
            Assert.IsType<BadRequestObjectResult>(controller.Save(badJson));

            Assert.Empty(store.GetAll()); // rejected saves persist nothing
        }

        [Fact]
        public void Controller_Delete_VersionReferencedByDomain_ReturnsConflict()
        {
            var store = new JsonFileSchemaDefinitionStore(Path.Combine(SubDir("schemas-json"), "schema_definitions.json"), NullLogger<JsonFileSchemaDefinitionStore>.Instance);
            var domains = new JsonFileAttributeDomainStore(Path.Combine(SubDir("domains-json"), "attribute_domains.json"), NullLogger<JsonFileAttributeDomainStore>.Instance);
            var controller = new SchemaDefinitionsController(store, domains);

            store.Save(SampleSchema()); // (Order, v1)
            var domain = new AttributeDomain
            {
                AttributeDomainName = "Order",
                Description = "Captured orders",
                Attributes = new List<EntityAttribute>
                {
                    new() { AttributeName = "CustomerName", DataType = AttributeDataType.String }
                },
                SchemaDefinition = new SchemaDefinition { SchemaDefinitionName = "Order", Version = "1" }
            };
            domains.Save(domain, domain.SchemaDefinition);

            var result = controller.Delete("Order", "1");
            var conflict = Assert.IsType<ConflictObjectResult>(result);
            var payload = JObject.FromObject(conflict.Value!);
            Assert.Contains("Order", ((JArray)payload["domains"]!).Select(d => d.ToString()).ToList());

            // The referenced version survives the rejected delete.
            Assert.NotNull(store.Get("Order", "1"));
        }

        [Fact]
        public void Controller_Delete_UnknownVersion_ReturnsNotFound()
        {
            var store = new JsonFileSchemaDefinitionStore(Path.Combine(SubDir("schemas-json"), "schema_definitions.json"), NullLogger<JsonFileSchemaDefinitionStore>.Instance);
            var domains = new JsonFileAttributeDomainStore(Path.Combine(SubDir("domains-json"), "attribute_domains.json"), NullLogger<JsonFileAttributeDomainStore>.Instance);
            var controller = new SchemaDefinitionsController(store, domains);

            Assert.IsType<NotFoundObjectResult>(controller.Delete("Nope", "1"));
        }
    }
}
