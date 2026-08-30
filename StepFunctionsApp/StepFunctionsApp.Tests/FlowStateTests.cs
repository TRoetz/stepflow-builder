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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StepFunctionsApp.StepFunctions;

namespace StepFunctionsApp.Tests
{
    /// <summary>
    /// Durable flow state: disk-store round-trip, checkpoint boundaries, and kill/restart
    /// recovery (at-least-once resume semantics).
    /// </summary>
    public class FlowStateTests : IDisposable
    {
        private readonly string _dir;

        public FlowStateTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "stepflow-flowstate-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        // ── Test doubles & helpers ────────────────────────────────────────────────

        /// <summary>Records invocations; optional per-resource delays simulate a state still running when the process dies.</summary>
        private sealed class FakeInvoker : IResourceInvoker
        {
            public int Calls;
            public readonly List<string> Resources = new();
            public readonly List<JToken> Inputs = new();

            /// <summary>Delay (ms) before a resource returns; cancellation interrupts it like a real long-running call.</summary>
            public Dictionary<string, int> DelayMsByResource { get; } = new();

            public async Task<JToken> InvokeAsync(string resource, JToken input, CancellationToken ct)
            {
                Interlocked.Increment(ref Calls);
                lock (Resources)
                {
                    Resources.Add(resource);
                    Inputs.Add(input.DeepClone());
                }
                if (DelayMsByResource.TryGetValue(resource, out var ms)) await Task.Delay(ms, ct);
                return new JObject { ["resource"] = resource, ["echo"] = input };
            }
        }

        /// <summary>A(Pass) → B(Task) → C(Task, End): two task states, three checkpoint boundaries.</summary>
        private static StateMachineDefinition ThreeStateFlow() => new()
        {
            StartAt = "A",
            States = new Dictionary<string, StateDefinition>
            {
                ["A"] = new() { Type = StateType.Pass, Next = "B" },
                ["B"] = new() { Type = StateType.Task, Resource = "test://b", Next = "C" },
                ["C"] = new() { Type = StateType.Task, Resource = "test://c", End = true }
            }
        };

        private static StepFunctionService NewService(DiskFlowStateStore store, IResourceInvoker invoker, FlowStateOptions? options = null) =>
            new(
                new StepFunctionInterpreter(invoker, NullLogger<StepFunctionInterpreter>.Instance),
                new BpmnConverter(NullLogger<BpmnConverter>.Instance),
                NullLogger<StepFunctionService>.Instance,
                store,
                Options.Create(options ?? new FlowStateOptions()));

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

        // ── 1. Disk store round-trip ──────────────────────────────────────────────

        [Fact]
        public async Task DiskStore_Roundtrip_PreservesCheckpointAndListsByTime()
        {
            var store = new DiskFlowStateStore(_dir, NullLogger<DiskFlowStateStore>.Instance);
            var def = ThreeStateFlow();

            var older = new FlowStateRecord
            {
                ExecutionId = "aaa",
                Definition = def,
                CheckpointedAt = DateTime.UtcNow.AddHours(-1),
                Execution = new Execution
                {
                    StateMachineName = "roundtrip-old",
                    Status = ExecutionStatus.Running,
                    CurrentState = "B",
                    PendingInput = new JObject { ["fromA"] = true },
                    Input = new JObject { ["x"] = 1 }
                }
            };
            older.Execution.Variables["v"] = JToken.FromObject(42);
            older.Execution.History.Add(new HistoryEvent { Type = "StateEntered", State = "A" });

            var newer = new FlowStateRecord
            {
                ExecutionId = "bbb",
                Definition = def,
                CheckpointedAt = DateTime.UtcNow,
                Execution = new Execution { StateMachineName = "roundtrip-new", Status = ExecutionStatus.Succeeded }
            };

            await store.SaveAsync(older);
            await store.SaveAsync(newer);

            var loaded = (await store.LoadAsync("aaa"))!;
            Assert.Equal("B", loaded.Execution.CurrentState);
            Assert.True(loaded.Execution.PendingInput!["fromA"]!.Value<bool>());
            Assert.Equal(42, loaded.Execution.Variables["v"].Value<int>());
            Assert.Single(loaded.Execution.History);
            // The embedded definition survives, so a restarted process needs no prior registration.
            Assert.Equal("test://b", loaded.Definition.States["B"].Resource);

            var list = await store.ListAsync();
            Assert.Equal(new[] { "aaa", "bbb" }, list.Select(s => s.ExecutionId).ToArray());
            Assert.Equal("Running", list[0].Status);
            Assert.Equal("Succeeded", list[1].Status);

            await store.DeleteAsync("aaa");
            Assert.Null(await store.LoadAsync("aaa"));
        }

        // ── 2. Checkpoint boundaries ──────────────────────────────────────────────

        [Fact]
        public async Task Interpreter_Checkpoints_AtEveryBoundaryAndTerminal()
        {
            var invoker = new FakeInvoker();
            var interpreter = new StepFunctionInterpreter(invoker, NullLogger<StepFunctionInterpreter>.Instance);
            var checkpoints = new List<(string State, ExecutionStatus Status, JToken? Pending)>();

            var execution = await interpreter.ExecuteAsync(ThreeStateFlow(), new JObject { ["seed"] = 7 }, "exec-cp", "sm-test", onCheckpoint: e =>
            {
                checkpoints.Add((e.CurrentState!, e.Status, e.PendingInput?.DeepClone()));
                return Task.CompletedTask;
            });

            Assert.Equal(ExecutionStatus.Succeeded, execution.Status);
            // Boundary before A, boundary before B, then the terminal record.
            Assert.Equal(new[] { "A", "B" }, checkpoints.Take(2).Select(c => c.State).ToArray());
            Assert.Equal(ExecutionStatus.Succeeded, checkpoints.Last().Status);
            // The pre-StartAt boundary carries the original input; the pre-B boundary carries A's output —
            // exactly what B received. Resuming from either replays that state with this input.
            Assert.True(JToken.DeepEquals(checkpoints[0].Pending, new JObject { ["seed"] = 7 }));
            Assert.True(JToken.DeepEquals(checkpoints[1].Pending, invoker.Inputs[0]));
            Assert.Equal(2, invoker.Calls);
        }

        // ── 3. Resume does not re-execute completed states ───────────────────────

        [Fact]
        public async Task Resume_DoesNotReExecuteCompletedStates()
        {
            var def = ThreeStateFlow();
            var phase1Invoker = new FakeInvoker();
            var interpreter1 = new StepFunctionInterpreter(phase1Invoker, NullLogger<StepFunctionInterpreter>.Instance);

            FlowStateRecord? atC = null;
            await interpreter1.ExecuteAsync(def, new JObject { ["seed"] = 1 }, "exec-resume", "sm-test", onCheckpoint: e =>
            {
                if (e.CurrentState == "C" && e.Status == ExecutionStatus.Running) // boundary checkpoint, not the terminal one
                {
                    // Simulate what the disk store persists: a JSON round-trip of the live state.
                    atC = new FlowStateRecord
                    {
                        ExecutionId = e.ExecutionId,
                        Definition = def,
                        CheckpointedAt = DateTime.UtcNow,
                        Execution = JsonConvert.DeserializeObject<Execution>(JsonConvert.SerializeObject(e))!
                    };
                }
                return Task.CompletedTask;
            });

            Assert.NotNull(atC);

            var phase2Invoker = new FakeInvoker();
            var interpreter2 = new StepFunctionInterpreter(phase2Invoker, NullLogger<StepFunctionInterpreter>.Instance);
            var result = await interpreter2.ResumeAsync(def, atC!.Execution);

            Assert.Equal(ExecutionStatus.Succeeded, result.Status);
            // Only state C ran in phase 2; A and B (completed before the checkpoint) did not.
            Assert.Equal(new[] { "test://c" }, phase2Invoker.Resources.ToArray());
            // Phase-1 history survived the round-trip: each state entered exactly once overall.
            var entered = result.History.Where(h => h.Type == "StateEntered").Select(h => h.State).ToList();
            Assert.Equal(new[] { "A", "B", "C" }, entered);
        }

        // ── 4. Kill/restart recovery (auto-resume) ───────────────────────────────

        [Fact]
        public async Task Recovery_AutoResumesKilledExecution_FromCheckpoint()
        {
            var store = new DiskFlowStateStore(_dir, NullLogger<DiskFlowStateStore>.Instance);
            var def = ThreeStateFlow();

            // "Process 1": state B is still running (30s delay) when the process dies.
            var invoker1 = new FakeInvoker();
            invoker1.DelayMsByResource["test://b"] = 30_000;
            var service1 = NewService(store, invoker1); // AutoResume defaults to true
            using var cts1 = new CancellationTokenSource();

            service1.RegisterStateMachine("crash-flow", def, id: "sm-crash");
            var execution = service1.StartExecution("crash-flow", new JObject { ["seed"] = 9 });
            await service1.StartAsync(cts1.Token);

            // Wait until the durable checkpoint shows we are inside state B.
            await WaitUntilAsync(
                () => store.LoadAsync(execution.ExecutionId).GetAwaiter().GetResult(),
                r => r != null && r.Execution.CurrentState == "B" && r.Execution.Status == ExecutionStatus.Running);

            // Simulate a process death: stop the host while B is in flight. The interpreter must not
            // overwrite the boundary record with a terminal one — recovery resumes from it.
            cts1.Cancel();
            await service1.StopAsync(CancellationToken.None);
            var atKill = await store.LoadAsync(execution.ExecutionId);
            Assert.Equal(ExecutionStatus.Running, atKill!.Execution.Status);

            // "Process 2": fresh service over the same store; recovery auto-resumes from B.
            var invoker2 = new FakeInvoker();
            var service2 = NewService(store, invoker2);
            await service2.StartAsync(CancellationToken.None); // recovery runs here

            var finished = await WaitUntilAsync(
                () => service2.GetExecution(execution.ExecutionId),
                e => e!.Status == ExecutionStatus.Succeeded);

            // The new process re-ran B (at-least-once) and C — never A.
            Assert.Equal(new[] { "test://b", "test://c" }, invoker2.Resources.ToArray());
            var entered = finished.History.Where(h => h.Type == "StateEntered").Select(h => h.State).ToList();
            Assert.Equal(new[] { "A", "B", "C" }, entered);

            // The terminal checkpoint landed in the store with the final status.
            var record = await store.LoadAsync(execution.ExecutionId);
            Assert.NotNull(record);
            Assert.Equal(ExecutionStatus.Succeeded, record!.Execution.Status);

            await service2.StopAsync(CancellationToken.None);
        }

        // ── 5. AutoResume disabled: recovery suspends, manual resume completes ────

        [Fact]
        public async Task Recovery_LeavesExecutionSuspended_WhenAutoResumeDisabled()
        {
            var store = new DiskFlowStateStore(_dir, NullLogger<DiskFlowStateStore>.Instance);
            var def = ThreeStateFlow();

            var invoker1 = new FakeInvoker();
            invoker1.DelayMsByResource["test://b"] = 30_000; // B in flight when the process dies
            var service1 = NewService(store, invoker1);
            using var cts1 = new CancellationTokenSource();
            service1.RegisterStateMachine("susp-flow", def, id: "sm-susp");
            var execution = service1.StartExecution("susp-flow", new JObject());
            await service1.StartAsync(cts1.Token);

            await WaitUntilAsync(
                () => store.LoadAsync(execution.ExecutionId).GetAwaiter().GetResult(),
                r => r != null && r.Execution.CurrentState == "B");

            cts1.Cancel(); // process death while B is in flight; boundary record stays authoritative
            await service1.StopAsync(CancellationToken.None);

            var invoker2 = new FakeInvoker();
            var service2 = NewService(store, invoker2, new FlowStateOptions { AutoResume = false });
            await service2.StartAsync(CancellationToken.None); // recovery: marks Suspended, does not enqueue

            var suspended = await WaitUntilAsync(
                () => service2.GetExecution(execution.ExecutionId),
                e => e!.Status == ExecutionStatus.Suspended);
            Assert.Equal("Suspended", (await store.LoadAsync(execution.ExecutionId))!.Execution.Status.ToString());
            Assert.Equal(0, invoker2.Calls);

            // Manual resume picks it up and runs to completion from the checkpoint.
            await service2.ResumeStoredExecutionAsync(execution.ExecutionId);
            var finished = await WaitUntilAsync(
                () => service2.GetExecution(execution.ExecutionId),
                e => e!.Status == ExecutionStatus.Succeeded);
            Assert.Equal(new[] { "test://b", "test://c" }, invoker2.Resources.ToArray());

            await service2.StopAsync(CancellationToken.None);
        }

        // ── 6. Resume of an unknown execution is rejected ────────────────────────

        [Fact]
        public async Task Resume_UnknownExecution_Throws()
        {
            var store = new DiskFlowStateStore(_dir, NullLogger<DiskFlowStateStore>.Instance);
            var service = NewService(store, new FakeInvoker());
            await Assert.ThrowsAsync<ArgumentException>(() => service.ResumeStoredExecutionAsync("does-not-exist"));
        }
    }
}
