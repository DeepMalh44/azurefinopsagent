using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AzureFinOps.Dashboard.AI.Tools;

/// <summary>
/// Saves and retrieves report schedule configurations.
/// Users can save report templates (prompt + scope + frequency) for later re-execution.
/// A background service could pick these up to auto-run, or they auto-execute on user visit.
/// </summary>
public static class ScheduleTools
{
    private static readonly string ScheduleDir = Path.Combine(
        Environment.GetEnvironmentVariable("HOME") ?? Path.GetTempPath(), "finops-agent-schedules");
    private static readonly string ScheduleFile = Path.Combine(ScheduleDir, "report-schedules.json");
    private static readonly Lock _fileLock = new();

    static ScheduleTools()
    {
        Directory.CreateDirectory(ScheduleDir);
    }

    public static IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(SaveReportSchedule);
        yield return AIFunctionFactory.Create(ListReportSchedules);
        yield return AIFunctionFactory.Create(DeleteReportSchedule);
    }

    [Description(@"Save a report schedule/template for recurring execution. The user can define what analysis to run, at what frequency, and for which scope. Saved reports can be re-run on demand or picked up by a scheduler.
Call this when the user says they want a recurring or scheduled report.")]
    private static string SaveReportSchedule(
        [Description("Short name for the report (e.g. 'Weekly Cost Overview', 'Monthly FinOps Score')")] string name,
        [Description("The full prompt/analysis to execute when the report runs")] string prompt,
        [Description("Frequency: 'daily', 'weekly', 'monthly'")] string frequency,
        [Description("Optional: scope to run against (subscription ID, resource group, management group). Omit for tenant-wide.")] string? scope = null,
        [Description("Optional: output format — 'chat' (default), 'html' (HTML presentation deck), 'script' (remediation script)")] string? outputFormat = "chat")
    {
        var id = Guid.NewGuid().ToString("N")[..8];
        var schedule = new ReportSchedule
        {
            Id = id,
            Name = name,
            Prompt = prompt,
            Frequency = frequency.ToLowerInvariant(),
            Scope = scope,
            OutputFormat = outputFormat ?? "chat",
            CreatedUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            LastRunUtc = null,
            Enabled = true
        };

        lock (_fileLock)
        {
            var schedules = LoadSchedules();
            schedules.Add(schedule);
            SaveSchedules(schedules);
        }

        return JsonSerializer.Serialize(new
        {
            saved = true,
            id,
            name,
            frequency,
            message = $"Report '{name}' saved (ID: {id}). It will run {frequency}. You can re-run it anytime by asking 'Run my saved report {name}' or view all saved reports with 'Show my report schedules'."
        });
    }

    [Description("List all saved report schedules. Shows report name, frequency, scope, last run time, and status.")]
    private static string ListReportSchedules()
    {
        List<ReportSchedule> schedules;
        lock (_fileLock)
        {
            schedules = LoadSchedules();
        }

        if (schedules.Count == 0)
            return "No saved report schedules found. Use 'Save a report schedule' to create one — for example: 'Schedule a weekly cost overview report for all my subscriptions'.";

        return JsonSerializer.Serialize(schedules);
    }

    [Description("Delete a saved report schedule by ID.")]
    private static string DeleteReportSchedule(
        [Description("The report schedule ID to delete")] string id)
    {
        lock (_fileLock)
        {
            var schedules = LoadSchedules();
            var removed = schedules.RemoveAll(s => s.Id == id);
            if (removed == 0)
                return $"No report schedule found with ID '{id}'. Use ListReportSchedules to see all saved reports.";
            SaveSchedules(schedules);
            return $"Report schedule '{id}' deleted.";
        }
    }

    /// <summary>Public accessor for listing schedules (used by background service or API endpoint).</summary>
    public static List<ReportSchedule> GetAll()
    {
        lock (_fileLock) { return LoadSchedules(); }
    }

    private static List<ReportSchedule> LoadSchedules()
    {
        try
        {
            if (File.Exists(ScheduleFile))
            {
                var json = File.ReadAllText(ScheduleFile);
                return JsonSerializer.Deserialize<List<ReportSchedule>>(json) ?? [];
            }
        }
        catch { }
        return [];
    }

    private static void SaveSchedules(List<ReportSchedule> schedules)
    {
        try
        {
            File.WriteAllText(ScheduleFile, JsonSerializer.Serialize(schedules, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    public class ReportSchedule
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Prompt { get; set; } = "";
        public string Frequency { get; set; } = "";
        public string? Scope { get; set; }
        public string OutputFormat { get; set; } = "chat";
        public string CreatedUtc { get; set; } = "";
        public string? LastRunUtc { get; set; }
        public bool Enabled { get; set; } = true;
    }
}
