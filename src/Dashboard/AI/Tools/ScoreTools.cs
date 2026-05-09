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
Scores are automatically saved to history for trend analysis.

When the user asks about their FinOps maturity, biggest issues, or Crawl-level assessment (or anything similar), automatically evaluate ALL 7 Crawl dimensions below using QueryAzure and score each 0-5 with a one-line `detail` citing concrete numbers from the environment. Do not ask the user which dimensions to score — score them all.

Crawl dimensions (label / what to check):
  1. 'Budgets & thresholds' — list Cost Management budgets: count, amounts, and whether notifications are configured. Flag unrealistic amounts (e.g. $999,999,999) or missing alerts.
  2. 'Tagging for accountability' — query Resource Graph for total resource count and % carrying CostCenter, Owner, Environment (exact key names). Flag inconsistent casing (e.g. 'department' vs 'Department') and placeholder values like 'unassigned' or 'unknown'.
  3. 'Cost data exports' — list Cost Management exports. Score 0 if none exist.
  4. 'Cost alerts & scheduled actions' — list Cost Management anomaly alerts and scheduled actions. Score 0 if none.
  5. 'Governance guardrails' — list management-group policy assignments and check whether any enforce FinOps-specific tagging or cost controls at subscription scope.
  6. 'Waste identification & cleanup' — count unattached disks, unassociated public IPs, empty App Service plans, and empty resource groups via Resource Graph.
  7. 'Cost visibility & ownership' — month-to-date spend grouped by resource group and by top services.

Return the scores array with id = short slug, label = exact dimension name above, score = 0-5, detail = the one-line reason with numbers.")]
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
