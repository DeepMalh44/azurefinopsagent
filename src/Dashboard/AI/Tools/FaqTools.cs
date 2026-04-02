using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace AzureFinOps.Dashboard.AI.Tools;

/// <summary>
/// Publishes useful public Q&As as SEO-indexable FAQ pages.
/// The LLM calls PublishFAQ after answering a public FinOps question.
/// </summary>
public static class FaqTools
{
    private static readonly string FaqDir = Path.Combine(Path.GetTempPath(), "finops-agent-faq");
    private static readonly string FaqFile = Path.Combine(FaqDir, "dynamic-faqs.json");
    private static readonly ConcurrentDictionary<string, FaqEntry> DynamicFaqs = new(StringComparer.OrdinalIgnoreCase);

    static FaqTools()
    {
        Directory.CreateDirectory(FaqDir);
        Load();
    }

    public static IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(PublishFAQ);
    }

    [Description("Publish a useful public FinOps Q&A as an SEO page. Call this ONLY for questions about Azure pricing, cost optimization, or FinOps best practices that would be useful to other users. Do NOT publish tenant-specific or private data.")]
    private static string PublishFAQ(
        [Description("The question (e.g. 'How much does a D4s_v5 VM cost per month?')")] string question,
        [Description("A concise, factual answer (1-3 sentences with specific numbers)")] string answer,
        [Description("SEO page title (e.g. 'Azure D4s_v5 VM Pricing by Region')")] string title)
    {
        if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(answer) || string.IsNullOrWhiteSpace(title))
            return "Error: question, answer, and title are all required.";

        var slug = GenerateSlug(question);
        var entry = new FaqEntry
        {
            Slug = slug,
            Title = title,
            Question = question,
            Answer = answer,
            CreatedUtc = DateTime.UtcNow.ToString("yyyy-MM-dd"),
        };

        DynamicFaqs[slug] = entry;
        Save();

        // Ping IndexNow asynchronously (fire-and-forget)
        _ = PingIndexNowAsync(slug);

        return JsonSerializer.Serialize(new { published = true, url = $"/faq/{slug}" });
    }

    public static IReadOnlyDictionary<string, FaqEntry> GetAll() => DynamicFaqs;

    public static bool TryGet(string slug, out FaqEntry entry) => DynamicFaqs.TryGetValue(slug, out entry!);

    private static string GenerateSlug(string text)
    {
        var slug = text.ToLowerInvariant();
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"\s+", "-");
        slug = slug.Trim('-');
        if (slug.Length > 60) slug = slug[..60].TrimEnd('-');
        return slug;
    }

    private static void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(DynamicFaqs.Values.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FaqFile, json);
        }
        catch { }
    }

    private static void Load()
    {
        try
        {
            if (File.Exists(FaqFile))
            {
                var json = File.ReadAllText(FaqFile);
                var entries = JsonSerializer.Deserialize<List<FaqEntry>>(json);
                if (entries is not null)
                    foreach (var e in entries)
                        DynamicFaqs[e.Slug] = e;
            }
        }
        catch { }
    }

    private static async Task PingIndexNowAsync(string slug)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var body = JsonSerializer.Serialize(new
            {
                host = "azure-finops-agent.com",
                urlList = new[] { $"https://azure-finops-agent.com/faq/{slug}" },
                key = "finopsagent2026"
            });
            await http.PostAsync("https://api.indexnow.org/indexnow",
                new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
        }
        catch { }
    }

    public class FaqEntry
    {
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string Question { get; set; } = "";
        public string Answer { get; set; } = "";
        public string CreatedUtc { get; set; } = "";
    }
}
