/// <summary>
/// Anti-rot checks: values the wizard bakes in or links to must match their sources in this repo.
/// These run in the Pages deploy workflow, so drift blocks deployment instead of publishing wrong output.
/// </summary>
public class RepoContractTests
{
    static string ReadSrc(params string[] segments) =>
        File.ReadAllText(RepoPaths.SrcFile(segments));

    static string ReadRepo(params string[] segments) =>
        File.ReadAllText(RepoPaths.RepoFile(segments));

    static string BuildProperty(string name)
    {
        var props = XDocument.Load(RepoPaths.SrcFile("Directory.Build.props"));
        return props.Descendants(name).Single().Value;
    }

    [Test]
    public async Task WizardVersionMatchesDirectoryBuildProps() =>
        await Assert.That(WizardDefaults.WizardVersion).IsEqualTo(BuildProperty("Version"));

    /// <summary>Generated solutions pin the same SDK as this repo.</summary>
    [Test]
    public async Task SdkVersionMatchesGlobalJson()
    {
        using var globalJson = JsonDocument.Parse(ReadRepo("global.json"));
        var version = globalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString();
        await Assert.That(WizardDefaults.SdkVersion).IsEqualTo(version);
    }

    /// <summary>Generated solutions target the same framework as this repo.</summary>
    [Test]
    public async Task TargetFrameworkMatchesDirectoryBuildProps() =>
        await Assert.That(WizardDefaults.TargetFramework).IsEqualTo(BuildProperty("GeneratedTargetFramework"));

    [Test]
    public async Task WizardLinksMatchTheDeployedSite()
    {
        // The deploy workflow rewrites <base href> to the Pages project path; the urls the wizard
        // prints into generated files and other projects' docs have to point at that same path.
        var workflow = ReadRepo(".github", "workflows", "deploy.yml");
        var baseHref = Regex.Match(workflow, "<base href=\"(/[^\"]+/)\" />").Groups[1].Value;
        await Assert.That(new Uri(WizardLinks.Base).AbsolutePath + "/").IsEqualTo(baseHref);

        var readme = ReadRepo("readme.md");
        await Assert.That(readme).Contains(WizardLinks.Base + "/");
    }

    /// <summary>
    /// Every character the wizard renders must be covered by the bundled fonts. A character outside
    /// them falls back to a system font, which measures differently per platform — that re-wraps
    /// prose and moves the height of the ScreenSnapshotTests PNGs, which is exactly the drift the
    /// bundled fonts exist to remove. The unicode-range descriptors in app.css are the source of truth.
    /// </summary>
    [Test]
    public async Task ShippedFontsCoverRenderedText()
    {
        var css = ReadSrc("Wizard.Web", "wwwroot", "css", "app.css");
        var covered = ParseUnicodeRanges(css);
        await Assert.That(covered.Count).IsGreaterThan(0).Because("app.css should declare unicode-range descriptors");

        var uncovered = new SortedSet<char>(RenderedCharacters().Where(_ => !covered.Contains(_)));

        await Assert.That(uncovered)
            .IsEmpty()
            .Because(
                "these characters have no bundled glyph and would fall back to a system font: " +
                string.Join(", ", uncovered.Select(_ => $"U+{(int) _:X4} '{_}'")));
    }

    /// <summary>Text the wizard actually renders, taken from the html snapshots plus the razor and html sources.</summary>
    static IEnumerable<char> RenderedCharacters()
    {
        var testDirectory = RepoPaths.SrcFile("Wizard.Tests");
        foreach (var file in Directory.EnumerateFiles(testDirectory, "*.verified.html", SearchOption.AllDirectories))
        {
            // strip markup: attribute values are urls and css classes, not rendered text
            var text = Regex.Replace(File.ReadAllText(file), "<[^>]*>", " ");
            foreach (var character in WebUtility.HtmlDecode(text))
            {
                yield return character;
            }
        }

        var webDirectory = RepoPaths.SrcFile("Wizard.Web");
        var sources = Directory.EnumerateFiles(webDirectory, "*.razor", SearchOption.AllDirectories)
            .Append(Path.Combine(webDirectory, "wwwroot", "index.html"));
        foreach (var source in sources)
        {
            foreach (var character in File.ReadAllText(source))
            {
                yield return character;
            }
        }
    }

    static HashSet<char> ParseUnicodeRanges(string css)
    {
        var covered = new HashSet<char>
        {
            // markup and source formatting, never glyphs on screen
            '\r',
            '\n',
            '\t',
            '﻿'
        };

        foreach (Match declaration in Regex.Matches(css, @"unicode-range:\s*([^;]+);"))
        {
            foreach (Match range in Regex.Matches(declaration.Groups[1].Value, @"U\+([0-9A-Fa-f]+)(?:-([0-9A-Fa-f]+))?"))
            {
                var start = Convert.ToInt32(range.Groups[1].Value, 16);
                var end = start;
                if (range.Groups[2].Success)
                {
                    end = Convert.ToInt32(range.Groups[2].Value, 16);
                }

                for (var code = start; code <= end; code++)
                {
                    covered.Add((char) code);
                }
            }
        }

        return covered;
    }
}
