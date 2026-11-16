
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;
using static Ehr.Web.Tests.Browser;

namespace Ehr.Web.Tests;

// The shared layout must disable htmx history snapshots
// so pages containing PHI are not cached in localStorage.
public class LayoutTests(WebApps apps) : IClassFixture<WebApps>
{
    static CancellationToken Cancel =>
        TestContext.Current.CancellationToken;

    [Fact]
    public async Task Layout_disables_htmx_history_cache()
    {
        // Arrange: Sign in as an authorized clinician.
        var session = await SignInAsync(
            apps.OnPrem,
            await apps.ClinicianAsync());

        // Act: Request a page that uses the shared layout.
        var html = await session.Client.GetStringAsync(
            "/patients", Cancel);

        // Assert: Find the htmx configuration meta tag.
        var match = Regex.Match(
            html,
            """<meta\s+name="htmx-config"\s+content='([^']+)'""");

        Assert.True(
            match.Success,
            "The shared layout must contain an htmx-config meta tag.");

        // Parse the JSON configuration.
        using var config = JsonDocument.Parse(match.Groups[1].Value);

        Assert.True(
            config.RootElement.TryGetProperty(
                "historyCacheSize", out var cacheSize),
            "The htmx configuration must define historyCacheSize.");

        Assert.Equal(0, cacheSize.GetInt32());
    }
}
