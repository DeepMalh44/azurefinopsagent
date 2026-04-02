using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AzureFinOps.Dashboard.AI.Tools;

public static class FollowUpTools
{
    public static IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(SuggestFollowUp);
    }

    [Description("Call this after answering to suggest a logical next action. Shows a clickable button in the UI. Use when a clear, actionable follow-up exists — e.g. after showing costs, suggest drilling into the top service; after comparing pricing, suggest adding reserved pricing.")]
    private static string SuggestFollowUp(
        [Description("Short button label (max 60 chars), e.g. 'Drill into top service' or 'Add reserved pricing'")] string label,
        [Description("Full prompt sent when clicked — must be a complete, actionable instruction, e.g. 'Show cost breakdown for the most expensive service this month'")] string prompt)
    {
        return JsonSerializer.Serialize(new { label, prompt });
    }
}
