using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.AI;

namespace AzureFinOps.Dashboard.Tools;

/// <summary>
/// Executes scripts (Python 3, bash, SQLite) on the server.
/// ⚠️ TEMPORARY: This runs code directly on the App Service with no sandboxing.
/// TODO: Migrate to Azure Container Apps dynamic sessions for secure, isolated execution.
/// </summary>
public static class CodeExecutionTools
{
    private const int TimeoutSeconds = 30;
    private const int MaxOutputChars = 50_000;

    private static readonly HashSet<string> AllowedLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "python", "bash", "sqlite"
    };

    public static IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(RunScript, "RunScript",
            "Executes a script on the server and returns stdout + stderr. " +
            "Supported languages: python (python3 with pandas, numpy, openpyxl, tabulate, python-dateutil), " +
            "bash (with jq, sqlite3, awk, sed, grep), " +
            "sqlite (sqlite3 in-memory). " +
            "Scripts have a 30-second timeout and 50KB output limit. " +
            "For Python: write complete scripts that print results to stdout. " +
            "For bash: pipe commands, use jq for JSON. " +
            "For SQLite: pass SQL statements that will run against an in-memory database.");
    }

    private static async Task<string> RunScript(
        [Description("The language to execute: python, bash, or sqlite")] string language,
        [Description("The script/code to execute. For Python, write a complete script. For bash, write shell commands. For SQLite, write SQL statements.")] string code)
    {
        if (!AllowedLanguages.Contains(language))
            return $"Error: Unsupported language '{language}'. Allowed: {string.Join(", ", AllowedLanguages)}";

        if (string.IsNullOrWhiteSpace(code))
            return "Error: No code provided.";

        var (command, args) = language.ToLowerInvariant() switch
        {
            "python" => ("python3", "-c"),
            "bash" => ("bash", "-c"),
            "sqlite" => ("sqlite3", ""),
            _ => throw new InvalidOperationException()
        };

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = language == "sqlite",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (language == "sqlite")
            {
                // sqlite3 reads from stdin
                psi.Arguments = ":memory:";
            }
            else
            {
                psi.ArgumentList.Add(args);
                psi.ArgumentList.Add(code);
            }

            using var process = new Process { StartInfo = psi };
            process.Start();

            if (language == "sqlite")
            {
                await process.StandardInput.WriteAsync(code);
                process.StandardInput.Close();
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            var exited = process.WaitForExit(TimeoutSeconds * 1000);
            if (!exited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return $"Error: Script timed out after {TimeoutSeconds} seconds.";
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            var result = "";
            if (!string.IsNullOrEmpty(stdout))
                result += $"=== STDOUT ===\n{Truncate(stdout)}\n";
            if (!string.IsNullOrEmpty(stderr))
                result += $"=== STDERR ===\n{Truncate(stderr)}\n";
            if (string.IsNullOrEmpty(result))
                result = "(no output)";

            result += $"\nExit code: {process.ExitCode}";
            return result;
        }
        catch (Exception ex)
        {
            return $"Error executing {language}: {ex.Message}";
        }
    }

    private static string Truncate(string text)
        => text.Length > MaxOutputChars
            ? text[..MaxOutputChars] + $"\n... (truncated, {text.Length} total chars)"
            : text;
}
