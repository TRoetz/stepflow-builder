using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.StepFunctions;
using Xunit;

namespace StepFunctionsApp.Tests
{
    /// <summary>
    /// eav:// handler: read (bare JArray, input ignored), write (object/array/scalar values + passthrough fallback),
    /// update/patch/delete by rowKeyId, and error contracts (missing entity type, unknown operation, missing row).
    /// </summary>
    public class EavResourceInvokerTests : IDisposable
    {
        private const string Domain = "smoke_domain";

        private readonly CompositeResourceInvoker _invoker;
        private readonly EavRowStore _store;
        private readonly string _dir;

        public EavResourceInvokerTests()
        {
            // Each xunit test instance gets its own isolated eav temp dir from the factory.
            (_invoker, _store, _dir) = TestResourceInvokers.Create();
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        [Fact]
        public async Task Read_EmptyDomain_ReturnsEmptyArray()
        {
            var result = await _invoker.InvokeAsync($"eav://{Domain}", new JObject(), CancellationToken.None);
            Assert.IsType<JArray>(result);
            Assert.Empty((JArray)result);
        }

        [Fact]
        public async Task Write_ObjectValues_AppendsOneRow()
        {
            var result = await _invoker.InvokeAsync($"eav://{Domain}", new JObject
            {
                ["operation"] = "write",
                ["values"] = new JObject { ["amount"] = 42, ["reason"] = "smoke" }
            }, CancellationToken.None);

            Assert.Equal(1, (int)((JObject)result)["count"]!);
            var rows = _store.ListRows(Domain);
            Assert.Single(rows);
            Assert.Equal(42, (int)rows[0].Values["amount"]!);
        }

        [Fact]
        public async Task Write_ArrayValues_AppendsOneRowPerElement()
        {
            var result = await _invoker.InvokeAsync($"eav://{Domain}", new JObject
            {
                ["operation"] = "write",
                ["values"] = new JArray(new JObject { ["n"] = 1 }, new JObject { ["n"] = 2 })
            }, CancellationToken.None);

            Assert.Equal(2, (int)((JObject)result)["count"]!);
            var rows = _store.ListRows(Domain);
            Assert.Equal(2, rows.Count);
            Assert.Equal(1, (int)rows[0].Values["n"]!);
        }

        [Fact]
        public async Task Write_ScalarValue_WrapsAsValue()
        {
            await _invoker.InvokeAsync($"eav://{Domain}", new JObject
            {
                ["operation"] = "write",
                ["values"] = "plain-text"
            }, CancellationToken.None);

            var rows = _store.ListRows(Domain);
            Assert.Single(rows);
            Assert.Equal("plain-text", (string?)rows[0].Values["value"]);
        }

        [Fact]
        public async Task Write_WithoutExplicitValues_PersistsInputMinusControlKeys()
        {
            // Mirrors the UI export: parameters = { operation, entityType, values.$ → $ }; after ApplyParameters
            // the input is { operation, entityType, values }. Without 'values' (e.g. a read-through payload),
            // everything except control keys becomes the row data.
            var result = await _invoker.InvokeAsync($"eav://{Domain}", new JObject
            {
                ["operation"] = "write",
                ["entityType"] = Domain,
                ["amount"] = 7,
                ["note"] = "passthrough"
            }, CancellationToken.None);

            Assert.Equal(1, (int)((JObject)result)["count"]!);
            var row = _store.ListRows(Domain).Single();
            Assert.Equal(7, (int)row.Values["amount"]!);
            Assert.Equal("passthrough", (string?)row.Values["note"]);
            Assert.Null(row.Values["operation"]);
            Assert.Null(row.Values["entityType"]);
        }

        [Fact]
        public async Task Write_EmptyValues_Fails()
        {
            var ex = await Assert.ThrowsAsync<StepEngineException>(() => _invoker.InvokeAsync($"eav://{Domain}", new JObject
            {
                ["operation"] = "write",
                ["values"] = new JArray()
            }, CancellationToken.None));
            Assert.Equal("States.TaskFailed", ex.ErrorCode);
            Assert.Contains("non-empty", ex.Message);
        }

        [Fact]
        public async Task Update_Patch_Delete_RoundTrip()
        {
            var writeResult = await _invoker.InvokeAsync($"eav://{Domain}", new JObject
            {
                ["operation"] = "write",
                ["values"] = new JObject { ["amount"] = 1, ["status"] = "open" }
            }, CancellationToken.None);
            Assert.Equal(1, (int)((JObject)writeResult)["count"]!);
            var rowKeyId = _store.ListRows(Domain).Single().RowKeyId;

            // patch merges keys
            var patched = await _invoker.InvokeAsync($"eav://{Domain}", new JObject
            {
                ["operation"] = "patch",
                ["rowKeyId"] = rowKeyId,
                ["values"] = new JObject { ["status"] = "closed" }
            }, CancellationToken.None);
            Assert.True((bool)((JObject)patched)["patched"]!);
            var afterPatch = _store.ListRows(Domain).Single();
            Assert.Equal(1, (int)afterPatch.Values["amount"]!); // preserved by merge
            Assert.Equal("closed", (string?)afterPatch.Values["status"]);

            // update replaces values wholesale
            var updated = await _invoker.InvokeAsync($"eav://{Domain}", new JObject
            {
                ["operation"] = "update",
                ["rowKeyId"] = rowKeyId,
                ["values"] = new JObject { ["amount"] = 99 }
            }, CancellationToken.None);
            Assert.True((bool)((JObject)updated)["updated"]!);
            var afterUpdate = _store.ListRows(Domain).Single();
            Assert.Equal(99, (int)afterUpdate.Values["amount"]!);
            Assert.Null(afterUpdate.Values["status"]); // replaced, not merged

            // delete removes the row
            var deleted = await _invoker.InvokeAsync($"eav://{Domain}", new JObject
            {
                ["operation"] = "delete",
                ["rowKeyId"] = rowKeyId
            }, CancellationToken.None);
            Assert.True((bool)((JObject)deleted)["removed"]!);
            Assert.Empty(_store.ListRows(Domain));
        }

        [Fact]
        public async Task Update_MissingRow_Fails()
        {
            var ex = await Assert.ThrowsAsync<StepEngineException>(() => _invoker.InvokeAsync($"eav://{Domain}", new JObject
            {
                ["operation"] = "update",
                ["rowKeyId"] = "does-not-exist",
                ["values"] = new JObject { ["x"] = 1 }
            }, CancellationToken.None));
            Assert.Equal("States.TaskFailed", ex.ErrorCode);
            Assert.Contains("row not found", ex.Message);
        }

        [Fact]
        public async Task Update_WithoutRowKeyId_Fails()
        {
            var ex = await Assert.ThrowsAsync<StepEngineException>(() => _invoker.InvokeAsync($"eav://{Domain}", new JObject
            {
                ["operation"] = "update",
                ["values"] = new JObject { ["x"] = 1 }
            }, CancellationToken.None));
            Assert.Equal("States.TaskFailed", ex.ErrorCode);
            Assert.Contains("'rowKeyId'", ex.Message);
        }

        [Fact]
        public async Task UnknownOperation_Fails()
        {
            var ex = await Assert.ThrowsAsync<StepEngineException>(() => _invoker.InvokeAsync($"eav://{Domain}", new JObject
            {
                ["operation"] = "explode"
            }, CancellationToken.None));
            Assert.Equal("States.TaskFailed", ex.ErrorCode);
            Assert.Contains("unknown operation", ex.Message);
        }

        [Fact]
        public async Task InvalidDomainName_SurfacesStoreValidation()
        {
            var ex = await Assert.ThrowsAsync<StepEngineException>(() => _invoker.InvokeAsync("eav://bad domain name!", new JObject(), CancellationToken.None));
            Assert.Equal("States.TaskFailed", ex.ErrorCode);
            Assert.Contains("Invalid attribute domain name", ex.Message);
        }

        [Fact]
        public async Task EntityTypeParameter_WinsOverUriPath()
        {
            await _invoker.InvokeAsync($"eav://{Domain}", new JObject
            {
                ["operation"] = "write",
                ["entityType"] = "other_domain",
                ["values"] = new JObject { ["x"] = 1 }
            }, CancellationToken.None);

            Assert.Empty(_store.ListRows(Domain));
            Assert.Single(_store.ListRows("other_domain"));
        }
    }
}
