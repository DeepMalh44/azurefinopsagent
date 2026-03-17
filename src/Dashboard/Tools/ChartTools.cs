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
                [Description("Data as JSON array string. " +
                    "Single series: [[\"Apple\",100],[\"Banana\",200]] or [{\"name\":\"A\",\"value\":100}]. " +
                    "Multi-series (grouped bar/line): [{\"name\":\"D2s_v5\",\"East US\":70,\"West Europe\":84},{\"name\":\"D4s_v5\",\"East US\":140,\"West Europe\":168}]")] string data,
                [Description("X-axis label (optional)")] string? xAxisName,
                [Description("Y-axis label (optional)")] string? yAxisName
            ) =>
            {
                return JsonSerializer.Serialize(new { type, title, seriesName, data, xAxisName, yAxisName });
            },
            "RenderChart",
            "Renders a simple ECharts chart (bar, line, pie, scatter, funnel). Use for straightforward data visualization.");

        yield return AIFunctionFactory.Create(
            (
                [Description("Full ECharts option object as JSON string. " +
                    "Can include any ECharts configuration: title, tooltip, visualMap, xAxis, yAxis, series, etc. " +
                    "For world map charts, set series type to 'map' with map:'world' and data as [{name:'United States of America',value:100},...]. " +
                    "IMPORTANT: Use Natural Earth country names for map data — e.g. 'United States of America' (NOT 'United States' or 'USA'), " +
                    "'Czechia' (NOT 'Czech Republic'), 'Dem. Rep. Congo' (NOT 'DR Congo'), 'Côte d\'Ivoire' (NOT 'Ivory Coast'), " +
                    "'United Republic of Tanzania' (NOT 'Tanzania'), 'Lao PDR' (NOT 'Laos'), 'Myanmar' (NOT 'Burma'). " +
                    "Most other common country names (India, Brazil, France, Japan, etc.) are fine as-is. " +
                    "The frontend will auto-register the world map GeoJSON. " +
                    "For scatter on map, use series type 'scatter' with coordinateSystem:'geo'. " +
                    "Example map: {title:{text:'VM Pricing by Region'},tooltip:{trigger:'item'},visualMap:{min:0,max:100,inRange:{color:['#50a3ba','#eac736','#d94e5d']}},series:[{type:'map',map:'world',data:[{name:'United States of America',value:50}]}]}")] string options
            ) =>
            {
                return JsonSerializer.Serialize(new { raw = true, options });
            },
            "RenderAdvancedChart",
            "Renders any ECharts visualization using raw ECharts options JSON. Use for advanced charts like world maps, heatmaps, treemaps, radar, gauge, or any chart that needs full ECharts configuration. " +
            "For geographic maps (e.g. show pricing by Azure region on a world map), use series type 'map' with map:'world'. The frontend auto-loads the world map GeoJSON. " +
            "CRITICAL: Use Natural Earth country names in map data — 'United States of America' not 'United States', 'Czechia' not 'Czech Republic', etc.");
    }
}
