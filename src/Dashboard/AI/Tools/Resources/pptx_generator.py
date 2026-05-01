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

# -- Color palette (Azure Fluent — modern, light) --
AZURE_BLUE = RGBColor(0x00, 0x78, 0xD4)
AZURE_LIGHT = RGBColor(0xDE, 0xEC, 0xF9)
TITLE_BG = RGBColor(0x00, 0x3B, 0x73)
WHITE = RGBColor(0xFF, 0xFF, 0xFF)
SLIDE_BG = RGBColor(0xFA, 0xFA, 0xFA)
ACCENT_TEAL = RGBColor(0x00, 0xB7, 0xC3)
ACCENT_GREEN = RGBColor(0x2D, 0x7D, 0x46)
TEXT_DARK = RGBColor(0x1A, 0x1A, 0x2E)
TEXT_MED = RGBColor(0x60, 0x5E, 0x5C)
TEXT_LIGHT = RGBColor(0x8A, 0x8A, 0x8A)

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
    rule = slide.shapes.add_shape(1, Inches(0.5), Inches(SLIDE_H_IN - 0.45), Inches(SLIDE_W_IN - 1.0), Inches(0.012))
    rule.fill.solid(); rule.fill.fore_color.rgb = RGBColor(0xE1, 0xE1, 0xE1)
    rule.line.fill.background()
    mark = slide.shapes.add_shape(1, Inches(0.5), Inches(SLIDE_H_IN - 0.32), Inches(0.14), Inches(0.14))
    mark.fill.solid(); mark.fill.fore_color.rgb = AZURE_BLUE
    mark.line.fill.background()
    left_label = 'Microsoft Azure FinOps'
    if customer:
        left_label = f'{customer}  \u2022  Microsoft Azure FinOps'
    tb = slide.shapes.add_textbox(Inches(0.72), Inches(SLIDE_H_IN - 0.36), Inches(8.5), Inches(0.25))
    p = tb.text_frame.paragraphs[0]
    p.text = left_label
    p.font.size = Pt(9); p.font.name = 'Segoe UI'; p.font.color.rgb = TEXT_MED
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
        total = sum(values)
        threshold = 0.03 * total if total > 0 else 0
        main_labels, main_values, main_colors = [], [], []
        other_val = 0
        for lbl, val, clr in zip(labels, values, colors):
            if val >= threshold:
                main_labels.append(lbl); main_values.append(val); main_colors.append(clr)
            else:
                other_val += val
        if other_val > 0:
            main_labels.append('Other'); main_values.append(other_val); main_colors.append('#D0D0D0')

        wedges, autotexts = ax.pie(main_values, colors=main_colors,
                                    autopct='', startangle=90,
                                    pctdistance=0.8, wedgeprops={'width': 0.55, 'edgecolor': 'white', 'linewidth': 2})[0:2]
        centre = plt.Circle((0, 0), 0.45, fc='#FAFAFA')
        ax.add_artist(centre)
        ax.text(0, 0, f'${total:,.0f}', ha='center', va='center', fontsize=16, fontweight='bold', color='#323130')
        ax.text(0, -0.15, 'Total (USD)', ha='center', va='center', fontsize=9, color='#605E5C')
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
    else:
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

    slide_layout = prs.slide_layouts[6]
    slide = prs.slides.add_slide(slide_layout)

    if layout == 'title':
        add_bg(slide, TITLE_BG)
        shape = slide.shapes.add_shape(1, Inches(0), Inches(0), prs.slide_width, Inches(0.1))
        shape.fill.solid(); shape.fill.fore_color.rgb = ACCENT_TEAL
        shape.line.fill.background()
        add_textbox(slide, 1, 2.0, 11, 1.2, title, font_size=42, bold=True, color=WHITE)
        if subtitle:
            add_textbox(slide, 1, 3.5, 11, 0.8, subtitle, font_size=20, color=RGBColor(0xBE, 0xD7, 0xEF))
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
        shape = slide.shapes.add_shape(1, Inches(0), Inches(0), Inches(0.12), prs.slide_height)
        shape.fill.solid(); shape.fill.fore_color.rgb = AZURE_BLUE
        shape.line.fill.background()
        add_textbox(slide, 1, 2.5, 11, 1.5, title, font_size=36, bold=True, color=TITLE_BG)
        if subtitle:
            add_textbox(slide, 1, 4.2, 11, 0.8, subtitle, font_size=18, color=RGBColor(0x00, 0x5A, 0x9E))

    elif layout == 'chart':
        add_bg(slide, SLIDE_BG)
        shape = slide.shapes.add_shape(1, Inches(0), Inches(0), prs.slide_width, Inches(0.06))
        shape.fill.solid(); shape.fill.fore_color.rgb = AZURE_BLUE
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
        shape.fill.solid(); shape.fill.fore_color.rgb = AZURE_BLUE
        shape.line.fill.background()
        add_textbox(slide, 0.8, 0.3, 11, 0.7, title, font_size=24, bold=True, color=TEXT_DARK)
        if bullets:
            add_bullets(slide, 0.8, 1.3, 5.2, 5.5, bullets, font_size=13, color=TEXT_DARK)
        right_bullets = slide_def.get('bullets_right', [])
        if right_bullets:
            add_bullets(slide, 6.8, 1.3, 5.2, 5.5, right_bullets, font_size=13, color=TEXT_DARK)

    else:
        add_bg(slide, SLIDE_BG)
        shape = slide.shapes.add_shape(1, Inches(0), Inches(0), prs.slide_width, Inches(0.06))
        shape.fill.solid(); shape.fill.fore_color.rgb = AZURE_BLUE
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

    if layout != 'title':
        add_brand_footer(slide, idx + 1, total_slides, customer_name)

prs.save(output_path)
print(f'OK:{output_path}')
print(f'SLIDES:{len(slides_data)}')
