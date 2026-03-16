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
                [Description("Data as JSON array of arrays, e.g. [[\"Apple\",100],[\"Banana\",200]] or [{\"name\":\"A\",\"value\":100}]")] string data,
                [Description("X-axis label (optional)")] string? xAxisName,
                [Description("Y-axis label (optional)")] string? yAxisName
            ) =>
            {
                return JsonSerializer.Serialize(new { type, title, seriesName, data, xAxisName, yAxisName });
            },
            "RenderChart",
            "Renders an interactive ECharts chart in the dashboard UI. Use this whenever data would be better visualized as a chart instead of a table. Supports: bar, line, pie, scatter, funnel. The data parameter must be a JSON array string.");
    }
}
