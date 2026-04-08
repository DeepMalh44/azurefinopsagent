using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AzureFinOps.Dashboard.AI.Tools;

public static class ScoreTools
{
    private static readonly string ScoreDir = Path.Combine(
        Environment.GetEnvironmentVariable("HOME") ?? Path.GetTempPath(), "finops-agent-scores");
    private static readonly string ScoreFile = Path.Combine(ScoreDir, "score-history.json");
    private static readonly Lock _fileLock = new();

    static ScoreTools()
    {
        Directory.CreateDirectory(ScoreDir);
    }

    public static IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(ReportMaturityScore);
        yield return AIFunctionFactory.Create(GetScoreHistory);
    }

    [Description(@"Report FinOps maturity scores after evaluating a level (crawl, walk, run, or playbook). Call this AFTER you have queried the relevant APIs and determined the scores. Each dimension gets a score from 0-5:
0 = Not started / no data
1 = Critical issues found
2 = Needs significant work
3 = Acceptable but room for improvement
4 = Good shape
5 = Excellent / best practice
Scores are automatically saved to history for trend analysis.")]
    private static string ReportMaturityScore(
        [Description("Level: 'crawl', 'walk', 'run', or 'playbook'")] string level,
        [Description(@"JSON array of score objects, e.g.: [{""id"":""tagging"",""label"":""Tagging"",""score"":3,""detail"":""45% of resources tagged""}]")] string scores)
    {
        // Persist score to history file for trend analysis
        try
        {
            var entry = new ScoreHistoryEntry
            {
                Timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                Level = level.ToLowerInvariant(),
                Scores = scores
            };

            lock (_fileLock)
            {
                var history = LoadHistory();
                history.Add(entry);
                File.WriteAllText(ScoreFile, JsonSerializer.Serialize(history));
            }
        }
        catch { /* non-critical — don't break scoring if persistence fails */ }

        return $"__MATURITY_SCORE__:{level}:{scores}";
    }

    [Description(@"Retrieve historical FinOps maturity scores for trend analysis. Returns all past scores so you can compare current vs previous assessments and show improvement or regression over time. Use this when the user asks about score trends, progress, or historical comparison.")]
    private static string GetScoreHistory(
        [Description("Optional: filter by level ('crawl', 'walk', 'run', 'playbook'). Omit to get all levels.")] string? level = null)
    {
        List<ScoreHistoryEntry> history;
        lock (_fileLock)
        {
            history = LoadHistory();
        }

        if (!string.IsNullOrWhiteSpace(level))
            history = history.Where(h => h.Level.Equals(level.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

        if (history.Count == 0)
            return "No score history found. Run a maturity scoring first to establish a baseline.";

        return JsonSerializer.Serialize(history);
    }

    private static List<ScoreHistoryEntry> LoadHistory()
    {
        try
        {
            if (File.Exists(ScoreFile))
            {
                var json = File.ReadAllText(ScoreFile);
                return JsonSerializer.Deserialize<List<ScoreHistoryEntry>>(json) ?? [];
            }
        }
        catch { }
        return [];
    }

    private class ScoreHistoryEntry
    {
        public string Timestamp { get; set; } = "";
        public string Level { get; set; } = "";
        public string Scores { get; set; } = "";
    }
}
