using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.AI;

namespace AzureFinOps.Dashboard.Tools;

/// <summary>
/// Executes scripts (Python 3, bash, SQLite) on the server.
/// ⚠️ TEMPORARY: This runs code directly on the App Service with no sandboxing.
/// TODO: Migrate to Azure Container Apps dynamic sessions for secure, isolated execution.
/// </summary>
public class CodeExecutionTools
{
    private const int TimeoutSeconds = 120;
    private const int MaxOutputChars = 50_000;

    private static readonly HashSet<string> AllowedLanguages = new(StringComparer.OrdinalIgnoreCase)
    {
        "python", "bash", "sqlite"
    };

    private readonly UserTokens _tokens;

    public CodeExecutionTools(UserTokens tokens) => _tokens = tokens;

    public IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(RunScript, "RunScript", @"Executes a script and returns stdout + stderr. 120s timeout, 50KB output limit.
Languages: python (requests, pandas, numpy, openpyxl, tabulate, python-dateutil available), bash (curl, jq, sqlite3, awk, sed, grep available), sqlite (in-memory).
Use for data processing, calculations, and complex API workflows. Prefer QueryAzure tool for simple Azure API calls.
Env vars available: AZURE_TOKEN (Azure ARM bearer), GRAPH_TOKEN (Microsoft Graph), LOG_ANALYTICS_TOKEN (Log Analytics / App Insights query APIs).
For Azure ARM APIs use https://management.azure.com with AZURE_TOKEN. For Graph use https://graph.microsoft.com with GRAPH_TOKEN.
For Log Analytics KQL use https://api.loganalytics.io/v1/workspaces/{wsId}/query with LOG_ANALYTICS_TOKEN.
For App Insights KQL use https://api.applicationinsights.io/v1/apps/{appId}/query with LOG_ANALYTICS_TOKEN.
Public (no auth): https://prices.azure.com/api/retail/prices?$filter=... — retail pricing.");
    }

    private async Task<string> RunScript(
        [Description("The language to execute: python, bash, or sqlite")] string language,
        [Description("The script/code to execute.")] string code)
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

        var psi = new ProcessStartInfo
        {
            FileName = command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = language == "sqlite",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Set tokens per-process (not global env vars) to avoid race conditions
        // between concurrent users
        if (_tokens.AzureToken is not null)
            psi.Environment["AZURE_TOKEN"] = _tokens.AzureToken;
        if (_tokens.GraphToken is not null)
            psi.Environment["GRAPH_TOKEN"] = _tokens.GraphToken;
        if (_tokens.LogAnalyticsToken is not null)
            psi.Environment["LOG_ANALYTICS_TOKEN"] = _tokens.LogAnalyticsToken;

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

    private static string Truncate(string text)
        => text.Length > MaxOutputChars
            ? text[..MaxOutputChars] + $"\n... (truncated, {text.Length} total chars)"
            : text;
}
