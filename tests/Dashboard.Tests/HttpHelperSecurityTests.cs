using AzureFinOps.Dashboard.Infrastructure;
using Xunit;

namespace AzureFinOps.Dashboard.Tests;

/// <summary>
/// Verifies the read/write security boundary enforced by <see cref="HttpHelper.ResolveMethod"/>.
/// This is the single code-level guard preventing the agent from issuing DELETE requests
/// against the user's Azure subscription via QueryAzure / QueryGraph / QueryLogAnalytics.
/// </summary>
public class HttpHelperSecurityTests
{
    [Theory]
    [InlineData("GET")]
    [InlineData("get")]
    [InlineData(" get ")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public void ResolveMethod_AllowsApprovedMethods(string method)
    {
        var (resolved, error) = HttpHelper.ResolveMethod(method, activity: null, telemetryPrefix: "test");

        Assert.Null(error);
        Assert.NotNull(resolved);
    }

    [Theory]
    [InlineData("DELETE")]
    [InlineData("delete")]
    [InlineData(" Delete ")]
    public void ResolveMethod_BlocksDelete(string method)
    {
        var (resolved, error) = HttpHelper.ResolveMethod(method, activity: null, telemetryPrefix: "test");

        Assert.Null(resolved);
        Assert.NotNull(error);
        Assert.Contains("403", error);
        Assert.Contains("DELETE", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("CONNECT")]
    [InlineData("TRACE")]
    [InlineData("FOO")]
    [InlineData("")]
    public void ResolveMethod_RejectsUnknownMethods(string method)
    {
        var (resolved, error) = HttpHelper.ResolveMethod(method, activity: null, telemetryPrefix: "test");

        Assert.Null(resolved);
        Assert.NotNull(error);
        Assert.Contains("400", error);
    }

    [Fact]
    public void ResolveMethod_NullDefaultsToGet()
    {
        var (resolved, error) = HttpHelper.ResolveMethod(null, activity: null, telemetryPrefix: "test");

        Assert.Null(error);
        Assert.Equal(HttpMethod.Get, resolved);
    }

    [Fact]
    public void TokenMissing_Returns401()
    {
        var result = HttpHelper.TokenMissing("AzureToken", activity: null, telemetryPrefix: "azure");

        Assert.Contains("401", result);
        Assert.Contains("AzureToken", result);
    }
}
