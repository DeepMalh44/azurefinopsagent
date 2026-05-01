using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;

using AzureFinOps.Dashboard.Infrastructure;

namespace AzureFinOps.Dashboard.AI.Tools;

/// <summary>
/// Generates FinOps PowerPoint presentations using python-pptx.
/// The LLM provides structured slide data as JSON, and a Python script
/// renders the .pptx file with charts (via matplotlib) and formatted text.
/// </summary>
public static class PresentationTools
{
    private const int TimeoutSeconds = 60;
    private const int MaxOutputChars = 10_000;

    // Store generated files for download: fileId → absolute path
    internal static readonly ConcurrentDictionary<string, (string Path, DateTime Created)> GeneratedFiles = new();

    internal static void CleanupOldFiles() =>
        TempFileHelper.CleanupOldFiles(GeneratedFiles, v => v.Created, v => v.Path);

    public static IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(GeneratePresentation, "GeneratePresentation",
            @"Generates a polished, executive-ready FinOps PowerPoint (.pptx) from structured slide data. Charts are rendered with matplotlib using an Azure brand palette and embedded as images. Output is 16:9 widescreen.

═══ WORKFLOW (do this BEFORE calling) ═══
1. You MUST have already gathered the data via QueryAzure / FindIdleResources / DetectCostAnomalies / GetAzureRetailPricing. Never invent numbers.
2. Show the user a 1-line outline of the proposed deck (e.g. 'Title → Exec Summary → Cost Trend → Top Services → Idle Resources → Recommendations → Next Steps') and confirm before generating.
3. Keep it SHORT. 7–12 slides is ideal. Executives don't read 30-slide decks. Cut ruthlessly.

═══ DESIGN RULES (the LLM is responsible for these — the renderer cannot fix bad input) ═══

CONTENT DENSITY
- Max 5 bullets per slide. If you have more, split across 2 slides or use a chart.
- Max ~10 words per bullet. No paragraphs. No nested sub-bullets.
- Never duplicate the slide title inside the body.
- Numbers always include unit + currency: '$12,400/mo', not '12400'.
- Lead with the number, then the explanation: '$12.4K/mo wasted — 47 idle VMs identified', not 'We identified 47 idle VMs which cost…'

NARRATIVE FLOW (executive-grade decks tell a story)
- Slide 1: title (company, period, who prepared it)
- Slide 2: executive summary — 3 numbers (current spend, identified savings, % reduction) and 1 sentence recommendation
- Middle slides: ONE idea per slide. Slide title should be the conclusion, not the topic. ❌ 'VM Costs'  ✅ 'Virtual Machines drive 38% of spend — D-series dominates'
- Final slide: next steps with owners and timelines, not vague platitudes

CHARTS
- Use a chart whenever you have ≥3 numeric data points. A chart beats a bullet list every time.
- Pick the right chart type:
    * pie/donut → composition (max 6 slices; tool auto-groups <3% into 'Other')
    * horizontal_bar → ranking (top N by cost). USE THIS for 'top 10 services/RGs/SKUs' instead of vertical bar.
    * bar → comparison across small set of categories
    * line → time series (daily/monthly trend, 7+ points)
- Chart values must be NUMBERS not strings. Currency is added by the renderer.
- Every chart slide should include 1–2 bullets BELOW the chart with the takeaway (the 'so what'), not a description of the chart itself.

LAYOUT SELECTION
- 'title' → opening slide only
- 'section' → divider between major parts (use sparingly: 'Findings', 'Recommendations')
- 'content' → bullets only, or bullets + small chart
- 'chart' → chart-dominant slide with optional 1-line takeaway below
- 'two_column' → before/after, current/optimized, problem/solution comparisons (use bullets + bullets_right)

LANGUAGE
- Active voice. Past tense for findings ('We identified…'), future tense for recommendations ('Migrating to…will save…').
- No hedging ('might', 'could possibly') — the user is paying for confident analysis.
- Spell out acronyms on first use: 'Azure Hybrid Benefit (AHUB)'.

WHAT NOT TO DO
- ❌ Don't add 'Thank You' / 'Questions?' slides — wastes a slide.
- ❌ Don't put dates in the format '2026-04-28' on title slides — use 'April 2026'.
- ❌ Don't render a chart with <3 data points (use bullets instead).
- ❌ Don't use 'chart' layout for tiny charts — use 'content' layout with chart embedded.
- ❌ Don't create a slide for an idea you can't back with data from a tool call.

═══ RECOMMENDED 9-SLIDE FINOPS REVIEW STRUCTURE ═══
1. title              — '{Customer} Azure FinOps Review' / 'April 2026'
2. content            — Executive Summary: 3 KPI bullets + 1 recommendation bullet
3. chart (line)       — Last 90 days daily cost trend, takeaway bullet
4. chart (horizontal_bar) — Top 10 services by cost
5. chart (pie/donut)  — Cost by resource group or environment (prod/dev/test)
6. content + chart    — Idle/orphan resources found, $ wasted (use FindIdleResources data)
7. two_column         — Reservations & Savings Plans: 'Current PAYG cost' vs 'Optimized with 1y RI'
8. content            — Prioritized recommendations table (rank, action, $ impact, effort)
9. content            — Next steps with named owner + date for each action

If the user wants a shorter version, collapse to 5 slides: 1, 2, 4, 6, 9.
");
    }

    private static async Task<string> GeneratePresentation(
        [Description(@"JSON array of slides — see tool description above for design rules and structure.

SLIDE OBJECT SCHEMA:
- layout: 'title' | 'section' | 'content' | 'chart' | 'two_column'  (REQUIRED)
- title: slide title (REQUIRED). For 'content' slides this should be the CONCLUSION, not the topic.
- subtitle: optional — used on title/section slides as tagline, or on content slides as a one-line lead
- bullets: optional array of strings (≤5 items, ≤10 words each)
- bullets_right: optional — right column for 'two_column' layout
- chart: optional object {type, title, labels[], values[], colors[]?}
    * type: 'bar' | 'horizontal_bar' (preferred for top-N) | 'line' (for time series) | 'pie' (composition, ≤6 slices)
    * labels: array of strings (e.g. ['VMs','Storage','SQL'])
    * values: array of NUMBERS (NOT strings — e.g. [17200, 8500, 6300])
    * colors: optional array of hex strings; omit to use Azure brand palette automatically
- notes: optional speaker notes (won't appear on slide, but added to notes pane)

GOOD EXAMPLE (note conclusion-style titles, takeaway bullets after charts, $ formatting):
[
  {""layout"":""title"",""title"":""Contoso Azure FinOps Review"",""subtitle"":""April 2026 — Prepared by Cloud CoE""},
  {""layout"":""content"",""title"":""$12.4K/month savings identified — 27% reduction"",""bullets"":[""Current spend: $45,230/mo"",""Identified savings: $12,400/mo"",""Top lever: Reserved Instances ($7.2K/mo)"",""Recommendation: prioritize RI purchase this sprint""]},
  {""layout"":""chart"",""title"":""Daily spend up 18% over last 30 days — driven by AKS scale-out"",""chart"":{""type"":""line"",""title"":""Daily Cost (USD)"",""labels"":[""Mar 1"",""Mar 8"",""Mar 15"",""Mar 22"",""Mar 29"",""Apr 5"",""Apr 12"",""Apr 19"",""Apr 26""],""values"":[1320,1410,1380,1450,1520,1610,1680,1740,1810]},""bullets"":[""Increase coincides with new AKS node pool deployment Apr 1"",""Recommend enabling cluster autoscaler with sensible min/max""]},
  {""layout"":""chart"",""title"":""Virtual Machines and AKS account for 61% of spend"",""chart"":{""type"":""horizontal_bar"",""title"":""Top Services by Monthly Cost"",""labels"":[""Virtual Machines"",""AKS"",""Storage"",""SQL Database"",""App Service"",""Azure Monitor""],""values"":[17200,10500,5400,4200,2800,1600]}},
  {""layout"":""two_column"",""title"":""Reserved Instances cut VM cost by 38%"",""bullets"":[""CURRENT (PAYG)"",""$17,200/mo for 142 VMs"",""All on-demand"",""No commitment""],""bullets_right"":[""OPTIMIZED (1-yr RI)"",""$10,664/mo same workload"",""Save $6,536/mo = $78K/yr"",""Recoups in <1 month""]},
  {""layout"":""content"",""title"":""Next Steps"",""bullets"":[""Week 1 — Purchase 1y RIs for top 50 stable VMs (Owner: Cloud CoE)"",""Week 2 — Delete 47 unattached disks identified ($340/mo) (Owner: Platform team)"",""Week 3 — Right-size 12 oversized SQL DBs (Owner: Data team)"",""Week 4 — Review with Finance, set monthly cadence""]}
]")] string slidesJson,
        [Description("Filename for the presentation (without extension). Default: 'FinOps-Report'")] string? filename,
        [Description("Optional customer/tenant name shown in the footer of every slide (e.g. 'Contoso'). Empty = footer shows just 'Microsoft Azure FinOps'.")] string? customer = null)
    {
        if (string.IsNullOrWhiteSpace(slidesJson))
            return "Error: No slides data provided.";

        CleanupOldFiles();

        var fileId = Guid.NewGuid().ToString("N")[..12];
        var safeName = string.IsNullOrWhiteSpace(filename) ? "FinOps-Report" : SanitizeFilename(filename);
        var outputPath = Path.Combine(Path.GetTempPath(), $"{fileId}_{safeName}.pptx");

        // Validate JSON before launching Python (cheap fail-fast, also catches malformed input)
        try { using var _ = JsonDocument.Parse(slidesJson); }
        catch (JsonException jex) { return $"Error: Invalid slides JSON — {jex.Message}"; }

        // Slides JSON and output path are passed via stdin/env (NEVER interpolated
        // into the Python source) to eliminate any shell/code injection risk.
        var pythonScript = LoadEmbeddedScript("pptx_generator.py");

        var psi = new ProcessStartInfo
        {
            FileName = "python3",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(pythonScript);
        psi.Environment["PPTX_OUTPUT_PATH"] = outputPath;
        psi.Environment["PPTX_CUSTOMER"] = customer ?? "";

        // Ensure pip packages installed by startup.sh are discoverable (Azure: /home/site/pip-packages)
        // On local dev (Windows/macOS), packages are in the default site-packages — no override needed
        var pipTarget = "/home/site/pip-packages";
        if (Directory.Exists(pipTarget))
        {
            var existingPythonPath = Environment.GetEnvironmentVariable("PYTHONPATH") ?? "";
            psi.Environment["PYTHONPATH"] = string.IsNullOrEmpty(existingPythonPath)
                ? pipTarget
                : $"{pipTarget}:{existingPythonPath}";
        }

        using var process = new Process { StartInfo = psi };
        process.Start();

        // Pipe slides JSON via stdin — no shell escaping needed, no injection risk
        await process.StandardInput.WriteAsync(slidesJson);
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        var exited = process.WaitForExit(TimeoutSeconds * 1000);
        if (!exited)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return $"Error: Presentation generation timed out after {TimeoutSeconds} seconds.";
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        if (process.ExitCode != 0)
            return $"Error generating presentation (exit={process.ExitCode}): stdout=[{Truncate(stdout, 500)}] stderr=[{Truncate(stderr, MaxOutputChars)}]";

        if (!stdout.Contains("OK:"))
            return $"Error: No OK marker in output. stdout=[{Truncate(stdout, 500)}] stderr=[{Truncate(stderr, 500)}] exit={process.ExitCode}";

        // Register the file for download
        GeneratedFiles[fileId] = (outputPath, DateTime.UtcNow);

        var slideCount = "unknown";
        foreach (var line in stdout.Split('\n'))
        {
            if (line.StartsWith("SLIDES:"))
                slideCount = line["SLIDES:".Length..].Trim();
        }

        return $"__PPTX_READY__:{fileId}:{safeName}.pptx:{slideCount}";
    }

    private static string SanitizeFilename(string name) =>
        TempFileHelper.SanitizeFilename(name, "FinOps-Report");

    private static string Truncate(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..maxLen] + "... (truncated)";

    private static string LoadEmbeddedScript(string filename)
    {
        var asm = Assembly.GetExecutingAssembly();
        // Resource name format: <RootNamespace>.<Path>.<File> with directory separators replaced by dots.
        var resourceName = $"AzureFinOps.Dashboard.AI.Tools.Resources.{filename}";
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found. Available: {string.Join(", ", asm.GetManifestResourceNames())}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
