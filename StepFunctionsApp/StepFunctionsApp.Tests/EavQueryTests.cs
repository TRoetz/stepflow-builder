using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.StepFunctions;

namespace StepFunctionsApp.Tests
{
    /// <summary>The shared eav GET query language: parse, filter/sort/page/shape, single-row lookup.</summary>
    public sealed class EavQueryTests
    {
        private static IQueryCollection Q(string raw) => new QueryCollection(QueryHelpers.ParseQuery(raw));

        private static EavRow Row(string rowKeyId, string? entityId, string entityType, params (string Key, object Value)[] values)
        {
            var row = new EavRow { RowKeyId = rowKeyId, EntityId = entityId, EntityType = entityType };
            foreach (var (key, value) in values) row.Values[key] = new JValue(value);
            return row;
        }

        // ── Parse ────────────────────────────────────────────────────────────────────

        [Fact]
        public void Parse_FiltersReservedSortPageLimitOffsetFields()
        {
            var options = EavQuery.Parse(Q("?name=alpha&sort=capturedAtUtc,-entityId&page=3&limit=10&offset=&fields=name,qty"));

            // Non-reserved keys become filters; reserved keys never do.
            Assert.Equal(new[] { "alpha" }, options.Filters["name"]);
            Assert.Single(options.Filters);
            Assert.Equal(("capturedAtUtc", false), options.SortKeys[0]);
            Assert.Equal(("entityId", true), options.SortKeys[1]);
            Assert.Equal(10, options.Limit);
            Assert.Equal(20, options.Offset); // (page - 1) * limit
            Assert.Equal("name,qty", options.Fields);
        }

        [Fact]
        public void Parse_MultiValueKey_IsOrList()
        {
            var options = EavQuery.Parse(Q("?status=open&status=closed"));

            Assert.Equal(new[] { "open", "closed" }, options.Filters["status"]);
        }

        [Fact]
        public void Parse_LenientReservedValues_FallBackToDefaults()
        {
            var options = EavQuery.Parse(Q("?limit=abc&offset=-4"));

            Assert.Equal(EavQuery.DefaultLimit, options.Limit);
            Assert.Equal(0, options.Offset);
        }

        [Fact]
        public void Parse_EmptyValues_CarryNoConstraint()
        {
            var options = EavQuery.Parse(Q("?entityId="));

            Assert.Empty(options.Filters); // legacy ?entityId= behavior preserved
        }

        [Fact]
        public void Parse_PageAndOffsetTogether_Throws()
        {
            var ex = Assert.Throws<ArgumentException>(() => EavQuery.Parse(Q("?page=1&offset=5")));

            Assert.Contains("page", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ── Apply ────────────────────────────────────────────────────────────────────

        private static (EavRow A, EavRow B, EavRow C) ThreeRows() =>
            (
                Row("k1", "e1", "Gadget", ("name", "alpha"), ("qty", 3)),
                Row("k2", "e2", "Gizmo", ("name", "beta"), ("qty", 1)),
                Row("k3", "e1", "Gadget", ("name", "gamma"), ("qty", 2))
            );
        [Fact]
        public void Apply_FiltersByValuesKeyAndTopLevelField_And()
        {
            var rows = ThreeRows(); // k1 alpha/Gadget, k2 beta/Gizmo, k3 gamma/Gadget
            var options = EavQuery.Parse(Q("?name=alpha&entityType=Gadget"));

            var (page, total) = EavQuery.Apply(new[] { rows.A, rows.B, rows.C }, options);

            Assert.Equal(1, total); // AND: k3 shares entityType but not name; k2 shares neither
            Assert.Equal("k1", (string)page[0]["rowKeyId"]!);
        }

        [Fact]
        public void Apply_RepeatedKey_OrsAcrossValues()
        {
            var rows = ThreeRows();
            var options = EavQuery.Parse(Q("?name=alpha&name=gamma"));

            var (page, total) = EavQuery.Apply(new[] { rows.A, rows.B, rows.C }, options);

            Assert.Equal(2, total);
            Assert.Equal(new[] { "k1", "k3" }, page.Select(r => (string)r["rowKeyId"]!));
        }

        [Fact]
        public void Apply_SortsDescByValuesKey()
        {
            var rows = ThreeRows();
            var options = EavQuery.Parse(Q("?sort=-qty"));

            var (page, _) = EavQuery.Apply(new[] { rows.A, rows.B, rows.C }, options);

            Assert.Equal(new[] { "k1", "k3", "k2" }, page.Select(r => (string)r["rowKeyId"]!)); // qty 3, 2, 1
        }

        [Fact]
        public void Apply_LimitOffset_SlicesPage_TotalIsPostFilterCount()
        {
            var rows = ThreeRows();
            var options = EavQuery.Parse(Q("?sort=qty&limit=2&offset=1"));

            var (page, total) = EavQuery.Apply(new[] { rows.A, rows.B, rows.C }, options);

            Assert.Equal(3, total);
            Assert.Equal(new[] { "k3", "k1" }, page.Select(r => (string)r["rowKeyId"]!)); // qty asc k2(1),k3(2),k1(3) -> skip 1 take 2
        }

        // ── Shape ────────────────────────────────────────────────────────────────────

        [Fact]
        public void Shape_FieldsProjectsToRequestedNamesPlusRowKeyId()
        {
            var row = Row("k9", "e1", "Gadget", ("name", "alpha"), ("note", "x"));

            var shaped = EavQuery.Shape(row, "name");

            Assert.Equal(new[] { "name", "rowKeyId" }, shaped.Properties().Select(p => p.Name).OrderBy(n => n));
            Assert.Equal("alpha", (string)shaped["name"]!);
            Assert.Null(shaped["values"]);
            Assert.Null(shaped["entityType"]);
        }

        [Fact]
        public void Shape_UnknownFieldNames_Dropped_RowKeyIdAlwaysPresent()
        {
            var row = Row("k9", "e1", "Gadget", ("name", "alpha"));

            var shaped = EavQuery.Shape(row, "bogus,name");

            Assert.Equal(new[] { "name", "rowKeyId" }, shaped.Properties().Select(p => p.Name).OrderBy(n => n));
        }

        [Fact]
        public void Shape_NoFields_FullWireShape()
        {
            var row = Row("k9", "e1", "Gadget", ("name", "alpha"));

            var shaped = EavQuery.Shape(row, null);

            Assert.Equal("k9", (string)shaped["rowKeyId"]!);
            Assert.Equal("e1", (string)shaped["entityId"]!);
            Assert.Equal("Gadget", (string)shaped["entityType"]!);
            Assert.Equal("alpha", (string)shaped["values"]!["name"]!);
        }

        // ── Lookup ───────────────────────────────────────────────────────────────────

        [Fact]
        public void Lookup_EntityIdFirst_RowKeyIdFallback_EmptyWhenNothing()
        {
            var dupA = Row("d1", "dup", "Gadget");
            var dupB = Row("d2", "dup", "Gadget");
            var solo = Row("s1", "solo", "Widget");
            var byKeyOnly = Row("abc123", null, "Widget"); // no entity id - addressable only via rowKeyId
            var rows = new[] { dupA, dupB, solo, byKeyOnly };

            Assert.Equal(2, EavQuery.Lookup(rows, "dup").Count);
            Assert.Same(solo, EavQuery.Lookup(rows, "solo").Single());
            Assert.Same(byKeyOnly, EavQuery.Lookup(rows, "abc123").Single()); // no entity match -> RowKeyId fallback
            Assert.Empty(EavQuery.Lookup(rows, "no-such-row"));
        }
    }
}
