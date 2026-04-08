using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.AI;

using AzureFinOps.Dashboard.Auth;
using AzureFinOps.Dashboard.Infrastructure;

namespace AzureFinOps.Dashboard.AI.Tools;

/// <summary>
/// Reads Azure Blob Storage blobs (CSV cost exports) using the user's delegated storage token.
/// Designed for reading FOCUS-format cost export data from scheduled exports.
/// </summary>
public class StorageQueryTools
{
    private readonly UserTokens _tokens;

    public StorageQueryTools(UserTokens tokens) => _tokens = tokens;

    public IEnumerable<AIFunction> Create()
    {
        yield return AIFunctionFactory.Create(ListCostExportBlobs, "ListCostExportBlobs", @"Lists blobs in an Azure Storage container to discover cost export files.
Use this to find available FOCUS-format cost exports before reading them with ReadCostExportBlob.
Scheduled exports typically write to a path like: {exportName}/{YYYYMMDD-YYYYMMDD}/{file}.csv
Returns blob names, sizes, and last modified dates.");

        yield return AIFunctionFactory.Create(ReadCostExportBlob, "ReadCostExportBlob", @"Reads a CSV blob from Azure Storage containing cost export data (FOCUS format).
Use this after ListCostExportBlobs to read the actual cost data.
For large files (>1MB), only the first 500KB is returned — use Python with pandas for full analysis.
FOCUS columns include: BilledCost, EffectiveCost, ServiceCategory, ServiceName, SubAccountName, ResourceId, Region, ChargeCategory, PricingModel, etc.
STRATEGY FOR LARGE EXPORTS:
1. First call this tool to get a sample (first 500KB) to understand the columns and data shape.
2. Save the full blob URL and use the built-in bash tool to run: curl -H 'Authorization: Bearer {token}' '{blobUrl}' -o /tmp/export.csv
3. Then run a Python script with pandas to process the full CSV: groupby, pivot, aggregate.");
    }

    private async Task<string> ListCostExportBlobs(
        [Description("Storage account name (e.g. 'mystorageaccount')")] string storageAccount,
        [Description("Container name (e.g. 'exports' or 'cost-exports')")] string container,
        [Description("Optional blob prefix/path to filter results (e.g. 'monthly-export/202604'). Omit to list all.")] string? prefix = null)
    {
        using var activity = HttpHelper.Telemetry.StartActivity("ListCostExportBlobs");
        activity?.SetTag("storage.account", storageAccount);
        activity?.SetTag("storage.container", container);
        activity?.SetTag("storage.prefix", prefix);

        var token = _tokens.StorageToken;
        if (string.IsNullOrEmpty(token))
            return HttpHelper.TokenMissing("StorageToken", activity, "storage");

        if (string.IsNullOrWhiteSpace(storageAccount) || string.IsNullOrWhiteSpace(container))
        {
            activity?.SetTag("storage.result", "invalid_input");
            return "HTTP 400 BadRequest\nBoth storageAccount and container are required.";
        }

        var url = $"https://{Uri.EscapeDataString(storageAccount)}.blob.core.windows.net/{Uri.EscapeDataString(container)}?restype=container&comp=list&maxresults=50";
        if (!string.IsNullOrWhiteSpace(prefix))
            url += $"&prefix={Uri.EscapeDataString(prefix)}";

        return await HttpHelper.SendWithRetryAsync(
            url, token, activity, "storage",
            extraHeaders: new Dictionary<string, string> { ["x-ms-version"] = "2026-02-06" },
            includeTimestamp: true);
    }

    private async Task<string> ReadCostExportBlob(
        [Description("Storage account name")] string storageAccount,
        [Description("Container name")] string container,
        [Description("Full blob path/name (e.g. 'monthly-export/20260401-20260430/export.csv')")] string blobPath)
    {
        using var activity = HttpHelper.Telemetry.StartActivity("ReadCostExportBlob");
        activity?.SetTag("storage.account", storageAccount);
        activity?.SetTag("storage.container", container);
        activity?.SetTag("storage.blob", blobPath);

        var token = _tokens.StorageToken;
        if (string.IsNullOrEmpty(token))
            return HttpHelper.TokenMissing("StorageToken", activity, "storage");

        if (string.IsNullOrWhiteSpace(storageAccount) || string.IsNullOrWhiteSpace(container) || string.IsNullOrWhiteSpace(blobPath))
        {
            activity?.SetTag("storage.result", "invalid_input");
            return "HTTP 400 BadRequest\nstorageAccount, container, and blobPath are all required.";
        }

        var url = $"https://{Uri.EscapeDataString(storageAccount)}.blob.core.windows.net/{Uri.EscapeDataString(container)}/{blobPath}";

        return await HttpHelper.SendWithRetryAsync(
            url, token, activity, "storage",
            extraHeaders: new Dictionary<string, string> { ["x-ms-version"] = "2026-02-06" },
            includeTimestamp: true,
            maxResponseChars: 512_000); // 500KB limit — use Python for full file
    }
}
