/// <summary>Snapshots of everything the generators produce. The clock is frozen.</summary>
public class GeneratorTests
{
    public static readonly Date Today = new(2026, 10, 1);

    /// <summary>The configuration that needs nothing installed, at the output step.</summary>
    public static WizardState State() =>
        new()
        {
            Flow = Flow.New,
            Hosting = Hosting.Worker,
            Transport = Transport.Learning,
            Persistence = Persistence.Learning,
            Serializer = Serializer.SystemJson,
            TestFramework = TestFramework.XunitV3,
            Step = "output"
        };

    /// <summary>A copy of the state with exactly these features selected.</summary>
    public static WizardState WithFeatures(WizardState state, params string[] ids)
    {
        var copy = state with
        {
            SelectedFeatures = new HashSet<string>(ids, StringComparer.Ordinal)
        };
        copy.Normalize();
        return copy;
    }

    /// <summary>An add-flow state: what the endpoint runs on, and what is being added to it.</summary>
    public static WizardState Addition(Transport transport, Persistence persistence, params string[] added)
    {
        var state = new WizardState
        {
            Flow = Flow.Add,
            Transport = transport,
            Persistence = persistence,
            SelectedFeatures = new HashSet<string>(added, StringComparer.Ordinal),
            Step = "output"
        };
        state.Normalize();
        return state;
    }

    /// <summary>Every feature that can be on at once: all but send-only and the second attachment store, on one SQL Server database.</summary>
    public static WizardState Everything() =>
        WithFeatures(
            State() with
            {
                Transport = Transport.SqlServer,
                Persistence = Persistence.SqlServer
            },
            [.. Features.All.Select(_ => _.Id).Where(_ => _ is not (Features.SendOnly or Features.AttachmentsFileShare))]);

    /// <summary>
    /// Every package at 1.0.0. The baked file is refreshed weekly, and snapshots taken with it would
    /// all change each time; that file's own diff is where a version change is reviewed.
    /// </summary>
    public static readonly PackageVersions Versions =
        PackageVersions.Create(Today, PackageVersions.Baked.Ids.ToDictionary(_ => _, _ => "1.0.0"));

    static Plan PlanFor(WizardState state) =>
        Plan.Build(state, Versions, Today);

    [Test]
    [MatrixDataSource]
    public Task Guide(Hosting hosting, bool sendOnly)
    {
        var state = State() with
        {
            Hosting = hosting
        };
        if (sendOnly)
        {
            state = WithFeatures(state, Features.SendOnly);
        }

        return Verify(DocsGenerator.Build(PlanFor(state)), "md")
            .UseParameters(hosting, sendOnly);
    }

    /// <summary>The guide, the AI instructions and every file, with everything that can be on at once.</summary>
    [Test]
    public Task GuideEverything() =>
        Verify(DocsGenerator.Build(PlanFor(Everything())), "md");

    [Test]
    public Task AiEverything() =>
        Verify(AiContentGenerator.Build(PlanFor(Everything())), "md");

    [Test]
    public Task SolutionFilesEverything() =>
        Verify(Render(Code(SolutionGenerator.Build(PlanFor(Everything())))));

    [Test]
    public Task Ai() =>
        Verify(AiContentGenerator.Build(PlanFor(State())), "md");

    [Test]
    [MatrixDataSource]
    public Task SolutionFiles(Hosting hosting, TestFramework framework)
    {
        var state = State() with
        {
            Hosting = hosting,
            TestFramework = framework
        };
        return Verify(Render(Code(SolutionGenerator.Build(PlanFor(state)))))
            .UseParameters(hosting, framework);
    }

    /// <summary>What a transport contributes: its code, packages, connection string and container.</summary>
    [Test]
    [MatrixDataSource]
    public Task TransportFiles(Transport transport)
    {
        var state = State() with
        {
            Transport = transport
        };
        return Verify(Render(Stack(SolutionGenerator.Build(PlanFor(state)))))
            .UseParameters(transport);
    }

    [Test]
    [MatrixDataSource]
    public Task PersistenceFiles(Persistence persistence)
    {
        var state = State() with
        {
            Persistence = persistence
        };
        return Verify(Render(Stack(SolutionGenerator.Build(PlanFor(state)))))
            .UseParameters(persistence);
    }

    [Test]
    [MatrixDataSource]
    public Task SerializerFiles(Serializer serializer)
    {
        var state = State() with
        {
            Serializer = serializer
        };
        return Verify(Render(Stack(SolutionGenerator.Build(PlanFor(state)))))
            .UseParameters(serializer);
    }

    /// <summary>
    /// Every feature on its own, with what it requires: the files it changes or adds, and the notices
    /// it raises. One snapshot per feature makes a registry edit reviewable.
    /// </summary>
    [Test]
    [MethodDataSource(nameof(EachFeature))]
    public Task Feature(string id)
    {
        var baseline = State() with
        {
            // the learning persistence has no outbox
            Persistence = Persistence.NonDurable
        };
        var plan = PlanFor(WithFeatures(baseline, [id, .. Features.ById[id].Requires]));
        var without = SolutionGenerator.Build(PlanFor(baseline)).ToDictionary(_ => _.Path, _ => _.Text);
        var changed = Code(SolutionGenerator.Build(plan))
            .Where(_ => !without.TryGetValue(_.Path, out var text) || text != _.Text);

        return Verify(
                $"""
                 {Render(changed)}
                 ==== interactions

                 {Interactions(plan)}
                 """)
            .UseParameters(id);
    }

    public static IEnumerable<string> EachFeature() =>
        Features.All.Select(_ => _.Id);

    /// <summary>A selection the features step would not let through still generates, so the output step can show it.</summary>
    [Test]
    public async Task ConflictingSelectionStillGenerates()
    {
        var plan = PlanFor(WithFeatures(State(), "SendOnly", "Sagas"));
        await Assert.That(plan.Blocked).IsTrue();
        await Assert.That(SolutionGenerator.Build(plan)).IsNotEmpty();
    }

    // Keyed by name, so the test's display name is the name rather than the whole state.
    static Dictionary<string, Func<WizardState>> additions = new()
    {
        ["Serilog"] = () => Addition(Transport.RabbitMQ, Persistence.MongoDb, "Serilog"),
        // Files that refer to the sample messages, which then come along as placeholders.
        ["SagasAndValidation"] = () => Addition(Transport.AzureServiceBus, Persistence.CosmosDb, "Sagas", "Outbox", "FluentValidation"),
        // A feature that brings a connection string and a container of its own.
        ["AttachmentsSql"] = () => Addition(Transport.RabbitMQ, Persistence.PostgreSql, "AttachmentsSql"),
        // Something the endpoint may already have is a warning rather than a conflict.
        ["AuditFilterAlone"] = () => Addition(Transport.SqlServer, Persistence.SqlServer, "AuditFilter", "Heartbeats")
    };

    public static IEnumerable<string> Additions() =>
        additions.Keys;

    /// <summary>Every file the add flow downloads, with the guide and AI instructions among them.</summary>
    [Test]
    [MethodDataSource(nameof(Additions))]
    public Task Addition(string name)
    {
        var plan = PlanFor(additions[name]());
        return Verify(
                $"""
                 ==== interactions

                 {Interactions(plan)}

                 {Render(SolutionGenerator.Build(plan))}
                 """)
            .UseParameters(name);
    }

    [Test]
    public async Task ZipHasTheSolutionUnderOneFolder()
    {
        var plan = PlanFor(State());
        var files = SolutionGenerator.Build(plan);
        var zip = ZipBuilder.Build(plan.ZipRoot, files);

        await using var archive = new ZipArchive(new MemoryStream(zip));
        var entries = archive.Entries.Select(_ => _.FullName).ToList();
        await Assert.That(entries.Count).IsEqualTo(files.Count);
        await Assert.That(entries.All(_ => _.StartsWith("SampleEndpoint/", StringComparison.Ordinal))).IsTrue();

        // same input, same bytes
        await Assert.That(ZipBuilder.Build(plan.ZipRoot, files).SequenceEqual(zip)).IsTrue();
    }

    [Test]
    public async Task AdditionsZipUnderTheirOwnFolder()
    {
        var plan = PlanFor(Addition(Transport.Learning, Persistence.Learning, "Serilog"));
        await Assert.That(plan.ZipRoot).IsEqualTo("nservicebus-additions");
    }

    /// <summary>Nothing generated may carry a carriage return, a tab, or trailing whitespace, whatever the source files hold.</summary>
    [Test]
    public async Task GeneratedTextIsClean()
    {
        foreach (var state in new[] {State(), Everything(), additions["SagasAndValidation"]()})
        {
            foreach (var file in SolutionGenerator.Build(PlanFor(state)))
            {
                await Assert.That(file.Text).DoesNotContain("\r").Because(file.Path);
                await Assert.That(file.Text).DoesNotContain("\t").Because(file.Path);
                await Assert.That(file.Text.Split('\n').Any(_ => _.EndsWith(' '))).IsFalse().Because(file.Path);
                await Assert.That(file.Text).EndsWith("\n").Because(file.Path);
                await Assert.That(Regex.IsMatch(file.Text, @"\{[A-Z][A-Za-z]+\}(?<!\{OrderId\}|\{Product\}|\{Name\}|\{Length\}|\{THandler\})")).IsFalse().Because($"{file.Path} has an unreplaced token");
            }
        }
    }

    static string Interactions(Plan plan) =>
        string.Join("\n\n", plan.Interactions.Select(_ => $"{_.Severity} {_.RuleId} [{string.Join(", ", _.Involved)}]\n{_.Message}"));

    /// <summary>The guide and the AI instructions have snapshots of their own.</summary>
    static IEnumerable<GeneratedFile> Code(IEnumerable<GeneratedFile> files) =>
        files.Where(_ => !_.Path.EndsWith(".md", StringComparison.Ordinal));

    /// <summary>The files a transport, a persistence or a serializer can change.</summary>
    static IEnumerable<GeneratedFile> Stack(IEnumerable<GeneratedFile> files) =>
        files.Where(_ => _.Path.EndsWith("Program.cs", StringComparison.Ordinal) ||
                         _.Path.EndsWith("appsettings.json", StringComparison.Ordinal) ||
                         _.Path is "Directory.Packages.props" or "docker-compose.yml");

    static string Render(IEnumerable<GeneratedFile> files)
    {
        var builder = new StringBuilder();
        foreach (var file in files)
        {
            builder.Append($"==== {file.Path}\n\n");
            builder.Append(file.Text.TrimEnd('\n'));
            builder.Append("\n\n");
        }

        return builder.ToString();
    }
}
