namespace AzureFinOps.Dashboard.Tools;

/// <summary>
/// Truncates large tool results to keep MAF conversation history small.
/// Full results are saved to the agent workspace so the LLM can use ReadFile to access them.
/// </summary>
public static class LargeResultHelper
{
    private static readonly string WorkspaceDir = Path.Combine(Path.GetTempPath(), "finops-agent-files");
    private const int DefaultMaxChars = 300;

    static LargeResultHelper()
    {
        Directory.CreateDirectory(WorkspaceDir);
    }

    /// <summary>
    /// If result exceeds 300 chars, saves full result to disk and returns a truncated preview.
    /// The LLM can use ReadFile to access the full data when needed.
    /// </summary>
    public static string Truncate(string result, string toolName)
    {
        if (string.IsNullOrEmpty(result) || result.Length <= DefaultMaxChars)
            return result;

        var shortId = Guid.NewGuid().ToString("N")[..6];
        var fileId = $"{toolName}_{DateTime.UtcNow:HHmmss}_{shortId}";
        var filePath = Path.Combine(WorkspaceDir, $"{fileId}.json");

        File.WriteAllText(filePath, result);

        var preview = result[..DefaultMaxChars];
        return $"{preview}\n\n... ({result.Length:N0} chars total — full result saved to: {filePath})\nUse ReadFile to access the complete data, or call this tool again with a larger maxChars value.";
    }
}
