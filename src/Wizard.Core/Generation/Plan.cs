/// <summary>
/// The resolved view of a <see cref="WizardState"/> that every generator reads: defaults filled in,
/// the registry entries looked up, and every package paired with the version to emit. Building it is
/// pure; <paramref name="Today"/> is injected so output is reproducible.
/// </summary>
/// <param name="Tests">Null when no test project is wanted, and always when adding to an existing endpoint.</param>
public sealed record Plan(
    WizardState State,
    Hosting Hosting,
    TransportDefinition Transport,
    PersistenceDefinition Persistence,
    SerializerDefinition Serializer,
    TestFrameworkInfo? Tests,
    PackageVersions Versions,
    Date Today)
{
    /// <summary>The selected features, in registry order.</summary>
    public required IReadOnlyList<FeatureDefinition> Features { get; init; }

    /// <summary>Every rule that fires for the state, most severe first.</summary>
    public required IReadOnlyList<InteractionResult> Interactions { get; init; }

    /// <summary>Adding to an existing endpoint rather than generating a new one.</summary>
    public bool IsAddition => State.Flow == Flow.Add;

    public string SolutionName => State.SolutionName;
    public string EndpointProject => SolutionName;
    public string TestProject => $"{SolutionName}.Tests";

    /// <summary>The endpoint's name on the transport: its queue, and the prefix of what it creates.</summary>
    public string EndpointName => SolutionName;

    public string Namespace => SolutionName;
    public string DatabaseName => SolutionNames.Database(SolutionName);

    public bool Has(string featureId) =>
        State.Has(featureId);

    public bool SendOnly => Has(global::Features.SendOnly);

    /// <summary>An endpoint that is added to is taken to receive, unless send-only is what is being added.</summary>
    public bool Receives => !SendOnly;

    public bool Conventions => Has(global::Features.Conventions);

    /// <summary>Whether the endpoint creates its queues and tables on startup. Not asked when adding.</summary>
    public bool Installers => !IsAddition && State.Installers;

    public string MessagesNamespace => $"{Namespace}.Messages";

    /// <summary>
    /// With message conventions the namespace is what makes a class a command or an event, so the two
    /// kinds are kept apart. Without them one namespace holds both.
    /// </summary>
    public string CommandsNamespace
    {
        get
        {
            if (Conventions)
            {
                return $"{MessagesNamespace}.Commands";
            }

            return MessagesNamespace;
        }
    }

    public string EventsNamespace
    {
        get
        {
            if (Conventions)
            {
                return $"{MessagesNamespace}.Events";
            }

            return MessagesNamespace;
        }
    }

    public string CommandsFolder => Folder(CommandsNamespace);
    public string EventsFolder => Folder(EventsNamespace);

    string Folder(string messageNamespace) =>
        messageNamespace[(Namespace.Length + 1)..].Replace('.', '/');

    /// <summary>
    /// What <c>Program.cs</c> is assembled from, in the order it is configured. A send-only endpoint
    /// stores nothing, so it gets no persistence. When adding to an existing endpoint only the
    /// features are generated: the transport and the persistence are already there.
    /// </summary>
    public IReadOnlyList<ComponentDefinition> Components
    {
        get
        {
            if (IsAddition)
            {
                return [.. Features];
            }

            if (SendOnly)
            {
                return [Transport, Serializer, .. Features];
            }

            return [Transport, Persistence, Serializer, .. Features];
        }
    }

    /// <summary>Whether the endpoint configures a persistence at all.</summary>
    public bool UsesPersistence => !IsAddition && !SendOnly;

    /// <summary>Each connection string once, however many components name it.</summary>
    public IReadOnlyList<ConnectionDefinition> Connections =>
        [.. Components.SelectMany(_ => _.Connections).DistinctBy(_ => _.Name)];

    /// <summary>Each container once, however many components need it.</summary>
    public IReadOnlyList<InfrastructureDefinition> Infrastructure =>
        [.. Components.SelectMany(_ => _.Infrastructure).DistinctBy(_ => _.Id)];

    /// <summary>Every choice in force, chosen or defaulted.</summary>
    public IReadOnlyList<(ChoiceDefinition Choice, string Value)> ChoiceValues =>
        [.. Components.SelectMany(_ => _.Choices).Select(_ => (_, State.ChoiceValue(_)))];

    /// <summary>What an existing endpoint project needs added: the features' packages.</summary>
    public IReadOnlyList<string> AddedPackages =>
        [.. Components.SelectMany(_ => _.Packages).Distinct(StringComparer.Ordinal)];

    /// <summary>Packages the endpoint project references: NServiceBus first, then each component's.</summary>
    public IReadOnlyList<string> EndpointPackages =>
        [.. AddedPackages.Prepend("NServiceBus").Distinct(StringComparer.Ordinal)];

    /// <summary>Transitive packages pinned above what the components' dependencies ask for.</summary>
    public IReadOnlyList<string> TransitivePins =>
        [.. Components.SelectMany(_ => _.TransitivePins).Distinct(StringComparer.Ordinal)];

    public IReadOnlyList<string> TestPackages
    {
        get
        {
            if (Tests == null)
            {
                return [];
            }

            return [.. Tests.Packages, "NServiceBus.Testing"];
        }
    }

    /// <summary>
    /// Every package id the output names a version for: what the live lookup checks, and nothing
    /// else, so a plan with two features makes a handful of requests rather than a hundred.
    /// </summary>
    public IReadOnlyList<string> EmittedPackages
    {
        get
        {
            if (IsAddition)
            {
                return AddedPackages;
            }

            return [.. EndpointPackages.Concat(TestPackages).Concat(TransitivePins).Distinct(StringComparer.Ordinal)];
        }
    }

    public string Version(string packageId) => Versions[packageId];

    /// <summary>The root folder of the zip: the solution, or the changes to merge into one.</summary>
    public string ZipRoot
    {
        get
        {
            if (IsAddition)
            {
                return "nservicebus-additions";
            }

            return SolutionName;
        }
    }

    /// <summary>A selection the features step would not let through. It still generates, so the output can be looked at.</summary>
    public bool Blocked =>
        Interactions.Any(_ => _.Severity == Severity.Conflict);

    /// <summary>The url that reopens the wizard on this exact state's output.</summary>
    public string WizardUrl =>
        WizardStateUrl.ToAbsoluteUrl(State with {Step = FlowSteps.Output.Id});

    /// <summary>Where the versions came from, for the guide.</summary>
    public string VersionsNote
    {
        get
        {
            if (Versions.Live)
            {
                return "the newest stable release of each on nuget.org when this was generated";
            }

            var updated = Versions.Updated.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return $"the newest stable releases as of {updated}; nuget.org was not checked when this was generated";
        }
    }

    /// <summary>What <see cref="Template"/> replaces, longest token first so one cannot eat part of another.</summary>
    public IReadOnlyList<(string Token, string Value)> Tokens
    {
        get
        {
            var commandInterface = "";
            var eventInterface = "";
            if (!Conventions)
            {
                commandInterface = " :\n    ICommand";
                eventInterface = " :\n    IEvent";
            }

            var tokens = new List<(string, string)>
            {
                ("{EndpointName}", EndpointName),
                ("{Namespace}", Namespace),
                ("{Database}", DatabaseName),
                ("{database}", DatabaseName.ToLowerInvariant()),
                ("{CommandsFolder}", CommandsFolder),
                ("{EventsFolder}", EventsFolder),
                ("{Commands}", CommandsNamespace),
                ("{Events}", EventsNamespace),
                ("{ICommand}", commandInterface),
                ("{IEvent}", eventInterface),
                ("{UsingCommands}", $"using {CommandsNamespace};"),
                ("{UsingEvents}", $"using {EventsNamespace};"),
                ("{UsingMessages}", string.Join('\n', new[] {CommandsNamespace, EventsNamespace}.Distinct().Select(_ => $"using {_};")))
            };
            tokens.AddRange(ChoiceValues.Select(_ => ($"{{{_.Choice.Id}}}", _.Value)));
            return tokens;
        }
    }

    /// <summary>How to run the tests from the solution directory.</summary>
    public const string TestCommand = "dotnet test";

    /// <summary>
    /// Missing answers fall back to the choice that needs nothing installed, so output can be previewed
    /// from any step and every generator can assume a complete state.
    /// </summary>
    public static Plan Build(WizardState state, PackageVersions versions, Date today)
    {
        TestFrameworkInfo? tests = null;
        if (state.Flow == Flow.New)
        {
            tests = TestFrameworkInfo.For(state.TestFramework ?? TestFramework.XunitV3);
        }

        return new(
            state,
            state.Hosting ?? Hosting.Worker,
            Transports.For(state.Transport ?? global::Transport.Learning),
            Persistences.For(state.Persistence ?? global::Persistence.Learning),
            Serializers.For(state.Serializer ?? global::Serializer.SystemJson),
            tests,
            versions,
            today)
        {
            Features = global::Features.Selected(state),
            Interactions = InteractionRules.For(state)
        };
    }
}
