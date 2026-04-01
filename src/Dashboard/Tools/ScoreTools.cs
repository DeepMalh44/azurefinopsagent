using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AzureFinOps.Dashboard.Tools;

public static class ScoreTools
{
    public static IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(ReportMaturityScore);
    }

    [Description(@"Report FinOps maturity scores after evaluating a level (crawl, walk, or run). Call this AFTER you have queried the relevant APIs and determined the scores. Each dimension gets a score from 0-5:
0 = Not started / no data
1 = Critical issues found
2 = Needs significant work
3 = Acceptable but room for improvement
4 = Good shape
5 = Excellent / best practice")]
    private static string ReportMaturityScore(
        [Description("Level: 'crawl', 'walk', or 'run'")] string level,
        [Description(@"JSON array of score objects, e.g.: [{""id"":""tagging"",""label"":""Tagging"",""score"":3,""detail"":""45% of resources tagged""}]")] string scores)
    {
        return $"__MATURITY_SCORE__:{level}:{scores}";
    }
}
