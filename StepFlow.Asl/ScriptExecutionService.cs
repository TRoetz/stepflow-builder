using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace StepFunctionsApp.StepFunctions
{
    public class ScriptGlobals
    {
        public JToken input { get; set; } = new JObject();
        public JToken context { get; set; } = new JObject();
    }

    public class ScriptExecutionService
    {
        private readonly ILogger<ScriptExecutionService> _logger;

        public ScriptExecutionService(ILogger<ScriptExecutionService> logger)
        {
            _logger = logger;
        }

        public async Task<JToken> ExecuteScriptAsync(string language, string script, JToken inputData, CancellationToken ct = default)
        {
            language = language.ToLowerInvariant();

            // An explicitly declared language (transform://javascript|python|powershell|csharp|shell) always wins.
            // Content-based detection is only a fallback for unrecognized declarations; its heuristics can
            // false-positive on PowerShell comments ("import result") or strings ending in 'f' before a quote.
            if (!IsKnownScriptLanguage(language))
            {
                var detected = DetectScriptLanguage(script);
                if (detected != null)
                {
                    _logger.LogInformation("Declared language '{Declared}' not recognized; using detected language '{Detected}'.", language, detected);
                    language = detected;
                }
            }

            _logger.LogInformation("Executing scripting task. Language: {Language}", language);

            try
            {
                if (language == "csharp")
                {
                    return await ExecuteCSharpScriptAsync(script, inputData, ct);
                }

                if (language == "shell")
                {
                    return await ExecuteShellCommandAsync(script, inputData, ct);
                }

                return await ExecuteExternalScriptAsync(language, script, inputData, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Cancellation and state timeouts must propagate: the interpreter maps them to States.Timeout or graceful shutdown.
                throw;
            }
            catch (StepEngineException ex)
            {
                _logger.LogError(ex, "Script execution failed");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Script execution failed");
                // Surface the failure as a standard task error so state catch/retry clauses can match it.
                throw new StepEngineException("States.TaskFailed", $"{language} script failed: {ex.Message}");
            }
        }

        private static bool IsKnownScriptLanguage(string language) =>
            language is "javascript" or "python" or "powershell" or "ps1" or "csharp" or "shell";

        /// <summary>
        /// Auto-detect script language from code heuristics.
        /// Returns null if no confident detection, or the language key.
        /// </summary>
        private string? DetectScriptLanguage(string script)
        {
            var trimmed = script.Trim();

            // Python indicators
            bool hasPythonImport = trimmed.Contains("import os") || trimmed.Contains("import sys") || trimmed.Contains("import json") || trimmed.Contains("import re");
            bool hasPythonWith = trimmed.Contains("with open(") || trimmed.Contains("with open (");
            bool hasPythonDef = trimmed.Contains("def ") || trimmed.Contains("print(");
            bool hasPythonFstring = trimmed.Contains("f\"") || trimmed.Contains("f'\"");
            bool hasPythonSelf = trimmed.Contains("self.");
            bool hasPythonIndent = trimmed.Contains(":\n    ") || trimmed.Contains(":\n        ");
            int pythonScore = (hasPythonImport ? 3 : 0) + (hasPythonWith ? 3 : 0) + (hasPythonDef ? 1 : 0) + (hasPythonFstring ? 2 : 0) + (hasPythonSelf ? 2 : 0) + (hasPythonIndent ? 1 : 0);

            // JavaScript indicators
            bool hasConst = trimmed.Contains("const ") || trimmed.Contains("let ") || trimmed.Contains("var ");
            bool hasRequire = trimmed.Contains("require(") || trimmed.Contains("require (");
            bool hasArrow = trimmed.Contains("=>");
            bool hasConsole = trimmed.Contains("console.");
            bool hasImportFrom = trimmed.Contains("import {") || trimmed.Contains("import ") && trimmed.Contains(" from ");
            int jsScore = (hasConst ? 1 : 0) + (hasRequire ? 2 : 0) + (hasArrow ? 1 : 0) + (hasConsole ? 2 : 0) + (hasImportFrom ? 2 : 0);

            // C# indicators
            bool hasNamespace = trimmed.Contains("namespace ");
            bool hasUsing = trimmed.Contains("using System") || trimmed.Contains("using ");
            bool hasCsharpTypes = trimmed.Contains("Console.WriteLine") || trimmed.Contains("var ") && trimmed.Contains(";\n");
            int csharpScore = (hasNamespace ? 2 : 0) + (hasUsing ? 1 : 0) + (hasCsharpTypes ? 2 : 0);

            // PowerShell indicators
            bool hasDollarVar = trimmed.Contains("$") && (trimmed.Contains("Write-Output") || trimmed.Contains("ConvertTo-Json"));
            int psScore = hasDollarVar ? 3 : 0;

            // Threshold: need score >= 3 to override declared language
            if (pythonScore >= 3 && pythonScore > jsScore && pythonScore > csharpScore) return "python";
            if (jsScore >= 3 && jsScore > pythonScore && jsScore > csharpScore) return "javascript";
            if (csharpScore >= 3 && csharpScore > pythonScore && csharpScore > jsScore) return "csharp";
            if (psScore >= 3) return "powershell";

            return null;
        }

        private async Task<JToken> ExecuteCSharpScriptAsync(string script, JToken inputData, CancellationToken ct)
        {
            _logger.LogDebug("Evaluating C# Script via Roslyn CSharpScript API");
            
            // Build Globals object
            var globals = new ScriptGlobals
            {
                input = inputData,
                context = new JObject { ["input"] = inputData }
            };

            // Setup Roslyn compilation options
            var options = ScriptOptions.Default
                .WithReferences(typeof(JToken).Assembly, typeof(JsonConvert).Assembly, typeof(System.Linq.Enumerable).Assembly)
                .WithImports("System", "System.Collections.Generic", "System.Linq", "Newtonsoft.Json", "Newtonsoft.Json.Linq");

            // Evaluate C# Script
            var scriptResult = await CSharpScript.EvaluateAsync(script, options, globals, cancellationToken: ct);
            
            if (scriptResult == null) return new JObject();
            return JToken.FromObject(scriptResult);
        }

        private async Task<JToken> ExecuteShellCommandAsync(string command, JToken inputData, CancellationToken ct)
        {
            _logger.LogDebug("Executing system shell command: {Command}", command);

            var isWindows = OperatingSystem.IsWindows();
            var shell = isWindows ? "cmd.exe" : "bash";
            var shellArgs = isWindows ? $"/c \"{command}\"" : $"-c \"{command}\"";

            var startInfo = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = shellArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            // Feed input JSON into standard input
            await process.StandardInput.WriteAsync(inputData.ToString(Formatting.None));
            process.StandardInput.Close();

            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);

            await Task.Run(() => process.WaitForExit(), ct);

            var stdout = await outputTask;
            var stderr = await errorTask;

            if (process.ExitCode != 0)
            {
                throw new Exception($"Shell command exited with code {process.ExitCode}. Error: {stderr}");
            }

            // Return stdout as raw string or parsed JSON if possible
            stdout = stdout.Trim();
            try
            {
                return JToken.Parse(stdout);
            }
            catch
            {
                return new JObject
                {
                    ["output"] = stdout,
                    ["error"] = stderr
                };
            }
        }

        private async Task<JToken> ExecuteExternalScriptAsync(string language, string script, JToken inputData, CancellationToken ct)
        {
            string extension;
            string executable;
            string wrapperCode;

            var contextJson = new JObject
            {
                ["input"] = inputData
            }.ToString(Formatting.None);

            if (language == "javascript" || language == "node")
            {
                // .mjs forces ESM by file extension on all modern Node versions.
                // (--input-type=module is rejected by Node when a file argument is given.)
                extension = "mjs";
                executable = "node";
                wrapperCode = $@"
import fs from 'fs';
const context = JSON.parse(fs.readFileSync(0, 'utf-8'));
(async () => {{
    try {{
        const execute = async (ctx) => {{
            const data = ctx?.input ?? ctx;
            {script}
        }};
        const result = await execute(context);
        console.log(JSON.stringify(result));
    }} catch (err) {{
        console.error(err.message);
        process.exit(1);
    }}
}})();";
            }
            else if (language == "python" || language == "py")
            {
                extension = "py";
                // Try 'python' then 'py' fallback
                executable = OperatingSystem.IsWindows() ? "python" : "python3";
                
                // Indent script for function wrapper in python
                var indentedScript = string.Join("\n", script.Split('\n').Select(line => "        " + line));
                wrapperCode = $@"
import sys, json, traceback
try:
    context = json.loads(sys.stdin.read())
    def execute(context):
        input = context.get('input', {{}})
        data = input  # alias commonly used by LLM-generated code
{indentedScript}

    result = execute(context)
    print(json.dumps(result))
except Exception as e:
    sys.stderr.write(traceback.format_exc())
    sys.exit(1)
";
            }
            else if (language == "powershell" || language == "ps1")
            {
                extension = "ps1";
                executable = "powershell";
                wrapperCode = $@"
$context = [Console]::In.ReadToEnd() | ConvertFrom-Json
function Execute($context) {{
    $input_data = $context.input
    {script}
}}
$result = Execute $context
ConvertTo-Json $result -Depth 10 | Write-Output";
            }
            else
            {
                throw new NotSupportedException($"Scripting language '{language}' is not supported");
            }

            var tempFilePath = Path.Combine(Path.GetTempPath(), $"stepflow_script_{Guid.NewGuid():N}.{extension}");
            await File.WriteAllTextAsync(tempFilePath, wrapperCode, Encoding.UTF8, ct);

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = $"\"{tempFilePath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();

                // Send context via stdin
                await process.StandardInput.WriteAsync(contextJson);
                process.StandardInput.Close();

                var outputTask = process.StandardOutput.ReadToEndAsync(ct);
                var errorTask = process.StandardError.ReadToEndAsync(ct);

                await Task.Run(() => process.WaitForExit(), ct);

                var stdout = await outputTask;
                var stderr = await errorTask;

                if (process.ExitCode != 0)
                {
                    throw new Exception($"{language} execution error: {stderr}");
                }

                stdout = stdout.Trim();
                try
                {
                    return JToken.Parse(stdout);
                }
                catch
                {
                    return new JValue(stdout);
                }
            }
            finally
            {
                // Clean up file safely
                try { File.Delete(tempFilePath); } catch { }
            }
        }
    }
}
