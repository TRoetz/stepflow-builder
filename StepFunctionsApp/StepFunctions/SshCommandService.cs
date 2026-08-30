using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Renci.SshNet;
using Renci.SshNet.Common; // SshException lives in the Common sub-namespace

namespace StepFunctionsApp.StepFunctions
{
    // ═══════════════════════════════════════════════════════════════════════════════
    // SSH COMMAND SERVICE
    // Executes a command on a curated remote host (SshHostStore inventory) over SSH.
    // Every command is sanity-checked by the default AI provider before execution;
    // the check fails CLOSED — if the AI call errors, the command is blocked with
    // Ssh.SanityCheckFailed unless the node's override flag bypasses it.
    // ═══════════════════════════════════════════════════════════════════════════════

    public class SshCommandResult
    {
        public string HostName { get; set; } = "";
        public string Command { get; set; } = "";
        public int ExitCode { get; set; }
        public string Stdout { get; set; } = "";
        public string Stderr { get; set; } = "";
        public double DurationMs { get; set; }
    }

    public class SshCommandService
    {
        private const int MaxErrorDetailChars = 2000;

        private readonly SshHostStore _hosts;
        private readonly AiDecisionService _ai;
        private readonly ILogger<SshCommandService>? _logger;

        /// <summary>
        /// System prompt for the AI harmful-command review. decision=true means the
        /// command IS harmful and must be blocked (fail-closed).
        /// </summary>
        public const string SafetySystemPrompt = """
            You are a command safety reviewer for an automated workflow. You receive a shell command that is about to be executed on a remote Linux host via SSH.
            Decide whether the command is HARMFUL or DANGEROUS: destructive (rm -rf, dd, mkfs, wipe), data-exfiltrating (curl/wget with POST of local files, scp/rsync outward), privilege-escalation or persistence (chmod 777 /etc, crontab edits, useradd), service disruption (shutdown, reboot, systemctl stop on critical services), or obfuscated/malformed beyond recognition.
            Normal operational commands (df, free, ps, journalctl, grep, service status checks, package installs, log inspection) are SAFE.
            Respond with ONLY a JSON object: {"decision": true|false, "reasoning": "<one sentence>"} where decision=true means the command IS harmful and must be blocked.
            """;

        public SshCommandService(SshHostStore hosts, AiDecisionService ai, ILogger<SshCommandService>? logger = null)
        {
            _hosts = hosts;
            _ai = ai;
            _logger = logger;
        }

        /// <summary>
        /// Resolve the command to execute from a Task-state input payload. Pure and
        /// static so it is unit-testable without SSH or AI. Resolution chain:
        ///   ① input is a non-empty string → use it (raw text passthrough)
        ///   ② input.command is a non-empty string → use it
        ///   ③ input.command is an object → first non-empty of [text, output, answer, content] inside it
        ///   ④ same lookup on the input root itself (AI node object passed directly as input)
        ///   ⑤ otherwise null (caller throws Ssh.NoCommand)
        /// </summary>
        public static string? ResolveCommand(JToken? input)
        {
            if (input == null || input.Type == JTokenType.Null) return null;

            // ① raw string passthrough
            if (input is JValue sv && sv.Type == JTokenType.String)
            {
                var s = sv.Value?.ToString();
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
                return null;
            }

            if (input is JObject obj)
            {
                // ② explicit .command string
                var commandToken = obj["command"];
                if (commandToken is JValue cv && cv.Type == JTokenType.String)
                {
                    var c = cv.Value?.ToString();
                    if (!string.IsNullOrWhiteSpace(c)) return c.Trim();
                }

                // ③ .command object → first non-empty of the documented fallback fields
                if (commandToken != null && commandToken.Type != JTokenType.Null)
                {
                    var fromCommandObj = FirstNonEmptyText(commandToken, new[] { "text", "output", "answer", "content" });
                    if (fromCommandObj != null) return fromCommandObj;
                }

                // ④ same lookup on the input root (e.g. AI Text Gen object used directly as input)
                var fromRoot = FirstNonEmptyText(obj, new[] { "text", "output", "answer", "content" });
                if (fromRoot != null) return fromRoot;
            }

            // ⑤ nothing found
            return null;
        }

        private static string? FirstNonEmptyText(JToken token, string[] fields)
        {
            foreach (var field in fields)
            {
                var value = token[field];
                if (value is JValue v && v.Type == JTokenType.String)
                {
                    var s = v.Value?.ToString();
                    if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
                }
            }
            return null;
        }

        /// <summary>
        /// Execute a command on the named curated host. Throws StepEngineException with
        /// Ssh.* error codes for every failure mode (host not found, safety check failed,
        /// command blocked, non-zero exit).
        /// </summary>
        public async Task<SshCommandResult> ExecuteAsync(string hostName, string command, bool overrideSafetyCheck, int timeoutSeconds, CancellationToken ct)
        {
            var host = _hosts.FindByName(hostName)
                ?? throw new StepEngineException("Ssh.HostNotFound", $"No curated SSH host named '{hostName}' in ssh_hosts.json");

            if (!overrideSafetyCheck)
            {
                var check = await _ai.AskAsync(new AiDecisionInput
                {
                    Question = command,
                    SystemPrompt = SafetySystemPrompt,
                    ConfidenceThreshold = 0
                }, ct);

                // Fail closed: an unavailable AI provider blocks the command.
                if (check.IsError)
                    throw new StepEngineException("Ssh.SanityCheckFailed", $"AI safety check unavailable ({check.ErrorMessage}); enable the node override or fix the AI provider");

                if (check.Decision)
                    throw new StepEngineException("Ssh.CommandBlocked", $"Command blocked by AI safety review: {check.Reasoning}");

                _logger?.LogInformation("SSH command passed safety review on {Host}: {Command}", host.Name, Truncate(command));
            }
            else
            {
                _logger?.LogWarning("SSH safety check BYPASSED (override) for {Host}: {Command}", host.Name, Truncate(command));
            }

            // Fresh ConnectionInfo per client — reusing one across clients corrupts state in SSH.NET 2024.x.
            var connection = SshHostStore.BuildConnectionInfo(host, TimeSpan.FromSeconds(timeoutSeconds));

            using var client = new SshClient(connection);
            try
            {
                await client.ConnectAsync(ct).ConfigureAwait(false);
            }
            catch (SshException ex) when (!ct.IsCancellationRequested)
            {
                throw new StepEngineException("Ssh.CommandFailed", $"SSH connection to {host.Host}:{host.Port} failed: {ex.Message}");
            }

            try
            {
                var sw = Stopwatch.StartNew();
                // SSH.NET 2024.x is natively async: ExecuteAsync(ct) closes the channel on cancellation, so the remote command is killed.
                var cmd = client.CreateCommand(command);
                await cmd.ExecuteAsync(ct).ConfigureAwait(false);
                sw.Stop();

                var exitCode = cmd.ExitStatus ?? 0;
                if (exitCode != 0)
                    throw new StepEngineException("Ssh.CommandFailed", $"exit {exitCode}: {Truncate(cmd.Error)}");

                return new SshCommandResult
                {
                    HostName = host.Name,
                    Command = command,
                    ExitCode = exitCode,
                    Stdout = cmd.Result ?? "",
                    Stderr = cmd.Error ?? "",
                    DurationMs = sw.Elapsed.TotalMilliseconds
                };
            }
            catch (SshException ex) when (!ct.IsCancellationRequested)
            {
                throw new StepEngineException("Ssh.CommandFailed", $"SSH execution error on {host.Host}: {ex.Message}");
            }
            finally
            {
                client.Disconnect();
            }
        }

        private static string Truncate(string? text) =>
            string.IsNullOrEmpty(text) ? "" : (text.Length <= MaxErrorDetailChars ? text : text[..MaxErrorDetailChars] + "…");
    }
}
