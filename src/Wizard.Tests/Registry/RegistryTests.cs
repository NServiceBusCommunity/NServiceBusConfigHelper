/// <summary>Invariants the registry has to hold, which the generators and the url rely on.</summary>
public class RegistryTests
{
    static IEnumerable<ComponentDefinition> Components =>
    [
        .. Transports.All,
        .. Persistences.All,
        .. Serializers.All,
        .. Features.All
    ];

    [Test]
    public async Task FeatureIdsAreUniqueAndUrlSafe()
    {
        var duplicates = Features.All
            .GroupBy(_ => _.Id, StringComparer.Ordinal)
            .Where(_ => _.Count() > 1)
            .Select(_ => _.Key);
        await Assert.That(duplicates).IsEmpty();
        await Assert.That(Features.All.Where(_ => !Regex.IsMatch(_.Id, "^[A-Za-z]+$")).Select(_ => _.Id)).IsEmpty();
    }

    [Test]
    public async Task EveryEnumMemberHasADefinition()
    {
        await Assert.That(Transports.All.Select(_ => _.Id)).IsEquivalentTo(Enum.GetValues<Transport>());
        await Assert.That(Persistences.All.Select(_ => _.Id)).IsEquivalentTo(Enum.GetValues<Persistence>());
        await Assert.That(Serializers.All.Select(_ => _.Id)).IsEquivalentTo(Enum.GetValues<Serializer>());
    }

    /// <summary>The whole feature registry as text, so any change to an entry is reviewed as a diff.</summary>
    [Test]
    public Task Registry() =>
        Verify(
            Features.All.Select(
                _ => new
                {
                    _.Id,
                    _.DisplayName,
                    Category = _.Category.ToString(),
                    _.Community,
                    _.Packages,
                    _.Usings,
                    _.Requires,
                    _.NeedsReceiving,
                    _.NeedsOutboxStorage,
                    _.NeedsServiceControl,
                    _.UsesSamples,
                    Choices = _.Choices.Select(choice => choice.Id),
                    Connections = _.Connections.Select(connection => connection.Name),
                    Files = _.Files.Select(file => file.Path),
                    _.DocsUrl
                }));

    /// <summary>Every package a definition names needs a version, or generating with it throws.</summary>
    [Test]
    public async Task EveryPackageHasAVersion()
    {
        var packages = Components.SelectMany(_ => _.Packages)
            .Concat(Enum.GetValues<TestFramework>().Select(TestFrameworkInfo.For).OfType<TestFrameworkInfo>().SelectMany(_ => _.Packages))
            .Append("NServiceBus")
            .Append("NServiceBus.Testing")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var baked = PackageVersions.Baked.Ids.ToHashSet(StringComparer.OrdinalIgnoreCase);

        await Assert.That(packages.Where(_ => !baked.Contains(_))).IsEmpty();
        // and nothing is refreshed every week that no definition uses
        await Assert.That(baked.Where(_ => !packages.Contains(_, StringComparer.OrdinalIgnoreCase))).IsEmpty();
    }

    [Test]
    public async Task RequiredFeaturesExistAndComeFirst()
    {
        var order = Features.All.Select(_ => _.Id).ToList();
        foreach (var feature in Features.All)
        {
            foreach (var required in feature.Requires)
            {
                await Assert.That(Features.Contains(required)).IsTrue().Because($"{feature.Id} requires {required}");
                // Program.cs configures features in registry order
                await Assert.That(order.IndexOf(required)).IsLessThan(order.IndexOf(feature.Id));
            }
        }
    }

    [Test]
    public async Task ChoicesAreWellFormed()
    {
        var choices = Components.SelectMany(_ => _.Choices).ToList();
        await Assert.That(choices.GroupBy(_ => _.Id).Where(_ => _.Count() > 1).Select(_ => _.Key)).IsEmpty();
        foreach (var choice in choices)
        {
            await Assert.That(choice.Options.Select(_ => _.Value)).Contains(choice.Default);
            // a value travels in the url as id:value, in a comma list
            await Assert.That(choice.Options.Any(_ => _.Value.Contains(':') || _.Value.Contains(','))).IsFalse();
        }

        // every choice is used by the code of the component that raises it
        foreach (var component in Components)
        {
            foreach (var choice in component.Choices)
            {
                await Assert.That(component.Code).Contains($"{{{choice.Id}}}");
            }
        }
    }

    /// <summary>Code refers to a connection string through its variable, so the two have to agree.</summary>
    [Test]
    public async Task ConnectionVariablesAreUsedByTheCode()
    {
        foreach (var component in Components)
        {
            foreach (var connection in component.Connections)
            {
                await Assert.That(component.Code).Contains(connection.Variable).Because(component.DisplayName);
            }
        }
    }

    [Test]
    public async Task DescriptionsAreSentences()
    {
        foreach (var component in Components)
        {
            await Assert.That(component.Description).EndsWith(".").Because(component.DisplayName);
            await Assert.That(component.DocsUrl).StartsWith("https://").Because(component.DisplayName);
            foreach (var note in component.Notes)
            {
                await Assert.That(note).EndsWith(".").Because(component.DisplayName);
            }
        }
    }

    [Test]
    public async Task CommunityFeaturesLinkToTheCommunity()
    {
        foreach (var feature in Features.All)
        {
            var linked = feature.DocsUrl.StartsWith("https://github.com/NServiceBusCommunity/", StringComparison.Ordinal);
            await Assert.That(linked).IsEqualTo(feature.Community).Because(feature.Id);
        }
    }

    /// <summary>Every rule that can fire, against the selection that fires it.</summary>
    [Test]
    public Task Rules()
    {
        var states = new Dictionary<string, WizardState>
        {
            ["send-only with receiving features"] = GeneratorTests.WithFeatures(GeneratorTests.State(), "SendOnly", "Sagas", "Auditing"),
            ["send-only alone"] = GeneratorTests.WithFeatures(GeneratorTests.State(), "SendOnly"),
            ["requirements missing, new"] = GeneratorTests.WithFeatures(GeneratorTests.State(), "AuditFilter", "SagaAudit"),
            ["requirements missing, add"] = GeneratorTests.Addition(Transport.RabbitMQ, Persistence.MongoDb, "AuditFilter"),
            ["both attachment stores"] = GeneratorTests.WithFeatures(GeneratorTests.State(), "AttachmentsSql", "AttachmentsFileShare"),
            ["outbox on cosmos"] = GeneratorTests.WithFeatures(
                GeneratorTests.State() with
                {
                    Transport = Transport.AzureServiceBus,
                    Persistence = Persistence.CosmosDb
                },
                "Outbox"),
            ["one sql server database"] = GeneratorTests.State() with
            {
                Transport = Transport.SqlServer,
                Persistence = Persistence.SqlServer
            },
            ["everything"] = GeneratorTests.Everything()
        };
        return Verify(
            states.ToDictionary(
                _ => _.Key,
                _ => InteractionRules.For(_.Value).Select(result => $"{result.Severity} {result.RuleId} [{string.Join(", ", result.Involved)}] {result.Message}{Marks(result)}")));
    }

    static string Marks(InteractionResult result)
    {
        if (result.Marks.Count == 0)
        {
            return "";
        }

        return $" Cards: {string.Join("; ", result.Marks.OrderBy(_ => _.Key, StringComparer.Ordinal).Select(_ => $"{_.Key} = {_.Value}"))}";
    }

    /// <summary>A conflict marks every card it is about, and only a conflict does.</summary>
    [Test]
    public async Task OnlyConflictsMarkCards()
    {
        var blocked = GeneratorTests.WithFeatures(GeneratorTests.State(), "SendOnly", "Sagas", "Auditing", "Recoverability", "AuditFilter", "Heartbeats");
        var results = InteractionRules.For(blocked);
        await Assert.That(results.Where(_ => _.Severity != Severity.Conflict).SelectMany(_ => _.Marks)).IsEmpty();
        await Assert.That(results.Where(_ => _.Severity == Severity.Conflict && _.Marks.Count == 0)).IsEmpty();

        var marks = InteractionRules.Marks(results);
        // a badge has no room for a list of four
        await Assert.That(marks["SendOnly"]).IsEquivalentTo(["Conflicts with 4 features"]);
        await Assert.That(marks["Sagas"]).IsEquivalentTo(["Conflicts with Send-only"]);
        await Assert.That(marks.ContainsKey("Heartbeats")).IsFalse();

        // when adding, a missing requirement is a warning, and marks nothing
        var added = GeneratorTests.Addition(Transport.RabbitMQ, Persistence.MongoDb, "AuditFilter");
        await Assert.That(InteractionRules.Marks(InteractionRules.For(added))).IsEmpty();
    }

    /// <summary>A feature two others need says so for each of them.</summary>
    [Test]
    public async Task ARequiredFeatureIsMarkedThoughNotSelected()
    {
        var state = GeneratorTests.WithFeatures(GeneratorTests.State(), "AuditFilter");
        var marks = InteractionRules.Marks(InteractionRules.For(state));
        await Assert.That(marks["AuditFilter"]).IsEquivalentTo(["Needs Auditing"]);
        await Assert.That(marks["Auditing"]).IsEquivalentTo(["Needed by Audit filter"]);
    }

    /// <summary>The outbox is greyed out, with the reason, on a persistence that cannot store it.</summary>
    [Test]
    public async Task OutboxIsUnavailableWithoutStorage()
    {
        var state = GeneratorTests.WithFeatures(GeneratorTests.State(), "Outbox", "Sagas");
        await Assert.That(state.SelectedFeatures).IsEquivalentTo(["Sagas"]);
        await Assert.That(state.Unavailable("Outbox")).IsEqualTo("Learning persistence cannot store the outbox. Choose another persistence to use it.");
    }
}
