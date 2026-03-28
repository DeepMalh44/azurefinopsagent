using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AzureFinOps.Dashboard.Tools;

/// <summary>
/// Persistent memory tools: store and recall facts across sessions.
/// Each user gets their own memory namespace.
/// </summary>
public class MemoryTools
{
    private static readonly string MemoryDir = Path.Combine(Path.GetTempPath(), "finops-agent-memory");
    private const int MaxMemories = 200;
    private const int MaxValueLength = 10_000;

    private readonly long _userId;

    public MemoryTools(long userId) => _userId = userId;

    static MemoryTools()
    {
        Directory.CreateDirectory(MemoryDir);
    }

    public IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(StoreMemory, "StoreMemory",
            "Store a fact or preference in persistent memory. Memory survives across sessions. Use a descriptive key.");
        yield return AIFunctionFactory.Create(RecallMemory, "RecallMemory",
            "Recall a specific fact from persistent memory by key.");
        yield return AIFunctionFactory.Create(ListMemories, "ListMemories",
            "List all stored memory keys for the current user.");
        yield return AIFunctionFactory.Create(DeleteMemory, "DeleteMemory",
            "Delete a specific memory by key.");
    }

    private string GetUserMemoryFile() => Path.Combine(MemoryDir, $"user_{_userId}.json");

    private Dictionary<string, string> LoadMemories()
    {
        var file = GetUserMemoryFile();
        if (!File.Exists(file)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var json = File.ReadAllText(file);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch { return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
    }

    private void SaveMemories(Dictionary<string, string> memories)
    {
        var file = GetUserMemoryFile();
        File.WriteAllText(file, JsonSerializer.Serialize(memories, new JsonSerializerOptions { WriteIndented = true }));
    }

    private string StoreMemory(
        [Description("Descriptive key for the memory (e.g. 'preferred_currency', 'subscription_ids', 'team_budget_goal')")] string key,
        [Description("The value to remember")] string value)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(key))
                return "Error: Key cannot be empty.";
            if (value.Length > MaxValueLength)
                return $"Error: Value too long ({value.Length} chars). Max: {MaxValueLength}.";

            var memories = LoadMemories();
            if (memories.Count >= MaxMemories && !memories.ContainsKey(key))
                return $"Error: Memory limit reached ({MaxMemories}). Delete some memories first.";

            memories[key] = value;
            SaveMemories(memories);
            return $"Stored: '{key}' ({value.Length} chars)";
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    private string RecallMemory(
        [Description("Key to look up")] string key)
    {
        try
        {
            var memories = LoadMemories();
            return memories.TryGetValue(key, out var value)
                ? $"{key}: {value}"
                : $"No memory found for key '{key}'.";
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    private string ListMemories()
    {
        try
        {
            var memories = LoadMemories();
            if (memories.Count == 0)
                return "No memories stored.";

            var keys = memories.Keys.Select(k => $"  {k} ({memories[k].Length} chars)");
            return $"{memories.Count} memories:\n{string.Join("\n", keys)}";
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }

    private string DeleteMemory(
        [Description("Key to delete")] string key)
    {
        try
        {
            var memories = LoadMemories();
            if (!memories.Remove(key))
                return $"No memory found for key '{key}'.";

            SaveMemories(memories);
            return $"Deleted: '{key}'";
        }
        catch (Exception ex) { return $"Error: {ex.Message}"; }
    }
}
