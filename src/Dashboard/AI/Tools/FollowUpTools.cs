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

    [Description(@"Call this after answering to suggest the next ACTION the user should take. Renders 1-3 clickable buttons in the UI.

You MUST call this exactly once per assistant turn (the only exception is when the conversation has clearly reached a natural endpoint).

## Rules
1. Each follow-up MUST reference a concrete entity from this turn (resource name, RG, service, file, $ figure, region, time window). Never generic (""tell me more"", ""want details?"", ""explore costs"").
2. The follow-up is an ACTION the user takes next, not a re-summary of what they just saw.
3. Keep each label \u226460 chars. The prompt should be a complete instruction the agent can execute.

## When the prior turn analyzed UPLOADED FILES (or any data-heavy answer)
You MUST pass label2/prompt2 (and ideally label3/prompt3) so the user sees a small action menu. The FIRST action MUST be a deep, decision-ready prioritization. Use this exact pattern:

  label  = ""Rank top 5 actions by $ impact""
  prompt = ""Across all the data we just analyzed, deeply re-examine it and produce a ranked list of the 5 most impactful, actionable FinOps actions I should take. For each: (a) the concrete action in one sentence, (b) which file/resource/RG it applies to, (c) estimated $ saving (or risk if it's a governance action), (d) effort (low/med/high). Keep it short and decision-ready \u2014 a CFO should be able to read it in 30 seconds.""

For label2/label3 pick from (in this order of preference):
  - ""Generate remediation script for top finding"" \u2192 calls GenerateScript on the #1 issue
  - ""Build CFO deck from this analysis""           \u2192 calls GenerateHtmlPresentation
  - ""Drill into <top cost driver / RG / SKU>""     \u2192 deep-dives the single biggest entity
  - ""Apply tags via PATCH to <N> untagged resources"" \u2192 only when Azure is connected and tagging is the top finding

## When the prior turn was a small / single-question answer
Pass just `label`/`prompt` (one button is fine).

## Examples
After a service breakdown:        label='Drill into Virtual Machines (top spender at $58K)'
After listing idle disks:         label='Generate cleanup script for the 47 unattached disks in rg-data-eus2'
After a maturity Crawl score:     label='Score Walk maturity'
After file analysis (heavy):      label='Rank top 5 actions by $ impact' + label2='Generate remediation script for the disks' + label3='Build a CFO deck'")]
    private static string SuggestFollowUp(
        [Description("Short button label (max 60 chars), e.g. 'Drill into Virtual Machines (top spender)' or 'Rank top 5 actions by $ impact'")] string label,
        [Description("Full prompt sent when clicked \u2014 must be a complete, actionable instruction referencing concrete entities (RG, service, $ figure)")] string prompt,
        [Description("Optional second button label (\u226460 chars). Use after data-heavy / multi-file answers to surface a remediation script, CFO deck, or top-driver drill-down.")] string? label2 = null,
        [Description("Optional second prompt \u2014 paired with label2.")] string? prompt2 = null,
        [Description("Optional third button label (\u226460 chars).")] string? label3 = null,
        [Description("Optional third prompt \u2014 paired with label3.")] string? prompt3 = null)
    {
        var actions = new List<object> { new { label, prompt } };
        if (!string.IsNullOrWhiteSpace(label2) && !string.IsNullOrWhiteSpace(prompt2))
            actions.Add(new { label = label2, prompt = prompt2 });
        if (!string.IsNullOrWhiteSpace(label3) && !string.IsNullOrWhiteSpace(prompt3))
            actions.Add(new { label = label3, prompt = prompt3 });
        // Back-compat: keep the top-level label/prompt for older clients.
        return JsonSerializer.Serialize(new { label, prompt, actions });
    }
}
