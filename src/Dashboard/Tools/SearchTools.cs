using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace AzureFinOps.Dashboard.Tools;

/// <summary>
/// Search tools: grep (text search) and glob (find files by pattern).
/// Scoped to the same workspace as FileSystemTools.
/// </summary>
public static class SearchTools
{
    private static readonly string WorkDir = Path.Combine(Path.GetTempPath(), "finops-agent-files");
    private const int MaxResults = 50;
    private const int MaxLineLength = 500;

    static SearchTools()
    {
        Directory.CreateDirectory(WorkDir);
    }

    public static IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(Grep, "Grep",
            "Search for text or regex pattern in files within the workspace. Returns matching lines with file paths and line numbers.");
        yield return AIFunctionFactory.Create(Glob, "Glob",
            "Find files matching a glob pattern (e.g. '**/*.csv', '*.json') within the workspace.");
    }

    private static string Grep(
        [Description("Text or regex pattern to search for")] string pattern,
        [Description("Optional glob filter for files to search (e.g. '*.csv'). Default: all files.")] string? includePattern,
        [Description("Whether the pattern is a regex. Default: false (literal text search).")] bool? isRegexp)
    {
        if (!Directory.Exists(WorkDir))
            return "Error: Workspace directory is empty.";

        var useRegex = isRegexp ?? false;
        Regex? regex = null;
        if (useRegex)
            regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(5));

        var searchPattern = string.IsNullOrWhiteSpace(includePattern) ? "*" : includePattern;
        var files = Directory.GetFiles(WorkDir, searchPattern, SearchOption.AllDirectories);
        var results = new List<string>();

        foreach (var file in files)
        {
            if (results.Count >= MaxResults) break;

            try
            {
                var lines = File.ReadAllLines(file);
                var relativePath = Path.GetRelativePath(WorkDir, file);

                for (int i = 0; i < lines.Length && results.Count < MaxResults; i++)
                {
                    var line = lines[i];
                    bool match = useRegex
                        ? regex!.IsMatch(line)
                        : line.Contains(pattern, StringComparison.OrdinalIgnoreCase);

                    if (match)
                    {
                        var truncated = line.Length > MaxLineLength ? line[..MaxLineLength] + "..." : line;
                        results.Add($"{relativePath}:{i + 1}: {truncated}");
                    }
                }
            }
            catch { /* skip binary or unreadable files */ }
        }

        return results.Count == 0
            ? $"No matches for '{pattern}'"
            : $"{results.Count} match(es):\n{string.Join("\n", results)}";
    }

    private static string Glob(
        [Description("Glob pattern to match files (e.g. '**/*.csv', 'data/*.json')")] string pattern)
    {
        if (!Directory.Exists(WorkDir))
            return "Error: Workspace directory is empty.";

        // Simple glob: use SearchOption.AllDirectories for ** patterns
        var searchOption = pattern.Contains("**") ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var filePattern = pattern.Replace("**/", "").Replace("**", "*");

        var files = Directory.GetFiles(WorkDir, filePattern, searchOption)
            .Take(MaxResults)
            .Select(f =>
            {
                var fi = new FileInfo(f);
                return $"  {Path.GetRelativePath(WorkDir, f)} ({fi.Length:N0} bytes)";
            })
            .ToList();

        return files.Count == 0
            ? $"No files matching '{pattern}'"
            : $"{files.Count} file(s):\n{string.Join("\n", files)}";
    }
}
