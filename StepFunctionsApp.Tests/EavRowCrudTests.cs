using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.StepFunctions;
using Xunit;

namespace StepFunctionsApp.Tests
{
    /// <summary>
    /// EavRowStore CRUD (the dynamic-API eav handler surface): key assignment on append,
    /// replace vs merge semantics, and false-when-missing contracts.
    /// </summary>
    public class EavRowCrudTests : IDisposable
    {
        private const string Domain = "TestDomain";

        private readonly string _dir;
        private readonly EavRowStore _store;

        public EavRowCrudTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "stepflow-eav-crud-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _store = new EavRowStore(NullLogger<EavRowStore>.Instance);
            _store.Initialize(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        private static EavRow Row(params (string Key, JToken Value)[] values)
        {
            var row = new EavRow();
            foreach (var (key, value) in values) row.Values[key] = value;
            return row;
        }

        [Fact]
        public void AppendRow_AssignsKey_WhenEmpty_AndListRowsPreservesOrder()
        {
            var a = Row(("first", "a"));
            var b = Row(("second", "b"));
            _store.AppendRow(Domain, a);
            _store.AppendRow(Domain, b);

            Assert.False(string.IsNullOrEmpty(a.RowKeyId));
            Assert.NotEqual(a.RowKeyId, b.RowKeyId);

            var rows = _store.ListRows(Domain);
            Assert.Equal(2, rows.Count);
            Assert.Equal("a", (string)rows[0].Values["first"]!);
            Assert.Equal("b", (string)rows[1].Values["second"]!);
        }

        [Fact]
        public void UpdateRow_ReplacesValues_FalseWhenMissing()
        {
            var row = Row(("old", 1));
            _store.AppendRow(Domain, row);

            Assert.True(_store.UpdateRow(Domain, row.RowKeyId!, new JObject { ["fresh"] = 2 }));
            var loaded = _store.ListRows(Domain).Single();
            Assert.Equal(2, (int)loaded.Values["fresh"]!);
            Assert.Null(loaded.Values["old"]); // replaced, not merged

            Assert.False(_store.UpdateRow(Domain, "missing-key", new JObject()));
            Assert.False(_store.UpdateRow("OtherDomain", row.RowKeyId!, new JObject()));
        }

        [Fact]
        public void PatchRow_MergesAndOverwrites_FalseWhenMissing()
        {
            var row = Row(("keep", 1), ("swap", "old"));
            _store.AppendRow(Domain, row);

            Assert.True(_store.PatchRow(Domain, row.RowKeyId!, new JObject { ["swap"] = "new", ["add"] = 3 }));
            var loaded = _store.ListRows(Domain).Single();
            Assert.Equal(1, (int)loaded.Values["keep"]!);
            Assert.Equal("new", (string)loaded.Values["swap"]!);
            Assert.Equal(3, (int)loaded.Values["add"]!);

            Assert.False(_store.PatchRow(Domain, "missing-key", new JObject()));
        }

        [Fact]
        public void RemoveRow_Removes_FalseWhenMissing()
        {
            var row = Row(("x", 1));
            _store.AppendRow(Domain, row);

            Assert.True(_store.RemoveRow(Domain, row.RowKeyId!));
            Assert.Empty(_store.ListRows(Domain));
            Assert.False(_store.RemoveRow(Domain, row.RowKeyId!));
        }
    }
}
