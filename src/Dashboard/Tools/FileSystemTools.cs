using System.ComponentModel;
using Microsoft.Extensions.AI;

namespace AzureFinOps.Dashboard.Tools;

/// <summary>
/// File system tools: read, write, edit, and list files/directories.
/// Scoped to the OS temp directory for security.
/// </summary>
public static class FileSystemTools
{
    private static readonly string WorkDir = Path.Combine(Path.GetTempPath(), "finops-agent-files");
    private const int MaxFileSizeBytes = 2 * 1024 * 1024; // 2 MB read limit

    static FileSystemTools()
    {
        Directory.CreateDirectory(WorkDir);
    }

    public static IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(ReadFile, "ReadFile",
            "Read the contents of a file. Path is relative to the agent workspace. Returns file text or an error.");
        yield return AIFunctionFactory.Create(WriteFile, "WriteFile",
            "Create or overwrite a file with the given content. Path is relative to the agent workspace. Creates parent directories if needed.");
        yield return AIFunctionFactory.Create(EditFile, "EditFile",
            "Edit a file by replacing an exact string with a new string. The old string must appear exactly once.");
        yield return AIFunctionFactory.Create(ListDirectory, "ListDirectory",
            "List files and subdirectories at the given path (relative to workspace). Returns names, sizes, and types.");
    }

    private static string ResolveSafePath(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(WorkDir, relativePath));
        if (!full.StartsWith(WorkDir, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path traversal denied — must stay within workspace.");
        return full;
    }

    private static async Task<string> ReadFile(
        [Description("Relative file path within the workspace")] string path,
        [Description("Optional start line (1-based). Omit to read from beginning.")] int? startLine,
        [Description("Optional end line (1-based, inclusive). Omit to read to end.")] int? endLine)
    {
        try
        {
            var fullPath = ResolveSafePath(path);
            if (!File.Exists(fullPath))
                return $"Error: File not found: {path}";

            var info = new FileInfo(fullPath);
            if (info.Length > MaxFileSizeBytes)
                return $"Error: File too large ({info.Length:N0} bytes). Max: {MaxFileSizeBytes:N0} bytes.";

            var lines = await File.ReadAllLinesAsync(fullPath);
            var start = Math.Max(0, (startLine ?? 1) - 1);
            var end = Math.Min(lines.Length, endLine ?? lines.Length);

            if (start >= lines.Length)
                return $"Error: startLine {startLine} exceeds file length ({lines.Length} lines).";

            var selected = lines[start..end];
            return $"File: {path} ({lines.Length} lines total, showing {start + 1}-{end})\n{string.Join("\n", selected)}";
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    private static async Task<string> WriteFile(
        [Description("Relative file path within the workspace")] string path,
        [Description("File content to write")] string content)
    {
        try
        {
            var fullPath = ResolveSafePath(path);
            var dir = Path.GetDirectoryName(fullPath);
            if (dir is not null) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(fullPath, content);
            return $"File written: {path} ({content.Length} chars)";
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    private static async Task<string> EditFile(
        [Description("Relative file path within the workspace")] string path,
        [Description("Exact string to find and replace (must appear exactly once)")] string oldString,
        [Description("Replacement string")] string newString)
    {
        try
        {
            var fullPath = ResolveSafePath(path);
            if (!File.Exists(fullPath))
                return $"Error: File not found: {path}";

            var text = await File.ReadAllTextAsync(fullPath);
            var count = CountOccurrences(text, oldString);

            if (count == 0)
                return "Error: oldString not found in the file.";
            if (count > 1)
                return $"Error: oldString found {count} times — must appear exactly once.";

            var newText = text.Replace(oldString, newString);
            await File.WriteAllTextAsync(fullPath, newText);
            return $"File edited: {path} (replaced {oldString.Length} chars with {newString.Length} chars)";
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    private static string ListDirectory(
        [Description("Relative directory path within the workspace. Use '.' or '' for root.")] string path)
    {
        try
        {
            var fullPath = ResolveSafePath(string.IsNullOrWhiteSpace(path) ? "." : path);
            if (!Directory.Exists(fullPath))
                return $"Error: Directory not found: {path}";

            var entries = new List<string>();
            foreach (var dir in Directory.GetDirectories(fullPath))
                entries.Add($"  {Path.GetFileName(dir)}/");
            foreach (var file in Directory.GetFiles(fullPath))
            {
                var fi = new FileInfo(file);
                entries.Add($"  {fi.Name} ({fi.Length:N0} bytes)");
            }

            return entries.Count == 0
                ? $"Directory: {path} (empty)"
                : $"Directory: {path} ({entries.Count} items)\n{string.Join("\n", entries)}";
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    private static int CountOccurrences(string text, string search)
    {
        int count = 0, index = 0;
        while ((index = text.IndexOf(search, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += search.Length;
        }
        return count;
    }
}
