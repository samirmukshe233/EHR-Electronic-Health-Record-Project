using System.Text.Json;
using Ehr.Testing;
using Xunit;

namespace Ehr.Web.Tests;

// docs/brand/tokens.json is the brand's source of truth. tools/brand/sync.cs writes the tokens' CSS from it, and copies
// the web apps' shared CSS, fonts and logos into the desktop app, whose web view loads only its own folder. These fail
// when either was changed without running it again.
public class BrandAssetsTests
{
    const string Fix = "Run from the repository folder: dotnet run tools/brand/sync.cs";

    [Fact]
    public void Tokens_css_has_every_token_in_tokens_json()
    {
        using var tokens = JsonDocument.Parse(File.ReadAllText(RepositoryRoot.Combine("docs/brand/tokens.json")));
        var css = File.ReadAllText(RepositoryRoot.Combine("src/Ehr.Design/wwwroot/css/tokens.css"));
        var missing = new List<string>();

        foreach (var group in new[] { "color", "shadow" })
        {
            foreach (var token in tokens.RootElement.GetProperty(group).GetProperty("tokens").EnumerateArray())
            {
                foreach (var theme in token.GetProperty("value").EnumerateObject())
                {
                    Expect($"--{token.GetProperty("name").GetString()}: {Reference(theme.Value.GetString()!)};");
                }
            }
        }

        foreach (var group in new[] { "spacing", "radius", "size" })
        {
            foreach (var token in tokens.RootElement.GetProperty(group).GetProperty("tokens").EnumerateArray())
            {
                Expect($"--{token.GetProperty("name").GetString()}: {token.GetProperty("value").GetString()};");
            }
        }

        var type = tokens.RootElement.GetProperty("type");
        foreach (var family in type.GetProperty("families").EnumerateObject())
        {
            Expect($"--font-{family.Name}: {family.Value.GetString()};");
        }

        foreach (var font in type.GetProperty("fonts").EnumerateArray())
        {
            Expect($"url('../{font.GetProperty("file").GetString()}')");
        }

        foreach (var style in type.GetProperty("groups").EnumerateArray().SelectMany(group => group.GetProperty("styles").EnumerateArray()))
        {
            Expect($".{style.GetProperty("name").GetString()} {{");
            Expect($"font-size: {style.GetProperty("fontSize").GetString()}; line-height: {style.GetProperty("lineHeight").GetString()}; "
                + $"font-weight: {style.GetProperty("fontWeight").GetInt32()};");
        }

        Assert.True(missing.Count == 0, $"tokens.css is out of date with docs/brand/tokens.json. {Fix}\nMissing: {string.Join("\n", missing)}");

        void Expect(string text)
        {
            if (!css.Contains(text, StringComparison.Ordinal))
            {
                missing.Add(text);
            }
        }

        // A value such as {brand-navy} points at another token.
        static string Reference(string value) => value.StartsWith('{') && value.EndsWith('}') ? $"var(--{value[1..^1]})" : value;
    }

    [Theory]
    [InlineData("css")]
    [InlineData("fonts")]
    [InlineData("img")]
    public void The_desktop_app_has_the_same_files_as_the_web_apps(string folder)
    {
        var web = Files(RepositoryRoot.Combine($"src/Ehr.Design/wwwroot/{folder}"));
        var desktop = Files(RepositoryRoot.Combine($"src/Ehr.Desktop/frontend/{folder}"));

        Assert.True(web.Keys.Order(StringComparer.Ordinal).SequenceEqual(desktop.Keys.Order(StringComparer.Ordinal)),
            $"src/Ehr.Desktop/frontend/{folder} doesn't hold the same files as src/Ehr.Design/wwwroot/{folder}. {Fix}");
        foreach (var (name, content) in web)
        {
            Assert.True(content.AsSpan().SequenceEqual(desktop[name]),
                $"src/Ehr.Desktop/frontend/{folder}/{name} differs from src/Ehr.Design/wwwroot/{folder}/{name}. {Fix}");
        }

        static Dictionary<string, byte[]> Files(string path) => Directory.GetFiles(path)
            .Where(file => !Path.GetFileName(file).StartsWith('.'))
            .ToDictionary(file => Path.GetFileName(file), File.ReadAllBytes, StringComparer.Ordinal);
    }

    // Offline first: the on-site server and the desktop app must work with no internet, so nothing in the shared CSS
    // may point at another host.
    [Theory]
    [InlineData("tokens.css")]
    [InlineData("ehr.css")]
    public void The_shared_css_loads_nothing_from_another_host(string file)
    {
        var css = File.ReadAllText(RepositoryRoot.Combine($"src/Ehr.Design/wwwroot/css/{file}"));

        Assert.DoesNotContain("http://", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@import", css, StringComparison.OrdinalIgnoreCase);
    }
}
