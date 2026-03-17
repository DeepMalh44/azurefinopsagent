using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AzureFinOps.Dashboard.Tools;

public static class ChartTools
{
    public static IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(
            (
                [Description("Chart type: bar, line, pie, scatter, funnel")] string type,
                [Description("Chart title")] string title,
                [Description("Series name for the legend")] string seriesName,
                [Description(@"Data as JSON array string.
Single series: [[""Apple"",100],[""Banana"",200]] or [{""name"":""A"",""value"":100}].
Multi-series (grouped bar/line): [{""name"":""D2s_v5"",""East US"":70,""West Europe"":84},{""name"":""D4s_v5"",""East US"":140,""West Europe"":168}]")] string data,
                [Description("X-axis label (optional)")] string? xAxisName,
                [Description("Y-axis label (optional)")] string? yAxisName
            ) =>
            {
                return JsonSerializer.Serialize(new { type, title, seriesName, data, xAxisName, yAxisName });
            },
            "RenderChart",
            "Renders an interactive ECharts chart. Use for straightforward single-series or multi-series data visualization.");

        yield return AIFunctionFactory.Create(
            (
                [Description(@"Full ECharts option object as JSON string.
For world maps: use series type 'map' with map:'world'.
Use Natural Earth country names (e.g. 'United States of America' not 'USA', 'Czechia' not 'Czech Republic').
The frontend auto-registers world map GeoJSON.")] string options
            ) =>
            {
                return JsonSerializer.Serialize(new { raw = true, options });
            },
            "RenderAdvancedChart",
            "Renders any ECharts visualization using raw options JSON. Use for world maps, heatmaps, treemaps, radar, gauge, or charts needing full ECharts config.");
    }
}
