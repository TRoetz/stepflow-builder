using System;
using System.IO;
using System.Linq;
using StepFlow.DynamicApi;
using StepFunctionsApp.DynamicApi;
using Xunit;

namespace StepFunctionsApp.Tests
{
    /// <summary>
    /// SqliteDynamicApiStore - id derivation (slug/uniquify), upsert semantics,
    /// active-only listing with segment-aware node prefix filter.
    /// </summary>
    public class SqliteDynamicApiStoreTests : IDisposable
    {
        private readonly string _dir;
        private readonly SqliteDynamicApiStore _store;

        public SqliteDynamicApiStoreTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "stepflow-dynamic-api-store-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _store = new SqliteDynamicApiStore(Path.Combine(_dir, "stepflow_data.db"));
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private static DynamicApiDefinition Def(string name, string nodePath = "Org", string basePath = "/x") => new()
        {
            Name = name,
            NodePath = nodePath,
            BasePath = basePath,
            Operations = new() { new DynamicApiOperation { Method = "GET", Path = "", HandlerType = "eav" } },
        };

        [Fact]
        public void Save_GeneratesSlugId_AndRoundTripsAllFields()
        {
            var def = Def("Order API");
            def.Description = "desc";
            def.AttributeDomain = "Widget";
            def.BearerToken = "tok";
            def.Operations[0] = new DynamicApiOperation { Method = "POST", Path = "/{rowKeyId}", HandlerType = "eav", DomainName = "Widget" };

            var (id, created) = _store.Save(def);

            Assert.True(created);
            Assert.Equal("Order-API", id); // SanitizeId keeps case, replaces invalid chars with '-'

            var loaded = _store.GetById(id)!;
            Assert.Equal("Order API", loaded.Name);
            Assert.Equal("desc", loaded.Description);
            Assert.Equal("Org", loaded.NodePath);
            Assert.Equal("/x", loaded.BasePath);
            Assert.Equal("Widget", loaded.AttributeDomain);
            Assert.Equal("tok", loaded.BearerToken);
            Assert.True(loaded.IsActive);
            Assert.Single(loaded.Operations);
            var op = loaded.Operations[0];
            Assert.Equal("POST", op.Method);
            Assert.Equal("/{rowKeyId}", op.Path);
            Assert.Equal("eav", op.HandlerType);
            Assert.Equal("Widget", op.DomainName);
        }

        [Fact]
        public void Save_SameNameAndNode_ReusesId_WithoutCreating()
        {
            var (id1, created1) = _store.Save(Def("Order API"));
            var (id2, created2) = _store.Save(Def("order api")); // case-insensitive name match

            Assert.True(created1);
            Assert.False(created2);
            Assert.Equal(id1, id2);
            Assert.Single(_store.GetAll());
        }

        [Fact]
        public void Save_SameNameDifferentNode_UniquifiesId()
        {
            var (id1, _) = _store.Save(Def("Order API", "Org"));
            var (id2, created2) = _store.Save(Def("Order API", "Other"));

            Assert.True(created2);
            Assert.Equal("Order-API-2", id2);
        }

        [Fact]
        public void Save_WithExplicitNewId_UsesSanitizedId()
        {
            var def = Def("Whatever");
            def.Id = "My Custom ID!";

            var (id, created) = _store.Save(def);

            Assert.True(created);
            Assert.Equal("My-Custom-ID", id); // '!' -> '-', trimmed
        }

        [Fact]
        public void Save_UpdatePreservesCreatedAt()
        {
            var def = Def("Order API");
            var (id, _) = _store.Save(def);
            var first = _store.GetById(id)!;

            def.Description = "updated";
            var (_, created2) = _store.Save(def); // same Name+NodePath -> reuse id
            Assert.False(created2);

            var second = _store.GetById(id)!;
            Assert.Equal(first.CreatedAt, second.CreatedAt);
            Assert.True(second.UpdatedAt >= first.UpdatedAt);
            Assert.Equal("updated", second.Description);
        }

        [Fact]
        public void GetById_SeesInactiveRows_ButGetAllDoesNot()
        {
            var (id, _) = _store.Save(Def("Order API"));

            var def = Def("Order API");
            def.Id = id;
            def.IsActive = false;
            _store.Save(def);

            Assert.Empty(_store.GetAll());
            var loaded = _store.GetById(id)!;
            Assert.False(loaded.IsActive);
        }

        [Fact]
        public void Delete_RemovesRow_AndFalseWhenMissing()
        {
            var (id, _) = _store.Save(Def("Order API"));

            Assert.True(_store.Delete(id));
            Assert.Null(_store.GetById(id));
            Assert.False(_store.Delete(id));
        }

        [Fact]
        public void GetAll_FiltersByNodePathPrefix_SegmentAware()
        {
            _store.Save(Def("A1", "Acme"));
            _store.Save(Def("A2", "Acme/P"));
            _store.Save(Def("A3", "Acme/P/S"));
            _store.Save(Def("B1", "Other"));

            Assert.Equal(4, _store.GetAll().Count);
            Assert.Equal(3, _store.GetAll("Acme").Count);
            Assert.Equal(2, _store.GetAll("Acme/P").Count);
            // Segment-aware: 'Acm' must not match 'Acme/...' (no partial-segment prefix).
            Assert.Empty(_store.GetAll("Acm"));
        }
    }
}
