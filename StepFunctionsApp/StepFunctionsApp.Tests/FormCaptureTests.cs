using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StepFlow.DataModel.Entities;
using StepFlow.DataModel.Entities.MetaData;
using StepFunctionsApp.Controllers;
using StepFunctionsApp.StepFunctions;

namespace StepFunctionsApp.Tests
{
    /// <summary>
    /// FormCapture E2E: interpreter suspension on a bound form, controller validation/coercion,
    /// EAV row persistence, and resume of the suspended execution.
    /// </summary>
    public class FormCaptureTests : IDisposable
    {
        private readonly string _dir;
        private readonly List<IHostedService> _started = new();

        public FormCaptureTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "stepflow-formcapture-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            foreach (var service in _started)
            {
                try { service.StopAsync(CancellationToken.None).GetAwaiter().GetResult(); } catch { /* best effort */ }
            }
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        // ── test doubles & helpers ────────────────────────────────────────────────

        private sealed class FakeInvoker : IResourceInvoker
        {
            public int Calls;
            public readonly List<JToken> Inputs = new();

            public Task<JToken> InvokeAsync(string resource, JToken input, CancellationToken ct)
            {
                Interlocked.Increment(ref Calls);
                lock (Inputs) Inputs.Add(input.DeepClone());
                return Task.FromResult<JToken>(new JObject { ["resource"] = resource });
            }
        }

        private sealed class Harness : IDisposable
        {
            public readonly StepFunctionService Service;
            public readonly DiskHumanTaskStore Tasks;
            public readonly JsonFileFormDefinitionStore Forms;
            public readonly JsonFileAttributeDomainStore Domains;
            public readonly EavRowStore Rows;
            public readonly FormCaptureController Controller;
            private readonly string _dir;

            public Harness(string root, IResourceInvoker invoker)
            {
                _dir = Directory.CreateDirectory(Path.Combine(root, Guid.NewGuid().ToString("N"))).FullName;
                var stateStore = new DiskFlowStateStore(Directory.CreateDirectory(Path.Combine(_dir, "state")).FullName, NullLogger<DiskFlowStateStore>.Instance);
                Tasks = new DiskHumanTaskStore(Directory.CreateDirectory(Path.Combine(_dir, "tasks")).FullName, NullLogger<DiskHumanTaskStore>.Instance);
                Forms = new JsonFileFormDefinitionStore(Directory.CreateDirectory(Path.Combine(_dir, "forms")).FullName, NullLogger<JsonFileFormDefinitionStore>.Instance);
                Domains = new JsonFileAttributeDomainStore(Path.Combine(_dir, "attribute_domains.json"), NullLogger<JsonFileAttributeDomainStore>.Instance);
                Rows = new EavRowStore(NullLogger<EavRowStore>.Instance);
                Rows.Initialize(Directory.CreateDirectory(Path.Combine(_dir, "eav-data")).FullName);

                Service = NewService(stateStore, Tasks, invoker, Forms);
                Controller = new FormCaptureController(Tasks, Forms, Domains, Rows, Service);
            }

            public void Dispose()
            {
                try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
            }
        }

        private static StepFunctionService NewService(DiskFlowStateStore store, DiskHumanTaskStore htStore, IResourceInvoker invoker, IFormDefinitionStore formStore) =>
            new(
                new StepFunctionInterpreter(invoker, NullLogger<StepFunctionInterpreter>.Instance, htStore, formStore),
                new BpmnConverter(NullLogger<BpmnConverter>.Instance),
                NullLogger<StepFunctionService>.Instance,
                store,
                Options.Create(new FlowStateOptions()),
                htStore);

        private async Task StartAsync(IHostedService service)
        {
            await service.StartAsync(CancellationToken.None);
            _started.Add(service);
        }

        private static async Task<T> WaitUntilAsync<T>(Func<T?> probe, Func<T?, bool> condition, int timeoutMs = 10_000) where T : class
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                var value = probe();
                if (value != null && condition(value)) return value;
                await Task.Delay(25);
            }
            throw new TimeoutException($"Condition not met within {timeoutMs}ms");
        }

        private static StateMachineDefinition FormFlow(string formId) => new()
        {
            StartAt = "A",
            States = new Dictionary<string, StateDefinition>
            {
                ["A"] = new() { Type = StateType.Pass, Next = "B" },
                ["B"] = new()
                {
                    Type = StateType.FormCapture,
                    Next = "C",
                    Task = new JObject { ["formId"] = formId, ["title"] = "Approve order", ["assignee"] = "alice" }
                },
                ["C"] = new() { Type = StateType.Task, Resource = "test://c", End = true }
            }
        };

        private static JObject FormPage => JObject.Parse("""{"RootElements":[{"Type":"TextBox","Name":"note"}]}""");
        /// <summary>A FormCapture flow that pins an explicit form version (no task title, so the record title falls back to the resolved form's).</summary>
        private static StateMachineDefinition PinnedFlow(string formId, string? formVersion) => new()
        {
            StartAt = "A",
            States = new Dictionary<string, StateDefinition>
            {
                ["A"] = new() { Type = StateType.Pass, Next = "B" },
                ["B"] = new()
                {
                    Type = StateType.FormCapture,
                    Next = "C",
                    Task = formVersion is null
                        ? new JObject { ["formId"] = formId }
                        : new JObject { ["formId"] = formId, ["formVersion"] = formVersion },
                },
                ["C"] = new() { Type = StateType.Task, Resource = "test://c", End = true }
            }
        };

        /// <summary>Seeds the bound Order domain + order-approval form used by most tests.</summary>
        private static void SeedBoundOrder(Harness h)
        {
            h.Domains.Save(new AttributeDomain
            {
                AttributeDomainName = "Order",
                Description = "Captured orders",
                Attributes = new List<EntityAttribute>
                {
                    new() { AttributeName = "CustomerName", DataType = AttributeDataType.String, ValidationSchemaJson = "{\"required\":true}" },
                    new() { AttributeName = "Amount", DataType = AttributeDataType.Number, ValidationSchemaJson = "{\"minimum\":0}" },
                    new() { AttributeName = "Approved", DataType = AttributeDataType.Boolean }
                }
            });
            h.Forms.Save(new FormDefinition
            {
                FormId = "order-approval",
                Title = "Approve order",
                AttributeDomainName = "Order",
                Page = FormPage
            });
        }

        /// <summary>Starts the flow, waits for suspension at B, and returns execution + pending record.</summary>
        private async Task<(Execution Execution, HumanTaskRecord Record)> SuspendAtBAsync(Harness h, string flowName)
        {
            h.Service.RegisterStateMachine(flowName, FormFlow("order-approval"));
            var execution = h.Service.StartExecution(flowName, new JObject { ["orderId"] = 42 });
            await WaitUntilAsync(() => h.Service.GetExecution(execution.ExecutionId), e => e!.Status == ExecutionStatus.Suspended);

            var record = (await h.Tasks.ListAsync()).Single();
            Assert.Equal(execution.ExecutionId, record.ExecutionId);
            return (h.Service.GetExecution(execution.ExecutionId)!, record);
        }

        // ── 1. suspension ─────────────────────────────────────────────────────────

        [Fact]
        public async Task Suspend_PersistsFormTaskAndPointsAtResumeState()
        {
            using var h = new Harness(_dir, new FakeInvoker());
            await StartAsync(h.Service);
            SeedBoundOrder(h);

            var (execution, record) = await SuspendAtBAsync(h, "fc-suspend");

            Assert.Equal(ExecutionStatus.Suspended, execution.Status);
            Assert.Equal("C", execution.CurrentState);
            Assert.True(JToken.DeepEquals(execution.PendingInput, new JObject { ["orderId"] = 42 }));

            Assert.Equal(HumanTaskStatus.Pending, record.Status);
            Assert.Equal("form", record.CompletionType);
            Assert.Equal("B", record.StateName);
            Assert.Equal("C", record.NextState);
            Assert.False(record.IsEnd);
            Assert.Equal("Approve order", record.Title);
            Assert.Equal("alice", record.Assignee);
            Assert.Equal("order-approval", record.Payload!["task"]!["formId"]!.ToString());
        }

        // ── 2. valid submit on a bound form ───────────────────────────────────────

        [Fact]
        public async Task Submit_ValidBoundForm_CoercesPersistsEavRowAndResumes()
        {
            var invoker = new FakeInvoker();
            using var h = new Harness(_dir, invoker);
            await StartAsync(h.Service);
            SeedBoundOrder(h);

            var (execution, record) = await SuspendAtBAsync(h, "fc-submit");

            var body = new JObject { ["values"] = new JObject
            {
                ["CustomerName"] = "Acme Corp",
                ["Amount"] = "150.5", // string → coerced to number
                ["Approved"] = true,
                ["Extra"] = "dropped" // not in the contract → ignored
            } };

            Assert.IsType<OkObjectResult>(await h.Controller.Submit(record.TaskId, body));

            // EAV row persisted with coerced values before resume.
            var rows = h.Rows.ListRows("Order");
            Assert.Single(rows);
            Assert.Equal(record.TaskId, rows[0].SourceTaskId);
            Assert.Equal("Acme Corp", rows[0].Values["CustomerName"]!.ToString());
            Assert.Equal(150.5, rows[0].Values["Amount"]!.Value<double>());
            Assert.True(rows[0].Values["Approved"]!.Value<bool>());
            Assert.Null(rows[0].Values["Extra"]);

            // Execution resumed; downstream state received the coerced values as its input.
            await WaitUntilAsync(() => h.Service.GetExecution(execution.ExecutionId), e => e!.Status == ExecutionStatus.Succeeded);
            Assert.Equal(1, invoker.Calls);
            Assert.True(JToken.DeepEquals(invoker.Inputs[0], new JObject
            {
                ["CustomerName"] = "Acme Corp",
                ["Amount"] = 150.5,
                ["Approved"] = true
            }));

            // Record is terminal with the coerced result.
            var completed = (await h.Tasks.LoadAsync(record.TaskId))!;
            Assert.Equal(HumanTaskStatus.Completed, completed.Status);
            Assert.NotNull(completed.Result);
        }

        // ── 2b. ISO datetime strings (DateParseHandling.Auto → JTokenType.Date) ────

        [Fact]
        public async Task Submit_IsoDateTimeString_DateAttributeCoercesToCanonicalRoundTrip()
        {
            using var h = new Harness(_dir, new FakeInvoker());
            await StartAsync(h.Service);

            // Dedicated domain with a Date attribute (shared fixture stays untouched).
            h.Domains.Save(new AttributeDomain
            {
                AttributeDomainName = "Order",
                Attributes = new List<EntityAttribute>
                {
                    new() { AttributeName = "CustomerName", DataType = AttributeDataType.String, ValidationSchemaJson = "{\"required\":true}" },
                    new() { AttributeName = "OrderDate", DataType = AttributeDataType.Date }
                }
            });
            h.Forms.Save(new FormDefinition { FormId = "order-approval", Title = "Approve order", AttributeDomainName = "Order", Page = FormPage });

            var (execution, record) = await SuspendAtBAsync(h, "fc-date");

            // Deserialize from a JSON string exactly like ASP.NET model binding does:
            // Newtonsoft's DateParseHandling.Auto turns ISO-8601 strings into JTokenType.Date.
            var body = JsonConvert.DeserializeObject<JObject>(
                """{"values":{"CustomerName":"Acme","OrderDate":"2026-08-26T12:00:00"}}""")!;

            Assert.IsType<OkObjectResult>(await h.Controller.Submit(record.TaskId, body));

            var rows = h.Rows.ListRows("Order");
            Assert.Single(rows);
            var orderDate = rows[0].Values["OrderDate"]!;
            Assert.Equal(JTokenType.String, orderDate.Type);
            Assert.Equal("2026-08-26T12:00:00.0000000", (string)orderDate);

            await WaitUntilAsync(() => h.Service.GetExecution(execution.ExecutionId), e => e!.Status == ExecutionStatus.Succeeded);
        }

        // ── 3. invalid submit ─────────────────────────────────────────────────────

        [Fact]
        public async Task Submit_InvalidValues_ReturnsErrorsAndKeepsTaskPending()
        {
            using var h = new Harness(_dir, new FakeInvoker());
            await StartAsync(h.Service);
            SeedBoundOrder(h);

            var (execution, record) = await SuspendAtBAsync(h, "fc-invalid");

            // Missing required CustomerName; Amount not a number.
            var body = new JObject { ["values"] = new JObject { ["Amount"] = "abc" } };
            var result = await h.Controller.Submit(record.TaskId, body);
            var bad = Assert.IsType<BadRequestObjectResult>(result);

            var errors = JObject.FromObject(bad.Value!)["errors"]!.ToObject<Dictionary<string, string>>();
            Assert.Equal(2, errors.Count);
            Assert.Contains("CustomerName", errors.Keys);
            Assert.Contains("Amount", errors.Keys);

            // Nothing persisted; execution still suspended.
            Assert.Empty(h.Rows.ListRows("Order"));
            var pending = (await h.Tasks.LoadAsync(record.TaskId))!;
            Assert.Equal(HumanTaskStatus.Pending, pending.Status);
            Assert.Equal(ExecutionStatus.Suspended, h.Service.GetExecution(execution.ExecutionId)!.Status);
        }

        // ── 4. unbound form accepts any object ────────────────────────────────────

        [Fact]
        public async Task Submit_UnboundForm_AcceptsAnyObjectWithoutEavRow()
        {
            var invoker = new FakeInvoker();
            using var h = new Harness(_dir, invoker);
            await StartAsync(h.Service);
            h.Forms.Save(new FormDefinition { FormId = "freeform", Title = "Freeform", Page = FormPage });

            h.Service.RegisterStateMachine("fc-freeform", FormFlow("freeform"));
            var execution = h.Service.StartExecution("fc-freeform", new JObject());
            await WaitUntilAsync(() => h.Service.GetExecution(execution.ExecutionId), e => e!.Status == ExecutionStatus.Suspended);
            var record = (await h.Tasks.ListAsync()).Single();

            var body = new JObject { ["values"] = new JObject { ["foo"] = "bar", ["n"] = 1 } };
            Assert.IsType<OkObjectResult>(await h.Controller.Submit(record.TaskId, body));

            await WaitUntilAsync(() => h.Service.GetExecution(execution.ExecutionId), e => e!.Status == ExecutionStatus.Succeeded);
            Assert.Equal(1, invoker.Calls);
            Assert.True(JToken.DeepEquals(invoker.Inputs[0], new JObject { ["foo"] = "bar", ["n"] = 1 }));
        }

        // ── 5. double submit → conflict ───────────────────────────────────────────

        [Fact]
        public async Task Submit_Twice_SecondIsConflict()
        {
            using var h = new Harness(_dir, new FakeInvoker());
            await StartAsync(h.Service);
            SeedBoundOrder(h);

            var (_, record) = await SuspendAtBAsync(h, "fc-double");
            var body = new JObject { ["values"] = new JObject { ["CustomerName"] = "Acme", ["Amount"] = 10 } };

            Assert.IsType<OkObjectResult>(await h.Controller.Submit(record.TaskId, body));
            // The first submit already persisted the terminal record before returning.
            Assert.IsType<ConflictObjectResult>(await h.Controller.Submit(record.TaskId, body));
        }

        // ── 5b. missing values → bad request ───────────────────────────────────────

        [Fact]
        public async Task Submit_MissingValues_ReturnsBadRequest()
        {
            using var h = new Harness(_dir, new FakeInvoker());
            await StartAsync(h.Service);
            SeedBoundOrder(h);

            var (_, record) = await SuspendAtBAsync(h, "fc-novalue");

            var result = await h.Controller.Submit(record.TaskId, new JObject { ["other"] = 1 });
            var bad = Assert.IsType<BadRequestObjectResult>(result);
            var errors = JObject.FromObject(bad.Value!)["errors"]!.ToObject<Dictionary<string, string>>();
            Assert.Contains("values", errors.Keys);

            // Nothing persisted; task stays pending.
            Assert.Empty(h.Rows.ListRows("Order"));
            Assert.Equal(HumanTaskStatus.Pending, (await h.Tasks.LoadAsync(record.TaskId))!.Status);
        }

        // ── 6. GET endpoint ───────────────────────────────────────────────────────

        [Fact]
        public async Task Get_ReturnsFormAndContract_ForPendingTask()
        {
            using var h = new Harness(_dir, new FakeInvoker());
            await StartAsync(h.Service);
            SeedBoundOrder(h);

            var (_, record) = await SuspendAtBAsync(h, "fc-get");

            var result = await h.Controller.Get(record.TaskId);
            var ok = Assert.IsType<OkObjectResult>(result);
            var payload = JObject.FromObject(ok.Value!);

            Assert.Equal("Pending", payload["status"]!.ToString());
            Assert.Equal("order-approval", payload["form"]!["FormId"]!.ToString());
            var attrs = (JArray)payload["attributes"]!;
            Assert.Equal(3, attrs.Count);
            Assert.Contains(attrs, a => a["AttributeName"]!.ToString() == "CustomerName");

            // Unknown task id → 404.
            Assert.IsType<NotFoundObjectResult>(await h.Controller.Get("nope1234"));
        }

        // ── 7. version pinning ─────────────────────────────────────────────────────

        [Fact]
        public async Task Suspend_PinnedVersion_ResolvesExactSavedVersion()
        {
            using var h = new Harness(_dir, new FakeInvoker());
            await StartAsync(h.Service);
            SeedBoundOrder(h); // order-approval v1 (current), title "Approve order"

            // Republish: v2 becomes current with a different title.
            h.Forms.Save(new FormDefinition { FormId = "order-approval", Version = "2", Title = "Renamed in v2", AttributeDomainName = "Order", Page = FormPage });

            h.Service.RegisterStateMachine("fc-pin", PinnedFlow("order-approval", "1"));
            var execution = h.Service.StartExecution("fc-pin", new JObject { ["orderId"] = 42 });
            await WaitUntilAsync(() => h.Service.GetExecution(execution.ExecutionId), e => e!.Status == ExecutionStatus.Suspended);

            var record = (await h.Tasks.ListAsync()).Single();
            // The pinned version is recorded, and its title wins over current v2's.
            Assert.Equal("1", record.FormVersion);
            Assert.Equal("Approve order", record.Title);
        }

        [Fact]
        public async Task Get_PendingTask_KeepsPinnedVersionAfterRepublish()
        {
            using var h = new Harness(_dir, new FakeInvoker());
            await StartAsync(h.Service);
            SeedBoundOrder(h);

            h.Service.RegisterStateMachine("fc-pin-republish", PinnedFlow("order-approval", "1"));
            var execution = h.Service.StartExecution("fc-pin-republish", new JObject { ["orderId"] = 42 });
            await WaitUntilAsync(() => h.Service.GetExecution(execution.ExecutionId), e => e!.Status == ExecutionStatus.Suspended);
            var record = (await h.Tasks.ListAsync()).Single();

            // Republish v2 as current while the task is still pending.
            h.Forms.Save(new FormDefinition { FormId = "order-approval", Version = "2", Title = "Renamed in v2", AttributeDomainName = "Order", Page = FormPage });

            var result = await h.Controller.Get(record.TaskId);
            var ok = Assert.IsType<OkObjectResult>(result);
            var payload = JObject.FromObject(ok.Value!);
            // The fill-in page still renders the pinned v1, not the new current version.
            Assert.Equal("1", payload["form"]!["Version"]!.ToString());
            Assert.Equal("Approve order", payload["title"]!.ToString());
        }

        [Fact]
        public async Task Suspend_UnknownPinnedVersion_FailsExecutionWithFormNotFound()
        {
            using var h = new Harness(_dir, new FakeInvoker());
            await StartAsync(h.Service);
            SeedBoundOrder(h);

            h.Service.RegisterStateMachine("fc-pin-missing", PinnedFlow("order-approval", "9"));
            var execution = h.Service.StartExecution("fc-pin-missing", new JObject { ["orderId"] = 42 });
            var failed = await WaitUntilAsync(() => h.Service.GetExecution(execution.ExecutionId), e => e!.Status == ExecutionStatus.Failed);

            Assert.Equal("FormCapture.FormNotFound", failed.ErrorCode);
            Assert.Empty(await h.Tasks.ListAsync()); // no human task was created
        }
    }
}
