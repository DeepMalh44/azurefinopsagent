using System.Diagnostics;

namespace AzureFinOps.Dashboard.Web;

/// <summary>
/// Frontend-facing metadata endpoints (build version, App Insights config, model list).
/// </summary>
public static class MetaEndpoints
{
    public static void MapMetaEndpoints(
        this IEndpointRouteBuilder app,
        string appInsightsConnectionString,
        string azureOpenAIDeployment)
    {
        var (sha, build) = ResolveBuildInfo();

        app.MapGet("/api/version", () => Results.Ok(new { sha, build, started = DateTime.UtcNow.ToString("o") }));

        app.MapGet("/api/config", () => Results.Ok(new { appInsightsConnectionString = appInsightsConnectionString ?? "" }));

        app.MapGet("/api/models", (HttpContext ctx) =>
        {
            if (ctx.Session.GetString("user") is null)
                return Results.Unauthorized();

            return Results.Json(new[]
            {
                new { id = azureOpenAIDeployment, name = azureOpenAIDeployment }
            });
        });
    }

    private static (string Sha, string Build) ResolveBuildInfo()
    {
        var sha = Environment.GetEnvironmentVariable("BUILD_SHA");
        if (string.IsNullOrEmpty(sha))
        {
            try { sha = Process.Start(new ProcessStartInfo("git", "rev-parse --short HEAD") { RedirectStandardOutput = true, UseShellExecute = false })!.StandardOutput.ReadToEnd().Trim(); }
            catch { sha = "dev"; }
        }

        var build = Environment.GetEnvironmentVariable("BUILD_NUMBER");
        if (string.IsNullOrEmpty(build))
        {
            try { build = Process.Start(new ProcessStartInfo("git", "rev-list --count HEAD") { RedirectStandardOutput = true, UseShellExecute = false })!.StandardOutput.ReadToEnd().Trim(); }
            catch { build = "0"; }
        }

        return (sha!, build!);
    }
}
