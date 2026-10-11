/// <summary>Picking versions, the weekly refresh, and pinning.</summary>
public class VersionTests
{
    [Test]
    [Arguments(new[] {"1.0.0", "1.2.0", "1.10.0", "1.9.9"}, "1.10.0")]
    [Arguments(new[] {"1.0.0", "2.0.0-beta.1"}, "1.0.0")]
    [Arguments(new[] {"1.0.0+build.5", "1.0.1-rc"}, "1.0.0+build.5")]
    [Arguments(new[] {"4.0.0.1", "4.0.0"}, "4.0.0.1")]
    [Arguments(new[] {"1.0.0-alpha", "not a version"}, null)]
    public async Task NewestStable(string[] versions, string? expected) =>
        await Assert.That(StableVersion.Newest(versions)).IsEqualTo(expected);

    const string file =
        """
        {
          "updated": "2026-10-01",
          "packages": {
            "NServiceBus": "10.2.9",
            "NServiceBus.RabbitMQ": "11.0.0",
            "Serilog.Sinks.Console": "6.0.0"
          },
          "pinned": {
            "Serilog.Sinks.Console": "an example of a package held back"
          }
        }

        """;

    static Dictionary<string, IReadOnlyList<string>> newer = new()
    {
        ["NServiceBus"] = ["10.2.9"],
        ["NServiceBus.RabbitMQ"] = ["11.0.0", "11.2.1", "12.0.0-alpha.1"],
        ["Serilog.Sinks.Console"] = ["6.0.0", "6.1.1"]
    };

    [Test]
    public Task RefreshMovesOnlyWhatChangedAndLeavesPinnedAlone()
    {
        var result = VersionRefresh.Refresh(file, newer, new(2026, 10, 8));
        return Verify(
            $"""
             {result.Json}
             ==== summary

             {result.Summary()}
             """);
    }

    /// <summary>A week with nothing new rewrites nothing, so no pull request is opened.</summary>
    [Test]
    public async Task RefreshWithNothingNewLeavesTheFileAsItWas()
    {
        var result = VersionRefresh.Refresh(
            file,
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["NServiceBus"] = ["10.2.9"],
                ["NServiceBus.RabbitMQ"] = ["11.0.0"]
            },
            new(2026, 10, 8));
        await Assert.That(result.Changed).IsFalse();
        await Assert.That(result.Json).IsEqualTo(file);
    }

    /// <summary>A package nuget.org did not answer for keeps its version, and the summary says so.</summary>
    [Test]
    public async Task RefreshKeepsWhatWasNotAnswered()
    {
        var result = VersionRefresh.Refresh(
            file,
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["NServiceBus.RabbitMQ"] = ["11.2.1"]
            },
            new(2026, 10, 8));
        await Assert.That(result.Unanswered).IsEquivalentTo(["NServiceBus"]);
        await Assert.That(result.Json).Contains("\"NServiceBus\": \"10.2.9\"");
    }

    /// <summary>The refresh writes the file exactly as it is kept, so its own run produces no churn.</summary>
    [Test]
    public async Task RefreshKeepsTheBakedFileFormat()
    {
        var path = Path.Combine(RepoPaths.SrcDirectory, "Wizard.Core", "Versions", "package-versions.json");
        var json = await File.ReadAllTextAsync(path);
        var ids = PackageVersions.Baked.Ids.ToList();
        // one version moved, so the file is rewritten, then compared with the moved line put back
        var lists = ids.ToDictionary(_ => _, _ => (IReadOnlyList<string>) [PackageVersions.Baked[_]], StringComparer.OrdinalIgnoreCase);
        lists["NServiceBus"] = ["999.0.0"];
        var result = VersionRefresh.Refresh(json, lists, PackageVersions.Baked.Updated);

        var restored = result.Json.Replace("\"NServiceBus\": \"999.0.0\"", $"\"NServiceBus\": \"{PackageVersions.Baked["NServiceBus"]}\"");
        await Assert.That(restored).IsEqualTo(json.ReplaceLineEndings("\n"));
    }

    /// <summary>The live lookup asks about what the output names, and nothing more.</summary>
    [Test]
    public async Task OnlyEmittedPackagesAreLookedUp()
    {
        var plan = Plan.Build(GeneratorTests.WithFeatures(GeneratorTests.State(), "Heartbeats"), PackageVersions.Baked, GeneratorTests.Today);
        await Assert.That(plan.EmittedPackages).IsEquivalentTo(["NServiceBus", "NServiceBus.Heartbeat", "xunit.v3", "NServiceBus.Testing"]);

        var addition = Plan.Build(GeneratorTests.Addition(Transport.RabbitMQ, Persistence.MongoDb, "Heartbeats"), PackageVersions.Baked, GeneratorTests.Today);
        await Assert.That(addition.EmittedPackages).IsEquivalentTo(["NServiceBus.Heartbeat"]);
    }

    [Test]
    public async Task TheGuideSaysWhereTheVersionsCameFrom()
    {
        var state = GeneratorTests.State();
        var baked = DocsGenerator.Build(Plan.Build(state, GeneratorTests.Versions, GeneratorTests.Today));
        await Assert.That(baked).Contains("Package versions: the newest stable releases as of 2026-10-01; nuget.org was not checked");

        var live = DocsGenerator.Build(Plan.Build(state, GeneratorTests.Versions.With(new Dictionary<string, string>()), GeneratorTests.Today));
        await Assert.That(live).Contains("Package versions: the newest stable release of each on nuget.org");
    }
}
