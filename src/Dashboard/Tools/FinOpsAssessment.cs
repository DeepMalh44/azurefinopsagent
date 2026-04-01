using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AzureFinOps.Dashboard.Tools;

/// <summary>
/// Runs lightweight Azure API checks to score FinOps maturity across Crawl/Walk/Run.
/// Each check returns a 0–100 score. Checks are designed to be fast (single API call each)
/// and non-disruptive (read-only GET requests).
/// </summary>
public static class FinOpsAssessment
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// Score: 0–100 for successful checks, -1 for failed/error checks.
    public record CategoryScore(string Id, string Label, int Score, string Detail);
    public record LevelResult(string Level, CategoryScore[] Categories, int Overall);

    public record AssessmentResult(LevelResult Crawl, LevelResult Walk, LevelResult Run, string[] UnlockHints);

    public static async Task<AssessmentResult> RunAsync(string? azureToken, string? graphToken, string? logAnalyticsToken, ILogger logger)
    {
        var crawlTasks = new List<Task<CategoryScore>>();
        var walkTasks = new List<Task<CategoryScore>>();
        var runTasks = new List<Task<CategoryScore>>();
        var unlockHints = new List<string>();

        if (azureToken is not null)
        {
            // CRAWL checks (ARM only)
            crawlTasks.Add(CheckTagging(azureToken, logger));
            crawlTasks.Add(CheckOrphanedDisks(azureToken, logger));
            crawlTasks.Add(CheckAdvisor(azureToken, logger));
            crawlTasks.Add(CheckBudgets(azureToken, logger));

            // WALK checks (ARM only)
            walkTasks.Add(CheckReservationCoverage(azureToken, logger));
            walkTasks.Add(CheckAutoShutdown(azureToken, logger));
            walkTasks.Add(CheckAdvisorRightSizing(azureToken, logger));
            walkTasks.Add(CheckTaggingPolicy(azureToken, logger));

            // RUN checks (ARM + optional Graph)
            runTasks.Add(CheckCostExports(azureToken, logger));
            runTasks.Add(CheckManagementGroups(azureToken, logger));
        }
        else
        {
            unlockHints.Add("Connect Azure to unlock FinOps maturity scoring");
        }

        if (graphToken is not null)
        {
            runTasks.Add(CheckLicenseUsage(graphToken, logger));
        }
        else if (azureToken is not null)
        {
            unlockHints.Add("Connect Graph for license optimization scoring");
        }

        if (logAnalyticsToken is null && azureToken is not null)
        {
            unlockHints.Add("Connect Log Analytics for deeper utilization scoring");
        }

        var crawlScores = crawlTasks.Count > 0 ? await Task.WhenAll(crawlTasks) : Array.Empty<CategoryScore>();
        var walkScores = walkTasks.Count > 0 ? await Task.WhenAll(walkTasks) : Array.Empty<CategoryScore>();
        var runScores = runTasks.Count > 0 ? await Task.WhenAll(runTasks) : Array.Empty<CategoryScore>();

        // Calculate overall: exclude failed checks (score = -1)
        static int Overall(CategoryScore[] scores)
        {
            var valid = scores.Where(s => s.Score >= 0).ToArray();
            return valid.Length > 0 ? (int)valid.Average(s => s.Score) : -1;
        }

        return new AssessmentResult(
            new LevelResult("Crawl", crawlScores, Overall(crawlScores)),
            new LevelResult("Walk", walkScores, Overall(walkScores)),
            new LevelResult("Run", runScores, Overall(runScores)),
            unlockHints.ToArray()
        );
    }

    // ── CRAWL CHECKS ──

    private static async Task<CategoryScore> CheckTagging(string token, ILogger logger)
    {
        // Resource Graph: count resources with vs without required tags
        var query = new
        {
            query = "Resources | summarize total=count(), tagged=countif(isnotempty(tags['cost-center']) or isnotempty(tags['CostCenter']) or isnotempty(tags['costcenter']) or isnotempty(tags['environment']) or isnotempty(tags['Environment']) or isnotempty(tags['department']) or isnotempty(tags['Department']))"
        };
        var json = await PostResourceGraph(token, query);
        if (json is null)
            return new CategoryScore("tagging", "Tagging Coverage", -1, "Could not query resources");

        try
        {
            var rows = json.Value.GetProperty("data").GetProperty("rows");
            if (rows.GetArrayLength() > 0)
            {
                var row = rows[0];
                var total = row[0].GetInt64();
                var tagged = row[1].GetInt64();
                if (total == 0) return new CategoryScore("tagging", "Tagging Coverage", 100, "No resources found");
                var pct = (int)(tagged * 100 / total);
                return new CategoryScore("tagging", "Tagging Coverage", pct, $"{tagged}/{total} resources tagged ({pct}%)");
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Tagging check parse error"); }
        return new CategoryScore("tagging", "Tagging Coverage", -1, "Unable to determine tagging coverage");
    }

    private static async Task<CategoryScore> CheckOrphanedDisks(string token, ILogger logger)
    {
        var query = new
        {
            query = "Resources | where type =~ 'microsoft.compute/disks' | summarize total=count(), orphaned=countif(isnull(managedBy) or managedBy == '')"
        };
        var json = await PostResourceGraph(token, query);
        if (json is null)
            return new CategoryScore("orphaned", "Orphaned Resources", -1, "Could not query disks");

        try
        {
            var rows = json.Value.GetProperty("data").GetProperty("rows");
            if (rows.GetArrayLength() > 0)
            {
                var row = rows[0];
                var total = row[0].GetInt64();
                var orphaned = row[1].GetInt64();
                if (total == 0) return new CategoryScore("orphaned", "Orphaned Resources", 100, "No disks found");
                var pct = (int)((total - orphaned) * 100 / total);
                return new CategoryScore("orphaned", "Orphaned Resources", pct, orphaned == 0 ? "No orphaned disks found" : $"{orphaned} orphaned disk(s) out of {total}");
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Orphaned disks check parse error"); }
        return new CategoryScore("orphaned", "Orphaned Resources", -1, "Unable to check orphaned disks");
    }

    private static async Task<CategoryScore> CheckAdvisor(string token, ILogger logger)
    {
        // Count cost recommendations — fewer open = higher score
        var url = "https://management.azure.com/providers/Microsoft.Advisor/recommendations?api-version=2023-01-01&$filter=Category eq 'Cost'&$top=100";
        var json = await GetArmJson(token, url);
        if (json is null)
            return new CategoryScore("advisor", "Advisor Recommendations", -1, "Could not query Advisor");

        try
        {
            var count = 0;
            if (json.Value.TryGetProperty("value", out var arr))
                count = arr.GetArrayLength();
            // Score: 100 if 0 recs, 80 if 1-3, 60 if 4-10, 40 if 11-20, 20 if 21-50, 0 if 50+
            var score = count switch
            {
                0 => 100,
                <= 3 => 80,
                <= 10 => 60,
                <= 20 => 40,
                <= 50 => 20,
                _ => 0
            };
            return new CategoryScore("advisor", "Advisor Recommendations", score, count == 0 ? "No open cost recommendations" : $"{count} open cost recommendation(s)");
        }
        catch (Exception ex) { logger.LogWarning(ex, "Advisor check parse error"); }
        return new CategoryScore("advisor", "Advisor Recommendations", -1, "Unable to check Advisor");
    }

    private static async Task<CategoryScore> CheckBudgets(string token, ILogger logger)
    {
        // Count subscriptions with budgets vs total subscriptions
        var subsJson = await GetArmJson(token, "https://management.azure.com/subscriptions?api-version=2022-12-01");
        if (subsJson is null)
            return new CategoryScore("budgets", "Budget Alerts", -1, "Could not list subscriptions");

        try
        {
            var subs = subsJson.Value.GetProperty("value");
            var totalSubs = subs.GetArrayLength();
            if (totalSubs == 0) return new CategoryScore("budgets", "Budget Alerts", 100, "No subscriptions");

            var subsWithBudgets = 0;
            foreach (var sub in subs.EnumerateArray())
            {
                var subId = sub.GetProperty("subscriptionId").GetString();
                var budgetJson = await GetArmJson(token, $"https://management.azure.com/subscriptions/{subId}/providers/Microsoft.Consumption/budgets?api-version=2023-11-01");
                if (budgetJson is not null && budgetJson.Value.TryGetProperty("value", out var budgets) && budgets.GetArrayLength() > 0)
                    subsWithBudgets++;
            }

            var pct = (int)(subsWithBudgets * 100 / totalSubs);
            return new CategoryScore("budgets", "Budget Alerts", pct, $"{subsWithBudgets}/{totalSubs} subscriptions have budgets ({pct}%)");
        }
        catch (Exception ex) { logger.LogWarning(ex, "Budget check parse error"); }
        return new CategoryScore("budgets", "Budget Alerts", -1, "Unable to check budgets");
    }

    // ── WALK CHECKS ──

    private static async Task<CategoryScore> CheckReservationCoverage(string token, ILogger logger)
    {
        // Check if any reservations exist
        var url = "https://management.azure.com/providers/Microsoft.Capacity/reservationOrders?api-version=2022-11-01";
        var json = await GetArmJson(token, url);
        if (json is null)
            return new CategoryScore("reservations", "Reservations & Savings Plans", -1, "Could not query reservations");

        try
        {
            var count = 0;
            if (json.Value.TryGetProperty("value", out var arr))
                count = arr.GetArrayLength();
            // Having reservations = good. Score based on count relative to a baseline.
            var score = count switch
            {
                0 => 10, // No reservations at all — low score but not 0 (they may have savings plans)
                <= 2 => 40,
                <= 5 => 60,
                <= 10 => 80,
                _ => 95
            };
            return new CategoryScore("reservations", "Reservations & Savings Plans", score, count == 0 ? "No active reservations found" : $"{count} active reservation order(s)");
        }
        catch (Exception ex) { logger.LogWarning(ex, "Reservation check parse error"); }
        return new CategoryScore("reservations", "Reservations & Savings Plans", -1, "Unable to check reservations");
    }

    private static async Task<CategoryScore> CheckAutoShutdown(string token, ILogger logger)
    {
        // Resource Graph: count VMs with auto-shutdown vs total VMs
        var query = new
        {
            query = @"Resources
| where type =~ 'microsoft.compute/virtualmachines'
| extend vmId = tolower(id)
| join kind=leftouter (
    Resources
    | where type =~ 'microsoft.devtestlab/schedules'
    | where name startswith 'shutdown-computevm-'
    | extend targetVmId = tolower(properties.targetResourceId)
    | project targetVmId
) on $left.vmId == $right.targetVmId
| summarize total=count(), withShutdown=countif(isnotempty(targetVmId))"
        };
        var json = await PostResourceGraph(token, query);
        if (json is null)
            return new CategoryScore("autoshutdown", "Non-Prod Snoozing", -1, "Could not query VM schedules");

        try
        {
            var rows = json.Value.GetProperty("data").GetProperty("rows");
            if (rows.GetArrayLength() > 0)
            {
                var row = rows[0];
                var total = row[0].GetInt64();
                var withShutdown = row[1].GetInt64();
                if (total == 0) return new CategoryScore("autoshutdown", "Non-Prod Snoozing", 100, "No VMs found");
                var pct = (int)(withShutdown * 100 / total);
                return new CategoryScore("autoshutdown", "Non-Prod Snoozing", pct, $"{withShutdown}/{total} VMs have auto-shutdown ({pct}%)");
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Auto-shutdown check parse error"); }
        return new CategoryScore("autoshutdown", "Non-Prod Snoozing", -1, "Unable to determine snoozing status");
    }

    private static async Task<CategoryScore> CheckAdvisorRightSizing(string token, ILogger logger)
    {
        // Count right-sizing recommendations specifically
        var url = "https://management.azure.com/providers/Microsoft.Advisor/recommendations?api-version=2023-01-01&$filter=Category eq 'Cost'&$top=100";
        var json = await GetArmJson(token, url);
        if (json is null)
            return new CategoryScore("rightsizing", "Right-sizing", -1, "Could not query Advisor");

        try
        {
            var rightSizeCount = 0;
            if (json.Value.TryGetProperty("value", out var arr))
            {
                foreach (var rec in arr.EnumerateArray())
                {
                    if (rec.TryGetProperty("properties", out var props) &&
                        props.TryGetProperty("shortDescription", out var desc))
                    {
                        var text = desc.TryGetProperty("solution", out var sol) ? sol.GetString() ?? "" : "";
                        if (text.Contains("right-size", StringComparison.OrdinalIgnoreCase) ||
                            text.Contains("resize", StringComparison.OrdinalIgnoreCase) ||
                            text.Contains("underutilized", StringComparison.OrdinalIgnoreCase))
                            rightSizeCount++;
                    }
                }
            }
            var score = rightSizeCount switch
            {
                0 => 100,
                <= 3 => 70,
                <= 10 => 40,
                _ => 10
            };
            return new CategoryScore("rightsizing", "Right-sizing", score, rightSizeCount == 0 ? "No right-sizing recommendations" : $"{rightSizeCount} right-sizing recommendation(s)");
        }
        catch (Exception ex) { logger.LogWarning(ex, "Right-sizing check parse error"); }
        return new CategoryScore("rightsizing", "Right-sizing", -1, "Unable to check right-sizing");
    }

    private static async Task<CategoryScore> CheckTaggingPolicy(string token, ILogger logger)
    {
        // Check if any tagging-related policies are assigned
        var query = new
        {
            query = "PolicyResources | where type =~ 'microsoft.authorization/policyassignments' | where properties.displayName contains 'tag' or properties.policyDefinitionId contains 'tag' | summarize count()"
        };
        var json = await PostResourceGraph(token, query);
        if (json is null)
            return new CategoryScore("taggingpolicy", "Tag Policy Enforcement", -1, "Could not query policies");

        try
        {
            var rows = json.Value.GetProperty("data").GetProperty("rows");
            if (rows.GetArrayLength() > 0)
            {
                var count = rows[0][0].GetInt64();
                var score = count switch
                {
                    0 => 0,
                    <= 2 => 50,
                    <= 5 => 75,
                    _ => 95
                };
                return new CategoryScore("taggingpolicy", "Tag Policy Enforcement", score, count == 0 ? "No tagging policies found" : $"{count} tagging policy assignment(s)");
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "Tagging policy check parse error"); }
        return new CategoryScore("taggingpolicy", "Tag Policy Enforcement", -1, "Unable to check tagging policies");
    }

    // ── RUN CHECKS ──

    private static async Task<CategoryScore> CheckCostExports(string token, ILogger logger)
    {
        // Check if cost exports are configured on any subscription
        var subsJson = await GetArmJson(token, "https://management.azure.com/subscriptions?api-version=2022-12-01");
        if (subsJson is null)
            return new CategoryScore("exports", "Cost Exports", -1, "Could not list subscriptions");

        try
        {
            var subs = subsJson.Value.GetProperty("value");
            var totalSubs = subs.GetArrayLength();
            if (totalSubs == 0) return new CategoryScore("exports", "Cost Exports", 100, "No subscriptions");

            var subsWithExports = 0;
            // Check first 5 subscriptions to keep it fast
            var checkedCount = 0;
            foreach (var sub in subs.EnumerateArray())
            {
                if (checkedCount++ >= 5) break;
                var subId = sub.GetProperty("subscriptionId").GetString();
                var exportJson = await GetArmJson(token, $"https://management.azure.com/subscriptions/{subId}/providers/Microsoft.CostManagement/exports?api-version=2023-11-01");
                if (exportJson is not null && exportJson.Value.TryGetProperty("value", out var exports) && exports.GetArrayLength() > 0)
                    subsWithExports++;
            }

            var pct = (int)(subsWithExports * 100 / Math.Min(totalSubs, 5));
            return new CategoryScore("exports", "Cost Exports", pct, $"{subsWithExports}/{Math.Min(totalSubs, 5)} checked subscriptions have exports ({pct}%)");
        }
        catch (Exception ex) { logger.LogWarning(ex, "Cost exports check parse error"); }
        return new CategoryScore("exports", "Cost Exports", -1, "Unable to check cost exports");
    }

    private static async Task<CategoryScore> CheckManagementGroups(string token, ILogger logger)
    {
        var json = await GetArmJson(token, "https://management.azure.com/providers/Microsoft.Management/managementGroups?api-version=2021-04-01");
        if (json is null)
            return new CategoryScore("mgmtgroups", "Management Group Structure", -1, "Could not query management groups");

        try
        {
            var count = 0;
            if (json.Value.TryGetProperty("value", out var arr))
                count = arr.GetArrayLength();
            // Having a hierarchy beyond the root tenant group = maturity
            var score = count switch
            {
                <= 1 => 20, // Only root group
                <= 3 => 50,
                <= 6 => 75,
                _ => 95
            };
            return new CategoryScore("mgmtgroups", "Management Group Structure", score, $"{count} management group(s)");
        }
        catch (Exception ex) { logger.LogWarning(ex, "Management groups check parse error"); }
        return new CategoryScore("mgmtgroups", "Management Group Structure", -1, "Unable to check management groups");
    }

    private static async Task<CategoryScore> CheckLicenseUsage(string graphToken, ILogger logger)
    {
        // Graph: check subscribedSkus for unused licenses
        var url = "https://graph.microsoft.com/v1.0/subscribedSkus";
        var json = await GetJson(graphToken, url);
        if (json is null)
            return new CategoryScore("licenses", "License Optimization", -1, "Could not query licenses");

        try
        {
            if (json.Value.TryGetProperty("value", out var skus))
            {
                long totalPaid = 0, totalConsumed = 0;
                foreach (var sku in skus.EnumerateArray())
                {
                    if (sku.TryGetProperty("prepaidUnits", out var prep) && prep.TryGetProperty("enabled", out var en))
                        totalPaid += en.GetInt64();
                    if (sku.TryGetProperty("consumedUnits", out var cu))
                        totalConsumed += cu.GetInt64();
                }
                if (totalPaid == 0) return new CategoryScore("licenses", "License Optimization", 100, "No paid licenses");
                var utilPct = (int)(totalConsumed * 100 / totalPaid);
                return new CategoryScore("licenses", "License Optimization", utilPct, $"{totalConsumed}/{totalPaid} licenses assigned ({utilPct}%)");
            }
        }
        catch (Exception ex) { logger.LogWarning(ex, "License usage check parse error"); }
        return new CategoryScore("licenses", "License Optimization", -1, "Unable to check license usage");
    }

    // ── Helpers ──

    private static async Task<JsonElement?> PostResourceGraph(string token, object query)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://management.azure.com/providers/Microsoft.ResourceGraph/resources?api-version=2022-10-01");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("User-Agent", "FinOps-Dashboard/1.0");
            req.Content = new StringContent(JsonSerializer.Serialize(query), Encoding.UTF8, "application/json");
            var res = await Http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<JsonElement>(body);
        }
        catch { return null; }
    }

    private static async Task<JsonElement?> GetArmJson(string token, string url)
    {
        return await GetJson(token, url);
    }

    private static async Task<JsonElement?> GetJson(string token, string url)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("User-Agent", "FinOps-Dashboard/1.0");
            var res = await Http.SendAsync(req);
            var body = await res.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<JsonElement>(body);
        }
        catch { return null; }
    }
}
