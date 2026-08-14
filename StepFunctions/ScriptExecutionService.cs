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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Script execution failed");
                return new JObject
                {
                    ["success"] = false,
                    ["error"] = ex.Message,
                    ["details"] = ex.ToString()
                };
            }
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
                extension = "js";
                executable = "node";
                wrapperCode = $@"
const fs = require('fs');
const context = JSON.parse(fs.readFileSync(0, 'utf-8'));
try {{
    const execute = (context) => {{
        {script}
    }};
    const result = execute(context);
    console.log(JSON.stringify(result));
}} catch (err) {{
    console.error(err.message);
    process.exit(1);
}}";
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
