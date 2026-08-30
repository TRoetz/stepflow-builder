using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using StepFlow.DataModel.Entities;
using StepFlow.DataModel.Entities.MetaData;
using StepFunctionsApp.StepFunctions;

namespace StepFunctionsApp.Tests
{
    /// <summary>
    /// FormDefinition and AttributeDomain stores across both providers (json + sqlite): round-trip,
    /// upsert semantics, case-insensitive lookup, delete, save validation. The form store is versioned:
    /// one entry per (FormId, Version), at most one current version per form, legacy layouts migrate in place.
    /// </summary>
    public class FormCaptureStoresTests : IDisposable
    {
        private readonly string _dir;

        public FormCaptureStoresTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "stepflow-formstore-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private string SubDir(string name) => Directory.CreateDirectory(Path.Combine(_dir, name)).FullName;

        // ── helpers ────────────────────────────────────────────────────────────────

        private static FormDefinition SampleForm(string formId = "order-approval", string? domainName = "Order") => new()
        {
            FormId = formId,
            Title = "Approve order",
            AttributeDomainName = domainName,
            Page = JObject.Parse("""{"RootElements":[{"Type":"TextBox","Name":"note"}]}""")
        };

        private static AttributeDomain SampleDomain(string name = "Order") => new()
        {
            AttributeDomainName = name,
            Description = "Test domain",
            Attributes = new List<EntityAttribute>
            {
                new() { AttributeName = "CustomerName", DataType = AttributeDataType.String, DisplayName = "Customer", ValidationSchemaJson = "{\"required\":true}" },
                new() { AttributeName = "Amount", DataType = AttributeDataType.Number, DisplayName = "Amount" }
            }
        };

        private void ForEachFormStore(Action<IFormDefinitionStore> test)
        {
            test(new JsonFileFormDefinitionStore(SubDir("forms-json"), NullLogger<JsonFileFormDefinitionStore>.Instance));
            test(new SqliteFormDefinitionStore(Path.Combine(SubDir("forms-sqlite"), "forms.db"), NullLogger<SqliteFormDefinitionStore>.Instance));
        }

        private void ForEachDomainStore(Action<IAttributeDomainStore> test)
        {
            test(new JsonFileAttributeDomainStore(Path.Combine(SubDir("domains-json"), "attribute_domains.json"), NullLogger<JsonFileAttributeDomainStore>.Instance));
            test(new SqliteAttributeDomainStore(Path.Combine(SubDir("domains-sqlite"), "domains.db"), NullLogger<SqliteAttributeDomainStore>.Instance));
        }

        // ── form definition store ──────────────────────────────────────────────────

        [Fact]
        public void FormStore_SaveGetRoundTrip()
        {
            ForEachFormStore(store =>
            {
                var def = SampleForm();
                store.Save(def);

                var loaded = store.Get("order-approval");
                Assert.NotNull(loaded);
                Assert.Equal("Approve order", loaded!.Title);
                Assert.Equal("Order", loaded.AttributeDomainName);
                Assert.True(JToken.DeepEquals(def.Page, loaded.Page));
            });
        }

        [Fact]
        public void FormStore_GetAllReturnsEverySavedForm()
        {
            ForEachFormStore(store =>
            {
                store.Save(SampleForm("alpha"));
                store.Save(SampleForm("beta", domainName: null));

                var all = store.GetAll();
                Assert.Equal(2, all.Count);
                Assert.Contains(all, f => f.FormId == "alpha");
                Assert.Contains(all, f => f.FormId == "beta" && f.AttributeDomainName == null);
            });
        }

        [Fact]
        public void FormStore_SaveIsUpsertByFormId()
        {
            ForEachFormStore(store =>
            {
                store.Save(SampleForm());
                var updated = SampleForm();
                updated.Title = "Renamed";
                store.Save(updated);

                Assert.Single(store.GetAll());
                Assert.Equal("Renamed", store.Get("order-approval")!.Title);
            });
        }

        [Fact]
        public void FormStore_DeleteRemovesOnlyKnownIds()
        {
            ForEachFormStore(store =>
            {
                store.Save(SampleForm());
                Assert.True(store.Delete("order-approval"));
                Assert.Null(store.Get("order-approval"));
                Assert.False(store.Delete("order-approval"));
            });
        }

        [Fact]
        public void FormStore_SaveValidatesFormIdAndPage()
        {
            ForEachFormStore(store =>
            {
                var noId = SampleForm();
                noId.FormId = "";
                Assert.Throws<ArgumentException>(() => store.Save(noId));

                var badPage = SampleForm("bad-page");
                badPage.Page = new JObject(); // no RootElements array
                Assert.Throws<ArgumentException>(() => store.Save(badPage));

                Assert.Empty(store.GetAll());
            });
        }

        [Fact]
        public void FormStore_ProviderNames()
        {
            var json = new JsonFileFormDefinitionStore(SubDir("names-json"), NullLogger<JsonFileFormDefinitionStore>.Instance);
            var sqlite = new SqliteFormDefinitionStore(Path.Combine(SubDir("names-sqlite"), "forms.db"), NullLogger<SqliteFormDefinitionStore>.Instance);
            Assert.Equal("json", json.ProviderName);
            Assert.Equal("sqlite", sqlite.ProviderName);
        }

        // ── form definition store: versioning ───────────────────────────────────────

        [Fact]
        public void FormStore_SaveCreatesDistinctVersions()
        {
            ForEachFormStore(store =>
            {
                store.Save(SampleForm()); // v1, current by default
                var v2 = SampleForm();
                v2.Version = "2";
                v2.Title = "Second edition";
                store.Save(v2);

                Assert.Equal(2, store.GetAll().Count);

                // Get(id) resolves the current version.
                var current = store.Get("order-approval");
                Assert.NotNull(current);
                Assert.Equal("2", current!.Version);
                Assert.True(current.IsCurrentVersion);
                Assert.Equal("Second edition", current.Title);

                // The old version is still retrievable by exact pair, no longer current.
                var v1 = store.Get("order-approval", "1");
                Assert.NotNull(v1);
                Assert.False(v1!.IsCurrentVersion);
                Assert.Equal("Approve order", v1.Title);
            });
        }

        [Fact]
        public void FormStore_SaveUpsertsByFormIdAndVersion()
        {
            ForEachFormStore(store =>
            {
                store.Save(SampleForm());
                var updated = SampleForm(); // same (id, "1") pair
                updated.Title = "Renamed in place";
                store.Save(updated);

                Assert.Single(store.GetAll());
                Assert.Equal("Renamed in place", store.Get("order-approval")!.Title);
            });
        }

        [Fact]
        public void FormStore_GetByVersionReturnsNullForUnknownVersion()
        {
            ForEachFormStore(store =>
            {
                store.Save(SampleForm());
                Assert.Null(store.Get("order-approval", "9"));
                Assert.Null(store.Get("missing-form", "1"));
            });
        }

        [Fact]
        public void FormStore_CurrentVersionFlagIsExclusivePerForm()
        {
            ForEachFormStore(store =>
            {
                store.Save(SampleForm()); // v1 current
                var v2 = SampleForm();
                v2.Version = "2";
                store.Save(v2); // v2 becomes current, v1's flag is cleared

                var all = store.GetAll().Where(f => f.FormId == "order-approval").ToList();
                Assert.Equal(2, all.Count);
                Assert.Single(all.Where(f => f.IsCurrentVersion));
                Assert.Equal("2", all.Single(f => f.IsCurrentVersion).Version);
            });
        }

        [Fact]
        public void FormStore_DeleteRemovesEverySavedVersion()
        {
            ForEachFormStore(store =>
            {
                store.Save(SampleForm());
                var v2 = SampleForm();
                v2.Version = "2";
                store.Save(v2);

                Assert.True(store.Delete("order-approval"));
                Assert.Empty(store.GetAll());
                Assert.False(store.Delete("order-approval"));
            });
        }

        [Fact]
        public void FormStore_SaveValidatesVersion()
        {
            ForEachFormStore(store =>
            {
                var noVersion = SampleForm();
                noVersion.Version = "";
                Assert.Throws<ArgumentException>(() => store.Save(noVersion));
                Assert.Empty(store.GetAll());
            });
        }

        [Fact]
        public void JsonFormStore_LegacySingleObjectFileIsUpgradedToVersionArray()
        {
            var dir = SubDir("forms-legacy");
            // A pre-versioning document: a single FormDefinition object at the root (PascalCase keys, as written by the old store).
            File.WriteAllText(Path.Combine(dir, "order-approval.json"), JsonConvert.SerializeObject(new
            {
                FormId = "order-approval",
                Title = "Legacy form",
                AttributeDomainName = (string?)null,
                Page = new JObject { ["RootElements"] = new JArray(new JObject { ["Type"] = "TextBox", ["Name"] = "note" }) }
            }, Formatting.Indented));

            var store = new JsonFileFormDefinitionStore(dir, NullLogger<JsonFileFormDefinitionStore>.Instance);
            var loaded = store.Get("order-approval");
            Assert.NotNull(loaded);
            Assert.Equal("1", loaded!.Version);
            Assert.True(loaded.IsCurrentVersion);
            Assert.Equal("Legacy form", loaded.Title);

            // The file on disk is now the versioned array layout.
            var onDisk = JToken.Parse(File.ReadAllText(Path.Combine(dir, "order-approval.json")));
            Assert.IsType<JArray>(onDisk);
        }

        [Fact]
        public void SqliteFormStore_LegacyUiFormsTableIsMigratedToVersionedLayout()
        {
            var dbPath = Path.Combine(SubDir("forms-legacy-sqlite"), "forms.db");
            using (var conn = StepFlowDataDb.Open(dbPath))
            {
                // Pre-versioning layout: one row per form, PK on form_id only.
                var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE ui_forms (
                      form_id TEXT PRIMARY KEY, title TEXT, description TEXT,
                      attribute_domain_name TEXT, schema_definition_name TEXT, page_json TEXT NOT NULL,
                      updated_at_utc TEXT);
                    INSERT INTO ui_forms (form_id, title, page_json) VALUES ('order-approval', 'Legacy form', '{"RootElements":[{"Type":"TextBox","Name":"note"}]}');
                    """;
                cmd.ExecuteNonQuery();
            }

            var store = new SqliteFormDefinitionStore(dbPath, NullLogger<SqliteFormDefinitionStore>.Instance);
            var loaded = store.Get("order-approval");
            Assert.NotNull(loaded);
            Assert.Equal("1", loaded!.Version);
            Assert.True(loaded.IsCurrentVersion);
            Assert.Equal("Legacy form", loaded.Title);

            // The migrated table accepts a second version (composite PK works).
            var v2 = SampleForm();
            v2.Version = "2";
            store.Save(v2);
            Assert.Equal(2, store.GetAll().Count);
        }

        // ── attribute domain store ─────────────────────────────────────────────────

        [Fact]
        public void DomainStore_SaveGetByNameRoundTrip_CaseInsensitive()
        {
            ForEachDomainStore(store =>
            {
                store.Save(SampleDomain());

                var (domain, schema) = store.GetByName("ORDER"); // different case on purpose
                Assert.NotNull(domain);
                Assert.Null(schema);
                Assert.Equal(2, domain!.Attributes.Count);
                var customer = domain.Attributes.Single(a => a.AttributeName == "CustomerName");
                Assert.Equal(AttributeDataType.String, customer.DataType);
                Assert.Equal("{\"required\":true}", customer.ValidationSchemaJson);
            });
        }

        [Fact]
        public void DomainStore_SaveIsUpsertAndReplacesAttributeSetWholesale()
        {
            ForEachDomainStore(store =>
            {
                store.Save(SampleDomain());

                var reduced = SampleDomain();
                reduced.Attributes = new List<EntityAttribute>
                {
                    new() { AttributeName = "Only", DataType = AttributeDataType.Boolean }
                };
                store.Save(reduced);

                Assert.Single(store.GetAll());
                var (domain, _) = store.GetByName("Order");
                var attrs = domain!.Attributes.ToList();
                Assert.Single(attrs);
                Assert.Equal("Only", attrs[0].AttributeName);
            });
        }

        [Fact]
        public void DomainStore_SchemaLinkIsStoredAndReturned()
        {
            ForEachDomainStore(store =>
            {
                var schema = new SchemaDefinition
                {
                    SchemaDefinitionName = "Order",
                    Version = "1",
                    Description = "Order schema"
                };
                store.Save(SampleDomain(), schema);

                var (domain, linked) = store.GetByName("Order");
                Assert.NotNull(domain);
                Assert.NotNull(linked);
                Assert.Equal("Order", linked!.SchemaDefinitionName);
            });
        }

        [Fact]
        public void DomainStore_DeleteRemovesOnlyKnownNames()
        {
            ForEachDomainStore(store =>
            {
                store.Save(SampleDomain());
                Assert.True(store.Delete("Order"));
                var (domain, _) = store.GetByName("Order");
                Assert.Null(domain);
                Assert.False(store.Delete("Order"));
            });
        }

        [Fact]
        public void DomainStore_SaveValidatesName()
        {
            ForEachDomainStore(store =>
            {
                var unnamed = SampleDomain();
                unnamed.AttributeDomainName = "";
                Assert.Throws<ArgumentException>(() => store.Save(unnamed));
                Assert.Empty(store.GetAll());
            });
        }

        [Fact]
        public void DomainStore_ProviderNames()
        {
            var json = new JsonFileAttributeDomainStore(Path.Combine(SubDir("names-json"), "attribute_domains.json"), NullLogger<JsonFileAttributeDomainStore>.Instance);
            var sqlite = new SqliteAttributeDomainStore(Path.Combine(SubDir("names-sqlite"), "domains.db"), NullLogger<SqliteAttributeDomainStore>.Instance);
            Assert.Equal("json", json.ProviderName);
            Assert.Equal("sqlite", sqlite.ProviderName);
        }
    }
}
