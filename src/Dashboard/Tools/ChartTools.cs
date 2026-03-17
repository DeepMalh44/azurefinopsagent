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
                    "For world map charts, set series type to 'map' with map:'world' and data as [{name:'United States',value:100},...]. " +
                    "The frontend will auto-register the world map GeoJSON. " +
                    "For scatter on map, use series type 'scatter' with coordinateSystem:'geo'. " +
                    "Example map: {title:{text:'VM Pricing by Region'},tooltip:{trigger:'item'},visualMap:{min:0,max:100,inRange:{color:['#50a3ba','#eac736','#d94e5d']}},series:[{type:'map',map:'world',data:[{name:'United States',value:50}]}]}")] string options
            ) =>
            {
                return JsonSerializer.Serialize(new { raw = true, options });
            },
            "RenderAdvancedChart",
            "Renders any ECharts visualization using raw ECharts options JSON. Use for advanced charts like world maps, heatmaps, treemaps, radar, gauge, or any chart that needs full ECharts configuration. " +
            "For geographic maps (e.g. show pricing by Azure region on a world map), use series type 'map' with map:'world'. The frontend auto-loads the world map GeoJSON.");
    }
}
