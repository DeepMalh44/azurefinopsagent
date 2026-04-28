using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
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
        var pythonScript = @"
import json, os, sys, io

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
    print(f'Error: python-pptx not installed ({e}). sys.path={sys.path}')
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

# Official Azure brand colors for data visualization
CHART_COLORS = ['#0078D4', '#50E6FF', '#008575', '#D83B01', '#8661C5',
                '#0063B1', '#00B7C3', '#E3008C', '#FFB900', '#107C10',
                '#B4009E', '#002050', '#4F6BED', '#C239B3', '#767676']

slides_data = json.loads(sys.stdin.read())
output_path = os.environ['PPTX_OUTPUT_PATH']
customer_name = os.environ.get('PPTX_CUSTOMER', '').strip()
total_slides = len(slides_data)

prs = Presentation()
prs.slide_width = Inches(13.333)
prs.slide_height = Inches(7.5)
SLIDE_W_IN = 13.333
SLIDE_H_IN = 7.5

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

def add_brand_footer(slide, slide_num, total, customer):
    # Thin Azure-blue rule
    rule = slide.shapes.add_shape(1, Inches(0.5), Inches(SLIDE_H_IN - 0.45), Inches(SLIDE_W_IN - 1.0), Inches(0.012))
    rule.fill.solid(); rule.fill.fore_color.rgb = RGBColor(0xE1, 0xE1, 0xE1)
    rule.line.fill.background()
    # Azure brand mark: small filled square in Azure blue (recognizable accent without using the trademarked logo)
    mark = slide.shapes.add_shape(1, Inches(0.5), Inches(SLIDE_H_IN - 0.32), Inches(0.14), Inches(0.14))
    mark.fill.solid(); mark.fill.fore_color.rgb = AZURE_BLUE
    mark.line.fill.background()
    # Wordmark + customer
    left_label = 'Microsoft Azure FinOps'
    if customer:
        left_label = f'{customer}  \u2022  Microsoft Azure FinOps'
    tb = slide.shapes.add_textbox(Inches(0.72), Inches(SLIDE_H_IN - 0.36), Inches(8.5), Inches(0.25))
    p = tb.text_frame.paragraphs[0]
    p.text = left_label
    p.font.size = Pt(9); p.font.name = 'Segoe UI'; p.font.color.rgb = TEXT_MED
    # Slide number (right)
    tb2 = slide.shapes.add_textbox(Inches(SLIDE_W_IN - 1.5), Inches(SLIDE_H_IN - 0.36), Inches(1.0), Inches(0.25))
    p2 = tb2.text_frame.paragraphs[0]
    p2.text = f'{slide_num} / {total}'
    p2.font.size = Pt(9); p2.font.name = 'Segoe UI'; p2.font.color.rgb = TEXT_MED
    p2.alignment = PP_ALIGN.RIGHT

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
    if not labels or not values:
        return False
    title = chart_cfg.get('title', '')
    colors = chart_cfg.get('colors', CHART_COLORS[:len(labels)])
    if len(colors) < len(labels):
        colors = colors + CHART_COLORS[len(colors):len(labels)]

    if ctype == 'pie':
        fig, ax = plt.subplots(figsize=(10, 5))
    elif ctype == 'horizontal_bar':
        fig_h = max(4, len(labels) * 0.55)
        fig, ax = plt.subplots(figsize=(8, fig_h))
    elif len(labels) > 15:
        fig, ax = plt.subplots(figsize=(10, 4.5))
    else:
        fig, ax = plt.subplots(figsize=(8, 4.5))
    fig.patch.set_facecolor('#FAFAFA')
    ax.set_facecolor('#FAFAFA')

    if ctype == 'pie':
        ax.set_aspect('equal')
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
            main_colors.append('#D0D0D0')

        # Donut-style pie with external legend for clarity
        wedges, autotexts = ax.pie(main_values, colors=main_colors,
                                    autopct='', startangle=90,
                                    pctdistance=0.8, wedgeprops={'width': 0.55, 'edgecolor': 'white', 'linewidth': 2})[0:2]
        # Add center circle for donut effect
        centre = plt.Circle((0, 0), 0.45, fc='#FAFAFA')
        ax.add_artist(centre)
        # Add total in center
        ax.text(0, 0, f'${total:,.0f}', ha='center', va='center', fontsize=16, fontweight='bold', color='#323130')
        ax.text(0, -0.15, 'Total (USD)', ha='center', va='center', fontsize=9, color='#605E5C')
        # Build legend labels with value and percentage
        legend_labels = [f'{lbl}  ${val:,.0f} ({val/total*100:.1f}%)' for lbl, val in zip(main_labels, main_values)]
        ax.legend(wedges, legend_labels, loc='center left', bbox_to_anchor=(1.05, 0.5),
                  fontsize=9, frameon=False, labelspacing=1.0)
    elif ctype == 'horizontal_bar':
        y_pos = range(len(labels))
        bars = ax.barh(y_pos, values, color=colors[:len(labels)], height=0.6, edgecolor='white', linewidth=0.5)
        ax.set_yticks(y_pos)
        ax.set_yticklabels(labels, fontsize=10, color='#323130')
        ax.invert_yaxis()
        for bar, val in zip(bars, values):
            ax.text(bar.get_width() + max(values)*0.02, bar.get_y() + bar.get_height()/2,
                    f'${val:,.0f}', va='center', fontsize=9, color='#605E5C')
    elif ctype == 'line':
        line_color = colors[0] if colors else '#0078D4'
        x_pos = range(len(labels))
        ax.plot(x_pos, values, color=line_color,
                marker='o', linewidth=2.5, markersize=5, markerfacecolor='white', markeredgewidth=2, markeredgecolor=line_color)
        ax.fill_between(x_pos, values, alpha=0.12, color=line_color)
        ax.set_xticks(x_pos)
        ax.set_xticklabels(labels, rotation=45, ha='right', fontsize=9, color='#605E5C')
        ax.set_xlim(-0.5, len(labels) - 0.5)
    else:  # bar
        bars = ax.bar(labels, values, color=colors[:len(labels)], width=0.65, edgecolor='white', linewidth=0.5)
        plt.xticks(rotation=45, ha='right', fontsize=9, color='#605E5C')
        for bar, val in zip(bars, values):
            ax.text(bar.get_x() + bar.get_width()/2, bar.get_height() + max(values)*0.01,
                    f'${val:,.0f}', ha='center', va='bottom', fontsize=9, color='#605E5C')

    if title:
        ax.set_title(title, fontsize=14, fontweight='600', pad=15, color='#323130')
    ax.spines['top'].set_visible(False)
    ax.spines['right'].set_visible(False)
    ax.spines['left'].set_color('#E8E8E8')
    ax.spines['bottom'].set_color('#E8E8E8')
    ax.tick_params(colors='#605E5C', labelsize=9)
    if ctype == 'horizontal_bar':
        ax.xaxis.set_major_formatter(ticker.FuncFormatter(lambda x, p: f'${x:,.0f}'))
    elif ctype != 'pie':
        ax.yaxis.set_major_formatter(ticker.FuncFormatter(lambda x, p: f'${x:,.0f}'))
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
        # Azure brand mark (small square) + wordmark
        mark = slide.shapes.add_shape(1, Inches(1), Inches(5.55), Inches(0.18), Inches(0.18))
        mark.fill.solid(); mark.fill.fore_color.rgb = ACCENT_TEAL
        mark.line.fill.background()
        brand_label = 'Microsoft Azure FinOps'
        if customer_name:
            brand_label = f'{customer_name}  \u2022  Microsoft Azure FinOps'
        add_textbox(slide, 1.3, 5.5, 11, 0.4, brand_label, font_size=12, bold=True, color=RGBColor(0xBE, 0xD7, 0xEF))
        add_textbox(slide, 1, 6.0, 11, 0.4, 'Generated by Azure FinOps Agent', font_size=10, color=RGBColor(0x80, 0x9D, 0xB8))

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
            chart_path = output_path.replace('.pptx', f'_chart_{idx}.png')
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
            chart_path = output_path.replace('.pptx', f'_chart_{idx}.png')
            if render_chart_image(chart_cfg, chart_path):
                chart_top = 1.3 + len(bullets) * 0.35 if bullets else 1.3
                slide.shapes.add_picture(chart_path, Inches(1.5), Inches(min(chart_top, 3.0)), Inches(8), Inches(4.0))
                os.remove(chart_path)

    if notes:
        slide.notes_slide.notes_text_frame.text = notes

    # Azure brand footer on every slide except the title slide
    if layout != 'title':
        add_brand_footer(slide, idx + 1, total_slides, customer_name)

prs.save(output_path)
print(f'OK:{output_path}')
print(f'SLIDES:{len(slides_data)}')
";

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
}
