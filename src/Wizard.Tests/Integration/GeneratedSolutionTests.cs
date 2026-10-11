/// <summary>
/// Generates solutions, writes them to disk, and runs <c>dotnet build</c>, <c>dotnet test</c> and the
/// endpoint itself on them. Slow and needs network for restore, so explicit: run by integration.yml,
/// or with <c>--treenode-filter "/*/*/GeneratedSolutionTests/*"</c>.
/// </summary>
[Explicit]
[NotInParallel]
public class GeneratedSolutionTests
{
    [Test]
    [MatrixDataSource]
    public async Task CoreSolutionBuildsAndPasses(
        [Matrix(TestFramework.XunitV3, TestFramework.NUnit, TestFramework.TUnit, TestFramework.MSTest)] TestFramework framework,
        Hosting hosting)
    {
        var state = State() with
        {
            TestFramework = framework,
            Hosting = hosting
        };
        var directory = await Generate(state, $"core-{framework}-{hosting}");

        var build = await Run(directory, "build --configuration Release");
        await Assert.That(build).DoesNotContain("warning").Because(build);
        var output = await Run(directory, "test --configuration Release --no-build");

        // An undiscovered test is not a failure, so count them: the submitter and the handler.
        await Assert.That(PassedCount(output)).IsEqualTo(2).Because(output);
    }

    /// <summary>With sagas the test project has a saga test too, and message conventions move the messages.</summary>
    [Test]
    public async Task SagaTestsPass()
    {
        var directory = await Generate(GeneratorTests.WithFeatures(State(), "Conventions", "Sagas"), "sagas");
        await Run(directory, "build --configuration Release");
        var output = await Run(directory, "test --configuration Release --no-build");
        await Assert.That(PassedCount(output)).IsEqualTo(3).Because(output);
    }

    /// <summary>
    /// The one configuration that needs nothing installed is run for real: the order submitted on
    /// startup has to be handled, its event handled, and the saga it starts has to time out.
    /// </summary>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LearningEndpointProcessesTheSampleOrder(bool conventions)
    {
        var state = GeneratorTests.WithFeatures(State(), "Sagas", "Recoverability", "Auditing");
        if (conventions)
        {
            state = GeneratorTests.WithFeatures(state, [.. state.SelectedFeatures, "Conventions"]);
        }

        var directory = await Generate(state, $"run-{conventions}");
        await Run(directory, "build --configuration Release");

        var log = await RunUntil(
            directory,
            $"run --no-build --configuration Release --project src/{state.SolutionName}",
            "order confirmed",
            TimeSpan.FromSeconds(90));
        await Assert.That(log).Contains("placed for Widget");
        await Assert.That(log).Contains("event received");
        await Assert.That(log).Contains("waiting for the cancellation window");
        await Assert.That(log).Contains("order confirmed");
    }

    [Test]
    [MatrixDataSource]
    public Task TransportBuilds(Transport transport)
    {
        var state = State() with
        {
            Transport = transport
        };
        return Build(state, $"transport-{transport}");
    }

    [Test]
    [MatrixDataSource]
    public Task PersistenceBuilds(Persistence persistence)
    {
        var state = State() with
        {
            Persistence = persistence
        };
        return Build(state, $"persistence-{persistence}");
    }

    [Test]
    [MatrixDataSource]
    public Task SerializerBuilds(Serializer serializer)
    {
        var state = State() with
        {
            Serializer = serializer
        };
        return Build(state, $"serializer-{serializer}");
    }

    /// <summary>
    /// Every feature on its own has to compile, with whatever it requires. The registry's code is
    /// written by hand against packages that move, and only the compiler notices when one drifts.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(EveryFeature))]
    public async Task FeatureBuilds(string id)
    {
        var feature = Features.ById[id];
        var state = State() with
        {
            // the learning persistence has no outbox
            Persistence = Persistence.NonDurable
        };
        state = GeneratorTests.WithFeatures(state, [id, .. feature.Requires]);
        await Assert.That(state.Has(id)).IsTrue();
        await Build(state, $"feature-{id}");
    }

    public static IEnumerable<Func<string>> EveryFeature() =>
        Features.All.Select<FeatureDefinition, Func<string>>(_ => () => _.Id);

    [Test]
    [MethodDataSource(nameof(Combinations))]
    public Task CombinationBuilds(string name) =>
        Build(combinations[name](), $"combination-{name}");

    public static IEnumerable<string> Combinations() =>
        combinations.Keys;

    // Keyed by name, so the test's display name is the name rather than the whole state.
    static Dictionary<string, Func<WizardState>> combinations = new()
    {
        // Everything that can be on at once, on one SQL Server database.
        ["Everything"] = GeneratorTests.Everything,
        ["SendOnlyWeb"] = () => GeneratorTests.WithFeatures(
            State() with
            {
                Hosting = Hosting.AspNetCore,
                Transport = Transport.RabbitMQ
            },
            "SendOnly", "Heartbeats", "Serilog", "OpenTelemetry", "AttachmentsFileShare"),
        ["PostgreSql"] = () => GeneratorTests.WithFeatures(
            State() with
            {
                Transport = Transport.PostgreSql,
                Persistence = Persistence.PostgreSql
            },
            "Sagas", "Outbox"),
        ["Azure"] = () => GeneratorTests.WithFeatures(
            State() with
            {
                Transport = Transport.AzureServiceBus,
                Persistence = Persistence.CosmosDb,
                Serializer = Serializer.NewtonsoftJson
            },
            "Sagas", "Outbox"),
        ["Aws"] = () => GeneratorTests.WithFeatures(
            State() with
            {
                Transport = Transport.AmazonSqs,
                Persistence = Persistence.DynamoDb
            },
            "Sagas", "Outbox")
    };

    static WizardState State() =>
        GeneratorTests.State();

    static async Task Build(WizardState state, string name)
    {
        var directory = await Generate(state, name);
        var output = await Run(directory, "build --configuration Release");
        await Assert.That(output).DoesNotContain("warning").Because(output);
    }

    static async Task<string> Generate(WizardState state, string name)
    {
        state.Normalize();
        var plan = Plan.Build(state, PackageVersions.Baked, Date.FromDateTime(DateTime.UtcNow));

        var directory = Path.Combine(Path.GetTempPath(), "NServiceBusWizardIntegration", name);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        foreach (var file in SolutionGenerator.Build(plan))
        {
            var path = Path.Combine(directory, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, file.ToBytes());
        }

        return directory;
    }

    /// <summary>Microsoft.Testing.Platform prints "succeeded: n".</summary>
    static int PassedCount(string output)
    {
        var match = Regex.Match(output, @"(?:Passed:|succeeded:)\s+(\d+)");
        if (match.Success)
        {
            return int.Parse(match.Groups[1].Value);
        }

        return 0;
    }

    static ProcessStartInfo StartInfo(string directory, string arguments) =>
        new("dotnet", arguments)
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Environment =
            {
                ["DOTNET_NOLOGO"] = "true"
            }
        };

    static async Task<string> Run(string directory, string arguments)
    {
        using var process = Process.Start(StartInfo(directory, arguments))!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new($"dotnet {arguments} failed with exit code {process.ExitCode} in {directory}:\n{await output}\n{await error}");
        }

        return await output;
    }

    /// <summary>Runs a process that does not end by itself until its output has the marker, then stops it.</summary>
    static async Task<string> RunUntil(string directory, string arguments, string marker, TimeSpan limit)
    {
        using var process = Process.Start(StartInfo(directory, arguments))!;
        var log = new StringBuilder();
        var seen = new TaskCompletionSource();

        void Collect(object sender, DataReceivedEventArgs args)
        {
            if (args.Data == null)
            {
                return;
            }

            lock (log)
            {
                log.AppendLine(args.Data);
            }

            if (args.Data.Contains(marker, StringComparison.Ordinal))
            {
                seen.TrySetResult();
            }
        }

        process.OutputDataReceived += Collect;
        process.ErrorDataReceived += Collect;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await Task.WhenAny(seen.Task, process.WaitForExitAsync(), Task.Delay(limit));
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }

        lock (log)
        {
            return log.ToString();
        }
    }
}
