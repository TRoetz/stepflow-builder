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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StepFunctionsApp.StepFunctions;

namespace StepFunctionsApp.Tests
{
    /// <summary>
    /// Human task lifecycle: interpreter suspension, disk-persisted pending records,
    /// completion via the API path and via the file-monitor provider, and resume semantics.
    /// </summary>
    public class HumanTaskTests : IDisposable
    {
        private readonly string _dir;
        private readonly List<IHostedService> _started = new();

        public HumanTaskTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "stepflow-humantask-tests", Guid.NewGuid().ToString("N"));
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

        // ── Test doubles & helpers ────────────────────────────────────────────────

        private sealed class FakeInvoker : IResourceInvoker
        {
            public int Calls;
            public readonly List<JToken> Inputs = new();

            public Task<JToken> InvokeAsync(string resource, JToken input, CancellationToken ct)
            {
                Interlocked.Increment(ref Calls);
                lock (Inputs) Inputs.Add(input.DeepClone());
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "fake-invoker.log"), $"[{DateTime.Now:HH:mm:ss.fff}] {resource} <- {input.ToString(Newtonsoft.Json.Formatting.None)}\n");
                return Task.FromResult<JToken>(new JObject { ["resource"] = resource });
            }
        }

        private string SubDir(string name) => Directory.CreateDirectory(Path.Combine(_dir, name)).FullName;

        /// <summary>A(Pass) → B(HumanTask) → C(Task, End). Completion type and result path are parameterized.</summary>
        private static StateMachineDefinition HumanTaskFlow(string completionType, string? resultPath = null) => new()
        {
            StartAt = "A",
            States = new Dictionary<string, StateDefinition>
            {
                ["A"] = new() { Type = StateType.Pass, Next = "B" },
                ["B"] = new()
                {
                    Type = StateType.HumanTask,
                    Next = "C",
                    ResultPath = resultPath,
                    Task = new JObject { ["title"] = "Approve order", ["assignee"] = "alice" },
                    Completion = new JObject { ["Type"] = completionType }
                },
                ["C"] = new() { Type = StateType.Task, Resource = "test://c", End = true }
            }
        };

        private static StepFunctionService NewService(DiskFlowStateStore store, DiskHumanTaskStore htStore, IResourceInvoker invoker) =>
            new(
                new StepFunctionInterpreter(invoker, NullLogger<StepFunctionInterpreter>.Instance, htStore),
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

        /// <summary>Starts the flow, waits for suspension at B, and returns the pending record.</summary>
        private async Task<(Execution Execution, HumanTaskRecord Record)> SuspendAtBAsync(StepFunctionService service, DiskHumanTaskStore htStore, string flowName)
        {
            var execution = service.StartExecution(flowName, new JObject { ["orderId"] = 42 });
            await WaitUntilAsync(() => service.GetExecution(execution.ExecutionId), e => e!.Status == ExecutionStatus.Suspended);

            var record = (await htStore.ListAsync()).Single();
            Assert.Equal(execution.ExecutionId, record.ExecutionId);
            return (service.GetExecution(execution.ExecutionId)!, record);
        }

        // ── 1. Suspension ─────────────────────────────────────────────────────────

        [Fact]
        public async Task Suspend_PersistsPendingTaskAndPointsAtResumeState()
        {
            var store = new DiskFlowStateStore(SubDir("state"), NullLogger<DiskFlowStateStore>.Instance);
            var htStore = new DiskHumanTaskStore(SubDir("tasks"), NullLogger<DiskHumanTaskStore>.Instance);
            var invoker = new FakeInvoker();
            var service = NewService(store, htStore, invoker);
            await StartAsync(service);

            service.RegisterStateMachine("ht-suspend", HumanTaskFlow("api"));
            var (execution, record) = await SuspendAtBAsync(service, htStore, "ht-suspend");

            // The execution is parked at the resume point with B's input held for the merge.
            Assert.Equal(ExecutionStatus.Suspended, execution.Status);
            Assert.Equal("C", execution.CurrentState);
            Assert.True(JToken.DeepEquals(execution.PendingInput, new JObject { ["orderId"] = 42 }));

            // The pending record carries everything a human (or provider) needs.
            Assert.Equal(HumanTaskStatus.Pending, record.Status);
            Assert.Equal("api", record.CompletionType);
            Assert.Equal("B", record.StateName);
            Assert.Equal("C", record.NextState);
            Assert.False(record.IsEnd);
            Assert.Null(record.ResultPath);
            Assert.Equal("Approve order", record.Title);
            Assert.Equal("alice", record.Assignee);
            Assert.Equal("Approve order", record.Payload!["task"]!["title"]!.ToString());
            Assert.True(JToken.DeepEquals(record.Payload["input"], new JObject { ["orderId"] = 42 }));

            // Nothing downstream ran.
            Assert.Equal(0, invoker.Calls);
        }

        // ── 2. Completion via API path resumes the flow ───────────────────────────

        [Fact]
        public async Task Complete_ResumeRunsNextStateWithResultAsInput()
        {
            var store = new DiskFlowStateStore(SubDir("state"), NullLogger<DiskFlowStateStore>.Instance);
            var htStore = new DiskHumanTaskStore(SubDir("tasks"), NullLogger<DiskHumanTaskStore>.Instance);
            var invoker = new FakeInvoker();
            var service = NewService(store, htStore, invoker);
            await StartAsync(service);

            service.RegisterStateMachine("ht-complete", HumanTaskFlow("api"));
            var (execution, record) = await SuspendAtBAsync(service, htStore, "ht-complete");

            var result = new JObject { ["approved"] = true, ["by"] = "alice" };
            var completed = await service.CompleteHumanTaskAsync(record.TaskId, result);

            // No ResultPath: the human's result replaces the pending input entirely.
            var finished = await WaitUntilAsync(() => service.GetExecution(execution.ExecutionId), e => e!.Status == ExecutionStatus.Succeeded);
            Assert.Equal(1, invoker.Calls);
            Assert.True(JToken.DeepEquals(invoker.Inputs[0], result));

            // The record is terminal and the durable checkpoint agrees.
            Assert.NotNull(completed);
            Assert.Equal(HumanTaskStatus.Completed, completed!.Status);
            Assert.NotNull(completed.CompletedAtUtc);
            Assert.True(JToken.DeepEquals(completed.Result, result));
            var stored = (await store.LoadAsync(execution.ExecutionId))!;
            Assert.Equal(ExecutionStatus.Succeeded, stored.Execution.Status);

            // Completing twice is rejected.
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteHumanTaskAsync(record.TaskId, result));
        }

        [Fact]
        public async Task Complete_WithResultPath_MergesIntoPendingInput()
        {
            var store = new DiskFlowStateStore(SubDir("state"), NullLogger<DiskFlowStateStore>.Instance);
            var htStore = new DiskHumanTaskStore(SubDir("tasks"), NullLogger<DiskHumanTaskStore>.Instance);
            var invoker = new FakeInvoker();
            var service = NewService(store, htStore, invoker);
            await StartAsync(service);

            service.RegisterStateMachine("ht-merge", HumanTaskFlow("api", resultPath: "$.approval"));
            var (execution, record) = await SuspendAtBAsync(service, htStore, "ht-merge");

            var result = new JObject { ["approved"] = true };
            await service.CompleteHumanTaskAsync(record.TaskId, result);

            await WaitUntilAsync(() => service.GetExecution(execution.ExecutionId), e => e!.Status == ExecutionStatus.Succeeded);
            Assert.True(JToken.DeepEquals(invoker.Inputs[0], new JObject
            {
                ["orderId"] = 42,
                ["approval"] = result
            }));
        }

        // ── 3. Terminal human task ────────────────────────────────────────────────

        [Fact]
        public async Task TerminalHumanTask_CompletionBecomesExecutionOutput()
        {
            var store = new DiskFlowStateStore(SubDir("state"), NullLogger<DiskFlowStateStore>.Instance);
            var htStore = new DiskHumanTaskStore(SubDir("tasks"), NullLogger<DiskHumanTaskStore>.Instance);
            var invoker = new FakeInvoker();
            var service = NewService(store, htStore, invoker);
            await StartAsync(service);

            var def = HumanTaskFlow("api");
            def.States["B"]!.Next = null;
            def.States["B"]!.End = true;
            service.RegisterStateMachine("ht-end", def);

            var execution = service.StartExecution("ht-end", new JObject { ["orderId"] = 7 });
            await WaitUntilAsync(() => service.GetExecution(execution.ExecutionId), e => e!.Status == ExecutionStatus.Suspended);
            var record = (await htStore.ListAsync()).Single();
            Assert.True(record.IsEnd);

            var result = new JObject { ["verdict"] = "ship it" };
            await service.CompleteHumanTaskAsync(record.TaskId, result);

            var finished = await WaitUntilAsync(() => service.GetExecution(execution.ExecutionId), e => e!.Status == ExecutionStatus.Succeeded);
            Assert.True(JToken.DeepEquals(finished.Output, result));
            Assert.Equal(0, invoker.Calls); // no next state to run
        }

        // ── 4. File provider unit behavior ────────────────────────────────────────

        [Fact]
        public async Task FileProvider_ReturnsNullUntilFileExists_ThenParsesAndConsumes()
        {
            var dir = SubDir("completions");
            var options = new HumanTaskOptions { DiskPath = SubDir("tasks"), FileMonitorDefaultDirectory = dir };
            var provider = new FileMonitorCompletionProvider(Options.Create(options));

            var record = new HumanTaskRecord { TaskId = "abc123", CompletionType = "file" };
            Assert.Null(await provider.CheckForCompletionAsync(record, CancellationToken.None));

            var path = Path.Combine(dir, "abc123.json");
            await File.WriteAllTextAsync(path, """{"approved":true,"note":"ok"}""");
            var result = await provider.CheckForCompletionAsync(record, CancellationToken.None);
            Assert.True(JToken.DeepEquals(result, new JObject { ["approved"] = true, ["note"] = "ok" }));

            // Cleanup consumes the artifact; a missing file is tolerated.
            await provider.OnCompletedAsync(record, result!, CancellationToken.None);
            Assert.False(File.Exists(path));
            await provider.OnCompletedAsync(record, result!, CancellationToken.None);
        }

        [Fact]
        public async Task FileProvider_InvalidJson_ReturnsNullAndKeepsFileForRetry()
        {
            var dir = SubDir("completions");
            var options = new HumanTaskOptions { DiskPath = SubDir("tasks"), FileMonitorDefaultDirectory = dir };
            var provider = new FileMonitorCompletionProvider(Options.Create(options));

            var record = new HumanTaskRecord { TaskId = "bad1", CompletionType = "file" };
            var path = Path.Combine(dir, "bad1.json");
            await File.WriteAllTextAsync(path, "{partial write");

            Assert.Null(await provider.CheckForCompletionAsync(record, CancellationToken.None));
            Assert.True(File.Exists(path), "invalid JSON must stay in place for the next poll");
        }

        // ── 5. Monitor end-to-end: file drop completes a suspended flow ───────────

        [Fact]
        public async Task Monitor_FileDropCompletesSuspendedFlowAndConsumesFile()
        {
            var store = new DiskFlowStateStore(SubDir("state"), NullLogger<DiskFlowStateStore>.Instance);
            var htDir = SubDir("tasks");
            var completionsDir = SubDir("completions");
            var htStore = new DiskHumanTaskStore(htDir, NullLogger<DiskHumanTaskStore>.Instance);
            var invoker = new FakeInvoker();
            var service = NewService(store, htStore, invoker);
            await StartAsync(service);

            var options = new HumanTaskOptions { DiskPath = htDir, FileMonitorDefaultDirectory = completionsDir, PollIntervalSeconds = 1 };
            var monitor = new HumanTaskCompletionMonitorService(
                htStore, service,
                new IHumanTaskCompletionProvider[] { new ApiCompletionProvider(), new FileMonitorCompletionProvider(Options.Create(options)) },
                Options.Create(options),
                NullLogger<HumanTaskCompletionMonitorService>.Instance);
            await StartAsync(monitor);

            service.RegisterStateMachine("ht-file", HumanTaskFlow("file"));
            var (execution, record) = await SuspendAtBAsync(service, htStore, "ht-file");

            // The human drops the completion file; the monitor picks it up on its next poll.
            var result = new JObject { ["approved"] = true, ["note"] = "from disk" };
            var path = Path.Combine(completionsDir, $"{record.TaskId}.json");
            await File.WriteAllTextAsync(path, JsonConvert.SerializeObject(result));

            await WaitUntilAsync(() => service.GetExecution(execution.ExecutionId), e => e!.Status == ExecutionStatus.Succeeded);
            Assert.Equal(1, invoker.Calls);
            Assert.True(JToken.DeepEquals(invoker.Inputs[0], result));

            // The completion artifact is consumed once the task completes.
            Assert.False(File.Exists(path), "completion file should be deleted after successful completion");
        }
    }
}
