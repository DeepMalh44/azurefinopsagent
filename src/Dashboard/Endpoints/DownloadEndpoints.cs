using AzureFinOps.Dashboard.AI.Tools;

namespace AzureFinOps.Dashboard.Endpoints;

/// <summary>
/// Single-use file downloads for generated PowerPoint and script artifacts.
/// </summary>
public static class DownloadEndpoints
{
    public static void MapDownloadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/download/pptx/{fileId}", (string fileId) =>
        {
            if (!PresentationTools.GeneratedFiles.TryGetValue(fileId, out var entry))
                return Results.NotFound(new { error = "File not found or expired" });

            if (!File.Exists(entry.Path))
            {
                PresentationTools.GeneratedFiles.TryRemove(fileId, out _);
                return Results.NotFound(new { error = "File no longer available" });
            }

            var fileName = Path.GetFileName(entry.Path);
            var downloadName = fileName.Contains('_') ? fileName[(fileName.IndexOf('_') + 1)..] : fileName;
            var bytes = File.ReadAllBytes(entry.Path);

            try { File.Delete(entry.Path); } catch { }
            PresentationTools.GeneratedFiles.TryRemove(fileId, out _);

            return Results.File(bytes, "application/vnd.openxmlformats-officedocument.presentationml.presentation", downloadName);
        });

        app.MapGet("/api/download/script/{fileId}", (string fileId) =>
        {
            if (!ScriptTools.GeneratedFiles.TryGetValue(fileId, out var entry))
                return Results.NotFound(new { error = "File not found or expired" });

            if (!File.Exists(entry.Path))
            {
                ScriptTools.GeneratedFiles.TryRemove(fileId, out _);
                return Results.NotFound(new { error = "File no longer available" });
            }

            var fileName = Path.GetFileName(entry.Path);
            var downloadName = fileName.Contains('_') ? fileName[(fileName.IndexOf('_') + 1)..] : fileName;
            var bytes = File.ReadAllBytes(entry.Path);
            var contentType = downloadName.EndsWith(".ps1") ? "application/x-powershell" : "application/x-shellscript";

            return Results.File(bytes, contentType, downloadName);
        });
    }
}
