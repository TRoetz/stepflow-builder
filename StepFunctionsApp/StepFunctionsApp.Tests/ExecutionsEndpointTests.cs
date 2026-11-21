using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StepFunctionsApp.DataExchange;
using StepFunctionsApp.StepFunctions;
using StepFunctionsApp.Workspace;
using Xunit;

namespace StepFunctionsApp.Tests
{
    /// <summary>
    /// Execution history endpoint tests — the stored-execution surface behind the Executions panel:
    /// list/get/resume/stop/delete against /api/flows/executions. Covers a completed run's full record
    /// (input/output/history), unknown-id 404s, and the human-task lifecycle end to end over HTTP:
    /// async start suspends at the HumanTask state, the task appears in /api/human-tasks?status=Pending,
    /// completing it via POST /api/human-tasks/{id}/complete resumes the run to Succeeded.
    /// </summary>
    public sealed class ExecutionsEndpointTests : IClassFixture<ExecutionsEndpointTests.Factory>
    {
        private readonly HttpClient _client;

        public ExecutionsEndpointTests(Factory factory)
        {
            _client = factory.CreateClient();
        }

        private static async Task<(HttpStatusCode Status, JToken Body)> SendAsync(HttpClient client, HttpMethod method, string url, object? payload)
        {
            var content = payload == null ? null : new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var res = await client.SendAsync(new HttpRequestMessage(method, url) { Content = content });
            var text = await res.Content.ReadAsStringAsync();
            return (res.StatusCode, string.IsNullOrWhiteSpace(text) ? JValue.CreateNull() : JToken.Parse(text));
        }

        /// <summary>Registers a minimal Start→Done flow and returns its id.</summary>
        private async Task<string> RegisterFlowAsync(string name)
        {
            var payload = new JObject
            {
                ["name"] = name,
                ["startAt"] = "Start",
                ["states"] = new JObject
                {
                    ["Start"] = new JObject { ["Type"] = "Pass", ["Next"] = "Done" },
                    ["Done"] = new JObject { ["Type"] = "Pass", ["end"] = true }
                }
            };
            var (status, body) = await SendAsync(_client, HttpMethod.Post, "/api/flows", payload);
            Assert.Equal(HttpStatusCode.OK, status);
            return (string)body!["id"]!;
        }

        /// <summary>Polls the executions list until the given execution reports the target status.</summary>
        private async Task<string?> WaitForStatusAsync(string executionId, string target, int timeoutMs = 15000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(250);
                var (status, body) = await SendAsync(_client, HttpMethod.Get, "/api/flows/executions", null);
                if (status != HttpStatusCode.OK) continue;
                var summary = ((JArray)body!).FirstOrDefault(e => string.Equals((string)e!["executionId"], executionId, StringComparison.Ordinal));
                var current = (string?)summary?["status"];
                if (string.Equals(current, target, StringComparison.OrdinalIgnoreCase)) return current;
            }
            return null;
        }

        [Fact]
        public async Task CompletedRun_ListGetDelete_RoundTrip()
        {
            var flowId = await RegisterFlowAsync("exec-e2e-" + Guid.NewGuid().ToString("N"));

            // Synchronous run to completion.
            var (es, eb) = await SendAsync(_client, HttpMethod.Post, $"/api/flows/execute-sync/{flowId}", new { a = 1 });
            Assert.Equal(HttpStatusCode.OK, es);
            var executionId = (string)eb!["executionId"]!;
            Assert.Equal("Succeeded", (string)eb["status"]!);

            // List shows the summary.
            var (ls, lb) = await SendAsync(_client, HttpMethod.Get, "/api/flows/executions", null);
            Assert.Equal(HttpStatusCode.OK, ls);
            var summary = ((JArray)lb!).FirstOrDefault(e => string.Equals((string)e!["executionId"], executionId, StringComparison.Ordinal));
            Assert.NotNull(summary);
            Assert.Equal("Succeeded", (string)summary!["status"]!);

            // Get returns the full record: input echoed, output present, history covers both states.
            var (gs, gb) = await SendAsync(_client, HttpMethod.Get, $"/api/flows/executions/{executionId}", null);
            Assert.Equal(HttpStatusCode.OK, gs);
            Assert.Equal(1, (int)gb!["input"]["a"]!);
            Assert.NotNull(gb["output"]);
            var history = (JArray)gb["history"]!;
            Assert.True(history.Count >= 2, $"expected at least 2 history events, got {history.Count}");

            // Delete removes the checkpoint: get then 404s and list no longer shows it.
            var (ds, db) = await SendAsync(_client, HttpMethod.Delete, $"/api/flows/executions/{executionId}", null);
            Assert.Equal(HttpStatusCode.OK, ds);
            Assert.Contains(executionId, (string)db!["message"]!, StringComparison.OrdinalIgnoreCase);

            var (g2, _) = await SendAsync(_client, HttpMethod.Get, $"/api/flows/executions/{executionId}", null);
            Assert.Equal(HttpStatusCode.NotFound, g2);
            var (l2, lb2) = await SendAsync(_client, HttpMethod.Get, "/api/flows/executions", null);
            Assert.Null(((JArray)lb2!).FirstOrDefault(e => string.Equals((string)e!["executionId"], executionId, StringComparison.Ordinal)));
        }

        [Fact]
        public async Task Get_Unknown_Returns404_NamingTheExecution()
        {
            var missing = "no-such-execution-" + Guid.NewGuid().ToString("N");
            var (status, body) = await SendAsync(_client, HttpMethod.Get, $"/api/flows/executions/{missing}", null);
            Assert.Equal(HttpStatusCode.NotFound, status);
            Assert.Contains(missing, (string)body!["error"]!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Resume_Unknown_Returns404_NamingTheExecution()
        {
            var missing = "no-such-execution-" + Guid.NewGuid().ToString("N");
            var (status, body) = await SendAsync(_client, HttpMethod.Post, $"/api/flows/executions/{missing}/resume", null);
            Assert.Equal(HttpStatusCode.NotFound, status);
            Assert.Contains(missing, (string)body!["error"]!, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Stop_UnknownExecution_ReturnsZeroStopped()
        {
            var (status, body) = await SendAsync(_client, HttpMethod.Post, "/api/flows/executions/no-such-execution/stop", null);
            Assert.Equal(HttpStatusCode.OK, status);
            Assert.Equal(0, (int)body!["stopped"]!);
        }

        [Fact]
        public async Task HumanTask_SuspendCompleteViaApi_ReachesSucceeded()
        {
            // Flow: Start → HumanTask (api completion) → Done. State casing mirrors the UI export format.
            var name = "exec-ht-" + Guid.NewGuid().ToString("N");
            var payload = new JObject
            {
                ["name"] = name,
                ["startAt"] = "Start",
                ["states"] = new JObject
                {
                    ["Start"] = new JObject { ["Type"] = "Pass", ["Next"] = "Approve" },
                    ["Approve"] = new JObject
                    {
                        ["type"] = "HumanTask",
                        ["next"] = "Done",
                        ["task"] = new JObject { ["title"] = "e2e approval", ["assignee"] = "tester" },
                        ["completion"] = new JObject { ["Type"] = "api" }
                    },
                    ["Done"] = new JObject { ["Type"] = "Pass", ["end"] = true }
                }
            };
            var (rs, rb) = await SendAsync(_client, HttpMethod.Post, "/api/flows", payload);
            Assert.Equal(HttpStatusCode.OK, rs);
            var flowId = (string)rb!["id"]!;

            // Async start suspends at the human task.
            var (xs, xb) = await SendAsync(_client, HttpMethod.Post, $"/api/flows/execute/{flowId}", new { requestId = "REQ-1" });
            Assert.Equal(HttpStatusCode.OK, xs);
            var executionId = (string)xb!["executionId"]!;

            // The run reaches Suspended in the stored list.
            var suspended = await WaitForStatusAsync(executionId, "Suspended");
            Assert.Equal("Suspended", suspended);

            // The pending task is visible with its title/assignee (what the inbox renders).
            var (ts, tb) = await SendAsync(_client, HttpMethod.Get, $"/api/human-tasks?status=Pending&executionId={Uri.EscapeDataString(executionId)}", null);
            Assert.Equal(HttpStatusCode.OK, ts);
            var tasks = (JArray)tb!;
            Assert.Single(tasks);
            var task = tasks[0]!;
            var taskId = (string)task["taskId"]!;
            Assert.Equal("e2e approval", (string)task["title"]!);
            Assert.Equal("tester", (string)task["assignee"]!);

            // Completing the task resumes the run.
            var (cs, cb) = await SendAsync(_client, HttpMethod.Post, $"/api/human-tasks/{taskId}/complete", new { approved = true });
            Assert.Equal(HttpStatusCode.OK, cs);
            Assert.Equal(taskId, (string)cb!["taskId"]!);
            Assert.Equal("Completed", (string)cb["status"]!);
            Assert.Equal(executionId, (string)cb["executionId"]!);
            Assert.Equal("Done", (string)cb["nextState"]!);
            Assert.False((bool)cb["isEnd"]!);

            // The execution reaches Succeeded.
            var succeeded = await WaitForStatusAsync(executionId, "Succeeded");
            Assert.Equal("Succeeded", succeeded);

            // Double-completing the same task conflicts.
            var (c2, c2b) = await SendAsync(_client, HttpMethod.Post, $"/api/human-tasks/{taskId}/complete", new { approved = true });
            Assert.Equal(HttpStatusCode.Conflict, c2);
            Assert.NotNull(c2b!["error"]);
        }

        // ── test host factory (isolated state dirs) ──

        public sealed class Factory : WebApplicationFactory<Program>
        {
            private readonly string _root = Path.Combine(Path.GetTempPath(), "stepflow-executions-tests", Guid.NewGuid().ToString("N"));

            protected override void ConfigureWebHost(IWebHostBuilder builder) =>
                builder.ConfigureAppConfiguration((_, config) =>
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        [FlowStateOptions.SectionName + ":DiskPath"] = Path.Combine(_root, "flow-state"),
                        [FormDataOptions.SectionName + ":DatabasePath"] = Path.Combine(_root, "stepflow_data.db"),
                        [WorkspaceOptions.SectionName + ":RootDirectory"] = Path.Combine(_root, "workspace-data"),
                        [DataExchangeOptions.SectionName + ":ProfilesDirectory"] = Path.Combine(_root, "dx", "profiles"),
                        [DataExchangeOptions.SectionName + ":InboxDirectory"] = Path.Combine(_root, "dx", "inbox"),
                        [DataExchangeOptions.SectionName + ":OutputDirectory"] = Path.Combine(_root, "dx", "output"),
                    }));

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
            }
        }
    }
}
