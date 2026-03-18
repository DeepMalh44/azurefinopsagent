using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AzureFinOps.Dashboard.Tools;

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

    // Cleanup files older than 30 minutes
    internal static void CleanupOldFiles()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-30);
        foreach (var kvp in GeneratedFiles)
        {
            if (kvp.Value.Created < cutoff)
            {
                GeneratedFiles.TryRemove(kvp.Key, out _);
                try { File.Delete(kvp.Value.Path); } catch { }
            }
        }
    }

    public static IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(GeneratePresentation, "GeneratePresentation",
            @"Generates a FinOps PowerPoint (.pptx) presentation from structured slide data.
Call this when the user wants to export findings, analysis, or recommendations as a downloadable PowerPoint.
The slides JSON should follow FinOps best practices structure:
1. Title slide (company/tenant name, date, 'Azure FinOps Review')
2. Executive Summary (key findings, total spend, top recommendation)
3. Cost Overview (monthly spend trend, breakdown by service)
4. Cost Breakdown (by resource group, subscription, or service — include chart data)
5. Optimization Recommendations (idle resources, rightsizing, reservations)
6. Savings Potential (estimated savings, ROI)
7. Next Steps / Action Items

Before calling this tool, ask the user if they want to customize the content or if the suggested structure is fine.
Each slide can have: title, subtitle, bullet points, and optional chart data (bar/line/pie with labels+values).
Charts are rendered as images in the presentation using matplotlib.");
    }

    private static async Task<string> GeneratePresentation(
        [Description(@"JSON array of slides. Each slide object has:
- layout: 'title'|'section'|'content'|'chart'|'two_column'
- title: slide title text
- subtitle: (optional) subtitle or date
- bullets: (optional) array of bullet point strings
- bullets_right: (optional) array for right column in two_column layout
- chart: (optional) object with {type:'bar'|'line'|'pie'|'horizontal_bar', title:string, labels:[...], values:[...], colors:[...optional]}
- notes: (optional) speaker notes
Example: [{""layout"":""title"",""title"":""Azure FinOps Review"",""subtitle"":""Contoso — March 2026""},{""layout"":""content"",""title"":""Executive Summary"",""bullets"":[""Total monthly spend: $45,230"",""Top service: Virtual Machines (38%)"",""Potential savings: $12,400/month""]},{""layout"":""chart"",""title"":""Cost by Service"",""chart"":{""type"":""pie"",""title"":""Monthly Spend by Service"",""labels"":[""VMs"",""Storage"",""SQL""],""values"":[17200,8500,6300]}}]")] string slidesJson,
        [Description("Filename for the presentation (without extension). Default: 'FinOps-Report'")] string? filename)
    {
        if (string.IsNullOrWhiteSpace(slidesJson))
            return "Error: No slides data provided.";

        CleanupOldFiles();

        var fileId = Guid.NewGuid().ToString("N")[..12];
        var safeName = string.IsNullOrWhiteSpace(filename) ? "FinOps-Report" : SanitizeFilename(filename);
        var outputPath = Path.Combine(Path.GetTempPath(), $"{fileId}_{safeName}.pptx");

        // Escape the JSON for embedding in the Python script
        var escapedJson = slidesJson.Replace("\\", "\\\\").Replace("'", "\\'");

        var pythonScript = $@"
import json, os, sys, io

# Ensure pip packages from startup.sh are discoverable (Azure App Service)
_pip = '/home/site/pip-packages'
if os.path.isdir(_pip) and _pip not in sys.path:
    sys.path.insert(0, _pip)

try:
    from pptx import Presentation
    from pptx.util import Inches, Pt, Emu
    from pptx.dml.color import RGBColor
    from pptx.enum.text import PP_ALIGN, MSO_ANCHOR
    from pptx.enum.chart import XL_CHART_TYPE, XL_LEGEND_POSITION
    from pptx.chart.data import CategoryChartData
except ImportError as e:
    print(f'Error: python-pptx not installed ({{e}}). sys.path={{sys.path}}')
    sys.exit(1)

try:
    import matplotlib
    matplotlib.use('Agg')
    import matplotlib.pyplot as plt
    import matplotlib.ticker as ticker
    HAS_MPL = True
except ImportError:
    HAS_MPL = False

# ── Color palette (Azure Fluent — modern, light) ──
AZURE_BLUE = RGBColor(0x00, 0x78, 0xD4)
AZURE_LIGHT = RGBColor(0xDE, 0xEC, 0xF9)  # soft Azure background
TITLE_BG = RGBColor(0x00, 0x3B, 0x73)      # deep Azure navy for title slide
WHITE = RGBColor(0xFF, 0xFF, 0xFF)
SLIDE_BG = RGBColor(0xFA, 0xFA, 0xFA)      # near-white warm background
ACCENT_TEAL = RGBColor(0x00, 0xB7, 0xC3)
ACCENT_GREEN = RGBColor(0x2D, 0x7D, 0x46)
TEXT_DARK = RGBColor(0x1A, 0x1A, 0x2E)
TEXT_MED = RGBColor(0x60, 0x5E, 0x5C)
TEXT_LIGHT = RGBColor(0x8A, 0x8A, 0x8A)

# Modern Azure-inspired chart palette — light, distinct, accessible
CHART_COLORS = ['#0078D4', '#50E6FF', '#00B7C3', '#2D7D46', '#FFB900',
                '#D83B01', '#8764B8', '#E3008C', '#4F6BED', '#009E49']

slides_data = json.loads('{escapedJson}')
output_path = r'{outputPath.Replace("\\", "\\\\")}'

prs = Presentation()
prs.slide_width = Inches(13.333)
prs.slide_height = Inches(7.5)

def add_bg(slide, color=WHITE):
    bg = slide.background
    fill = bg.fill
    fill.solid()
    fill.fore_color.rgb = color

def add_textbox(slide, left, top, width, height, text, font_size=14,
                bold=False, color=TEXT_DARK, alignment=PP_ALIGN.LEFT, font_name='Segoe UI'):
    txBox = slide.shapes.add_textbox(Inches(left), Inches(top), Inches(width), Inches(height))
    tf = txBox.text_frame
    tf.word_wrap = True
    p = tf.paragraphs[0]
    p.text = text
    p.font.size = Pt(font_size)
    p.font.bold = bold
    p.font.color.rgb = color
    p.font.name = font_name
    p.alignment = alignment
    return txBox

def add_bullets(slide, left, top, width, height, items, font_size=14, color=TEXT_DARK):
    txBox = slide.shapes.add_textbox(Inches(left), Inches(top), Inches(width), Inches(height))
    tf = txBox.text_frame
    tf.word_wrap = True
    for i, item in enumerate(items):
        p = tf.paragraphs[0] if i == 0 else tf.add_paragraph()
        p.text = item
        p.font.size = Pt(font_size)
        p.font.color.rgb = color
        p.font.name = 'Segoe UI'
        p.space_after = Pt(6)
        p.level = 0
    return txBox

def render_chart_image(chart_cfg, path):
    if not HAS_MPL:
        return False
    ctype = chart_cfg.get('type', 'bar')
    labels = chart_cfg.get('labels', [])
    values = chart_cfg.get('values', [])
    title = chart_cfg.get('title', '')
    colors = chart_cfg.get('colors', CHART_COLORS[:len(labels)])
    if len(colors) < len(labels):
        colors = colors + CHART_COLORS[len(colors):len(labels)]

    fig, ax = plt.subplots(figsize=(8, 4.5))
    fig.patch.set_facecolor('#FAFAFA')
    ax.set_facecolor('#FAFAFA')

    if ctype == 'pie':
        # Group small slices (< 3%) into 'Other' to avoid label overlap
        total = sum(values)
        threshold = 0.03 * total if total > 0 else 0
        main_labels, main_values, main_colors = [], [], []
        other_val = 0
        for lbl, val, clr in zip(labels, values, colors):
            if val >= threshold:
                main_labels.append(lbl)
                main_values.append(val)
                main_colors.append(clr)
            else:
                other_val += val
        if other_val > 0:
            main_labels.append('Other')
            main_values.append(other_val)
            main_colors.append('#AAAAAA')

        def make_autopct(vals):
            def autopct(pct):
                return f'${{sum(vals)*pct/100:,.0f}}\n({{pct:.1f}}%)'
            return autopct

        wedges, texts, autotexts = ax.pie(main_values, labels=main_labels, colors=main_colors,
                                           autopct=make_autopct(main_values), startangle=90,
                                           pctdistance=0.75, labeldistance=1.12,
                                           textprops={{'fontsize': 9}})
        for t in autotexts:
            t.set_fontsize(8)
        for t in texts:
            t.set_fontsize(9)
    elif ctype == 'horizontal_bar':
        y_pos = range(len(labels))
        bars = ax.barh(y_pos, values, color=colors[:len(labels)])
        ax.set_yticks(y_pos)
        ax.set_yticklabels(labels, fontsize=10)
        ax.invert_yaxis()
        for bar, val in zip(bars, values):
            ax.text(bar.get_width() + max(values)*0.01, bar.get_y() + bar.get_height()/2,
                    f'${{val:,.0f}}', va='center', fontsize=9)
    elif ctype == 'line':
        ax.plot(labels, values, color=colors[0] if colors else '#0078D4',
                marker='o', linewidth=2, markersize=6)
        ax.fill_between(range(len(labels)), values, alpha=0.1, color=colors[0] if colors else '#0078D4')
        plt.xticks(rotation=45, ha='right', fontsize=9)
    else:  # bar
        bars = ax.bar(labels, values, color=colors[:len(labels)])
        plt.xticks(rotation=45, ha='right', fontsize=9)
        for bar, val in zip(bars, values):
            ax.text(bar.get_x() + bar.get_width()/2, bar.get_height() + max(values)*0.01,
                    f'${{val:,.0f}}', ha='center', va='bottom', fontsize=9)

    if title:
        ax.set_title(title, fontsize=13, fontweight='bold', pad=12)
    ax.spines['top'].set_visible(False)
    ax.spines['right'].set_visible(False)
    ax.spines['left'].set_color('#E0E0E0')
    ax.spines['bottom'].set_color('#E0E0E0')
    ax.tick_params(colors='#605E5C')
    if ctype != 'pie':
        ax.yaxis.set_major_formatter(ticker.FuncFormatter(lambda x, p: f'${{x:,.0f}}'))
    plt.tight_layout()
    fig.savefig(path, dpi=150, bbox_inches='tight', facecolor='#FAFAFA')
    plt.close(fig)
    return True

for idx, slide_def in enumerate(slides_data):
    layout = slide_def.get('layout', 'content')
    title = slide_def.get('title', '')
    subtitle = slide_def.get('subtitle', '')
    bullets = slide_def.get('bullets', [])
    chart_cfg = slide_def.get('chart', None)
    notes = slide_def.get('notes', '')

    slide_layout = prs.slide_layouts[6]  # blank
    slide = prs.slides.add_slide(slide_layout)

    if layout == 'title':
        add_bg(slide, TITLE_BG)
        # Gradient-style accent bar at top
        shape = slide.shapes.add_shape(1, Inches(0), Inches(0), prs.slide_width, Inches(0.1))
        shape.fill.solid()
        shape.fill.fore_color.rgb = ACCENT_TEAL
        shape.line.fill.background()
        add_textbox(slide, 1, 2.0, 11, 1.2, title, font_size=42, bold=True, color=WHITE)
        if subtitle:
            add_textbox(slide, 1, 3.5, 11, 0.8, subtitle, font_size=20, color=RGBColor(0xBE, 0xD7, 0xEF))
        # Azure FinOps Agent branding
        add_textbox(slide, 1, 5.5, 11, 0.5, 'Generated by Azure FinOps Agent', font_size=11, color=RGBColor(0x80, 0x9D, 0xB8))

    elif layout == 'section':
        add_bg(slide, AZURE_LIGHT)
        # Accent bar
        shape = slide.shapes.add_shape(1, Inches(0), Inches(0), Inches(0.12), prs.slide_height)
        shape.fill.solid()
        shape.fill.fore_color.rgb = AZURE_BLUE
        shape.line.fill.background()
        add_textbox(slide, 1, 2.5, 11, 1.5, title, font_size=36, bold=True, color=TITLE_BG)
        if subtitle:
            add_textbox(slide, 1, 4.2, 11, 0.8, subtitle, font_size=18, color=RGBColor(0x00, 0x5A, 0x9E))

    elif layout == 'chart':
        add_bg(slide, SLIDE_BG)
        # Blue accent bar
        shape = slide.shapes.add_shape(1, Inches(0), Inches(0), prs.slide_width, Inches(0.06))
        shape.fill.solid()
        shape.fill.fore_color.rgb = AZURE_BLUE
        shape.line.fill.background()
        add_textbox(slide, 0.8, 0.3, 11, 0.7, title, font_size=24, bold=True, color=TEXT_DARK)

        if chart_cfg and HAS_MPL:
            chart_path = output_path.replace('.pptx', f'_chart_{{idx}}.png')
            if render_chart_image(chart_cfg, chart_path):
                slide.shapes.add_picture(chart_path, Inches(1.5), Inches(1.3), Inches(8), Inches(4.5))
                os.remove(chart_path)
        if bullets:
            bullet_top = 1.3 if not chart_cfg else 6.0
            add_bullets(slide, 0.8, bullet_top, 11, 1.2, bullets, font_size=12, color=TEXT_MED)

    elif layout == 'two_column':
        add_bg(slide, SLIDE_BG)
        shape = slide.shapes.add_shape(1, Inches(0), Inches(0), prs.slide_width, Inches(0.06))
        shape.fill.solid()
        shape.fill.fore_color.rgb = AZURE_BLUE
        shape.line.fill.background()
        add_textbox(slide, 0.8, 0.3, 11, 0.7, title, font_size=24, bold=True, color=TEXT_DARK)
        if bullets:
            add_bullets(slide, 0.8, 1.3, 5.2, 5.5, bullets, font_size=13, color=TEXT_DARK)
        right_bullets = slide_def.get('bullets_right', [])
        if right_bullets:
            add_bullets(slide, 6.8, 1.3, 5.2, 5.5, right_bullets, font_size=13, color=TEXT_DARK)

    else:  # content
        add_bg(slide, SLIDE_BG)
        shape = slide.shapes.add_shape(1, Inches(0), Inches(0), prs.slide_width, Inches(0.06))
        shape.fill.solid()
        shape.fill.fore_color.rgb = AZURE_BLUE
        shape.line.fill.background()
        add_textbox(slide, 0.8, 0.3, 11, 0.7, title, font_size=24, bold=True, color=TEXT_DARK)
        if subtitle:
            add_textbox(slide, 0.8, 1.0, 11, 0.5, subtitle, font_size=14, color=TEXT_MED)
        if bullets:
            bullet_top = 1.6 if subtitle else 1.3
            add_bullets(slide, 0.8, bullet_top, 11, 5.0, bullets, font_size=15, color=TEXT_DARK)

        if chart_cfg and HAS_MPL:
            chart_path = output_path.replace('.pptx', f'_chart_{{idx}}.png')
            if render_chart_image(chart_cfg, chart_path):
                chart_top = 1.3 + len(bullets) * 0.35 if bullets else 1.3
                slide.shapes.add_picture(chart_path, Inches(1.5), Inches(min(chart_top, 3.0)), Inches(8), Inches(4.0))
                os.remove(chart_path)

    if notes:
        slide.notes_slide.notes_text_frame.text = notes

prs.save(output_path)
print(f'OK:{{output_path}}')
print(f'SLIDES:{{len(slides_data)}}')
";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "python3",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(pythonScript);

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
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private static string SanitizeFilename(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "FinOps-Report" : sanitized;
    }

    private static string Truncate(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..maxLen] + "... (truncated)";
}
