// Keeps the brand's generated files in step with their sources. docs/brand/tokens.json is the source of truth for
// every color, type style, space, radius and size (docs/brand/README.md is the guide that goes with it).
//
//   dotnet run tools/brand/sync.cs              writes the files below
//   dotnet run tools/brand/sync.cs -- --check   writes nothing; exits with 1 and names each file that is out of date
//
// It writes:
//   - src/Ehr.Design/wwwroot/css/tokens.css: the tokens as CSS custom properties (light, dark, and dark when the
//     system asks for it and no theme is set), the @font-face rules and one class per type style;
//   - src/Ehr.Desktop/frontend/{css,fonts,img}: a copy of the web apps' shared CSS, fonts and logos. The desktop app's
//     web view loads only its own folder, so it can't take them from the Razor class library.
//
// Run it after changing tokens.json, the shared stylesheet, a font or a logo. CI runs it with --check, and
// tests/Ehr.Web.Tests/BrandAssetsTests.cs fails when the tokens' CSS or the desktop copy is out of date.

// Run, never published: without this, file-based apps default to native AOT and restore its compiler.
#:property PublishAot=false

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

var check = args.Contains("--check");
var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ScriptPath())!, "..", ".."));
var design = Path.Combine(root, "src", "Ehr.Design", "wwwroot");
var desktop = Path.Combine(root, "src", "Ehr.Desktop", "frontend");
var stale = new List<string>();

using var tokens = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "docs", "brand", "tokens.json")));
Put(Path.Combine(design, "css", "tokens.css"), Encoding.UTF8.GetBytes(TokensCss(tokens.RootElement)));

foreach (var folder in new[] { "css", "fonts", "img" })
{
    Mirror(Path.Combine(design, folder), Path.Combine(desktop, folder));
}

if (stale.Count > 0)
{
    Console.Error.WriteLine("Out of date:");
    foreach (var path in stale)
    {
        Console.Error.WriteLine($"  {Path.GetRelativePath(root, path).Replace('\\', '/')}");
    }

    Console.Error.WriteLine("Run: dotnet run tools/brand/sync.cs");
    return 1;
}

Console.WriteLine(check ? "The brand files are up to date." : "The brand files are written.");
return 0;

// Writes the file when its content differs or, with --check, records that it does.
void Put(string path, byte[] content)
{
    if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
    {
        return;
    }

    if (check)
    {
        stale.Add(path);
        return;
    }

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllBytes(path, content);
}

// Makes the target folder hold exactly the source folder's files.
void Mirror(string source, string target)
{
    var names = Directory.GetFiles(source).Select(Path.GetFileName).OfType<string>()
        .Where(name => !name.StartsWith('.')).Order(StringComparer.Ordinal).ToList();
    foreach (var name in names)
    {
        Put(Path.Combine(target, name), File.ReadAllBytes(Path.Combine(source, name)));
    }

    if (!Directory.Exists(target))
    {
        return;
    }

    foreach (var extra in Directory.GetFiles(target).Where(file => !names.Contains(Path.GetFileName(file))
        && !Path.GetFileName(file).StartsWith('.')))
    {
        if (check)
        {
            stale.Add(extra);
        }
        else
        {
            File.Delete(extra);
        }
    }
}

static string TokensCss(JsonElement tokens)
{
    var css = new StringBuilder();
    css.Append("/* SwiftChart tokens, generated from docs/brand/tokens.json by tools/brand/sync.cs: change that file, not this one.\n");
    css.Append("   Light is the default; dark applies with [data-theme=\"dark\"] or, when no theme is set, the system preference. */\n");

    var themes = tokens.GetProperty("color").GetProperty("themes").EnumerateArray().Select(theme => theme.GetProperty("id").GetString()!).ToList();
    foreach (var theme in themes)
    {
        css.Append(theme == themes[0] ? $":root, [data-theme=\"{theme}\"] {{\n" : $"[data-theme=\"{theme}\"] {{\n");
        Theme(css, tokens, theme, "  ");
        css.Append("}\n");
    }

    css.Append("@media (prefers-color-scheme: dark) {\n  :root:not([data-theme]) {\n");
    Theme(css, tokens, "dark", "    ");
    css.Append("  }\n}\n");

    css.Append(":root {\n");
    foreach (var group in new[] { "spacing", "radius", "size" })
    {
        foreach (var token in tokens.GetProperty(group).GetProperty("tokens").EnumerateArray())
        {
            css.Append($"  --{token.GetProperty("name").GetString()}: {token.GetProperty("value").GetString()};\n");
        }
    }

    var type = tokens.GetProperty("type");
    foreach (var family in type.GetProperty("families").EnumerateObject())
    {
        css.Append($"  --font-{family.Name}: {family.Value.GetString()};\n");
    }

    css.Append("}\n");

    // The fonts sit beside the css folder, in both the web apps and the desktop app's copy.
    foreach (var font in type.GetProperty("fonts").EnumerateArray())
    {
        var style = font.TryGetProperty("style", out var given) ? given.GetString() : "normal";
        css.Append($"@font-face {{ font-family: '{font.GetProperty("family").GetString()}'; src: url('../{font.GetProperty("file").GetString()}') format('woff2'); "
            + $"font-weight: {font.GetProperty("weight").GetString()}; font-style: {style}; font-display: swap; }}\n");
    }

    foreach (var group in type.GetProperty("groups").EnumerateArray())
    {
        foreach (var style in group.GetProperty("styles").EnumerateArray())
        {
            css.Append($".{style.GetProperty("name").GetString()} {{ font-family: var(--font-{group.GetProperty("family").GetString()}); "
                + $"font-size: {style.GetProperty("fontSize").GetString()}; line-height: {style.GetProperty("lineHeight").GetString()}; "
                + $"font-weight: {style.GetProperty("fontWeight").GetInt32()};");
            if (style.TryGetProperty("letterSpacing", out var spacing))
            {
                css.Append($" letter-spacing: {spacing.GetString()};");
            }

            css.Append(" }\n");
        }
    }

    return css.ToString();
}

// One theme's colors and shadows. A value such as {brand-navy} points at another token.
static void Theme(StringBuilder css, JsonElement tokens, string theme, string indent)
{
    css.Append($"{indent}color-scheme: {theme};\n");
    foreach (var group in new[] { "color", "shadow" })
    {
        foreach (var token in tokens.GetProperty(group).GetProperty("tokens").EnumerateArray())
        {
            var value = token.GetProperty("value").GetProperty(theme).GetString()!;
            if (value.StartsWith('{') && value.EndsWith('}'))
            {
                value = $"var(--{value[1..^1]})";
            }

            css.Append($"{indent}--{token.GetProperty("name").GetString()}: {value};\n");
        }
    }
}

static string ScriptPath([CallerFilePath] string path = "") => path;
