public enum Severity
{
    /// <summary>Worth knowing; nothing has to be done.</summary>
    Info,

    /// <summary>Something outside the generated code has to exist or be done for it to work.</summary>
    Warning,

    /// <summary>The selection cannot be generated as it stands. The features step cannot be left.</summary>
    Conflict
}

/// <summary>One rule that fired for a state.</summary>
/// <param name="RuleId">Stable, for tests and for the <c>data-rule</c> attribute.</param>
/// <param name="Involved">Display names of what the rule is about.</param>
/// <param name="Message">Markdown: a sentence or two.</param>
/// <param name="Stack">
/// About the transport or the persistence rather than a feature. The features step leaves these out;
/// the options step and the guide show everything.
/// </param>
public sealed record InteractionResult(
    string RuleId,
    Severity Severity,
    IReadOnlyList<string> Involved,
    string Message,
    bool Stack = false)
{
    /// <summary>
    /// For a conflict: feature id to what that feature's card says about it, such as "Conflicts with
    /// Send-only". The notice explains the whole conflict, but it sits at the end of a long list; the
    /// mark is on the card that was just clicked. It can name a feature that is not selected, when
    /// selecting it is what resolves the conflict.
    /// </summary>
    public IReadOnlyDictionary<string, string> Marks { get; init; } = noMarks;

    static IReadOnlyDictionary<string, string> noMarks = new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>
/// What follows from a combination of answers that no single one of them says: features that cannot
/// work together, things that have to exist outside the generated code, and what a pairing gives for
/// free. Evaluated on every change, so a notice appears the moment the combination does.
/// </summary>
public static class InteractionRules
{
    public const string ServiceControlDocs = "https://docs.particular.net/servicecontrol/";
    public const string CommunityBacking = "https://github.com/NServiceBusCommunity/Home#community-backed";

    /// <summary>Every rule that fires, most severe first, then by rule id so the order is stable.</summary>
    public static IReadOnlyList<InteractionResult> For(WizardState state) =>
    [
        .. Evaluate(state)
            .OrderByDescending(_ => _.Severity)
            .ThenBy(_ => _.RuleId, StringComparer.Ordinal)
    ];

    public static bool Blocked(WizardState state) =>
        Evaluate(state).Any(_ => _.Severity == Severity.Conflict);

    /// <summary>Feature id to everything its card says, across the conflicts among <paramref name="results"/>.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Marks(IEnumerable<InteractionResult> results) =>
        results
            .Where(_ => _.Severity == Severity.Conflict)
            .SelectMany(_ => _.Marks)
            .GroupBy(_ => _.Key, _ => _.Value, StringComparer.Ordinal)
            .ToDictionary(_ => _.Key, IReadOnlyList<string> (_) => [.. _.Distinct(StringComparer.Ordinal)], StringComparer.Ordinal);

    static IEnumerable<InteractionResult> Evaluate(WizardState state)
    {
        var selected = Features.Selected(state);
        var sendOnly = state.Has(Features.SendOnly);

        if (sendOnly)
        {
            var idle = selected.Where(_ => _.NeedsReceiving).ToList();
            if (idle.Count > 0)
            {
                var names = Names(idle);
                yield return new(
                    "send-only",
                    Severity.Conflict,
                    ["Send-only", .. names],
                    $"A send-only endpoint never receives a message, so {DisplayNames.Join(names)} would do nothing. Remove Send-only, or remove {Them(idle.Count)}.")
                {
                    Marks = idle
                        .ToDictionary(_ => _.Id, _ => "Conflicts with Send-only", StringComparer.Ordinal)
                        .Append(new(Features.SendOnly, $"Conflicts with {Few(names)}"))
                        .ToDictionary(StringComparer.Ordinal)
                };
            }
            else if (state is {Flow: Flow.New, Persistence: not null})
            {
                yield return new(
                    "send-only-persistence",
                    Severity.Info,
                    ["Send-only"],
                    "A send-only endpoint has no sagas, no outbox and no subscriptions, so it stores nothing: the generated endpoint configures no persistence.");
            }
        }

        foreach (var feature in selected)
        {
            foreach (var requiredId in feature.Requires.Where(_ => !state.Has(_)))
            {
                var required = Features.ById[requiredId];
                // An existing endpoint may already have what is required, so only a new one is stopped.
                if (state.Flow == Flow.New)
                {
                    yield return new(
                        $"needs-{requiredId.ToLowerInvariant()}",
                        Severity.Conflict,
                        [feature.DisplayName, required.DisplayName],
                        $"{feature.DisplayName} configures {required.DisplayName}, so {required.DisplayName} has to be selected too.")
                    {
                        Marks = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [feature.Id] = $"Needs {required.DisplayName}",
                            [requiredId] = $"Needed by {feature.DisplayName}"
                        }
                    };
                    continue;
                }

                yield return new(
                    $"needs-{requiredId.ToLowerInvariant()}",
                    Severity.Warning,
                    [feature.DisplayName, required.DisplayName],
                    $"{feature.DisplayName} configures {required.DisplayName}. If the endpoint does not have it yet, select it here as well.");
            }
        }

        if (state.Has(Features.AttachmentsSql) &&
            state.Has(Features.AttachmentsFileShare))
        {
            var sql = Features.ById[Features.AttachmentsSql];
            var fileShare = Features.ById[Features.AttachmentsFileShare];
            yield return new(
                "attachments-storage",
                Severity.Conflict,
                [sql.DisplayName, fileShare.DisplayName],
                "Both attachment packages define the same `Attachments()` extension methods, so a project that references both does not compile. Choose one place to keep attachments.")
            {
                Marks = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [sql.Id] = $"Conflicts with {fileShare.DisplayName}",
                    [fileShare.Id] = $"Conflicts with {sql.DisplayName}"
                }
            };
        }

        if (state.Has(Features.Outbox) &&
            state.Persistence is { } outboxPersistence &&
            Persistences.For(outboxPersistence).OutboxNote is { } outboxNote)
        {
            yield return new(
                "outbox-partition-key",
                Severity.Warning,
                ["Outbox", outboxPersistence.Name()],
                outboxNote);
        }

        var monitored = selected.Where(_ => _.NeedsServiceControl).ToList();
        if (monitored.Count > 0)
        {
            var names = Names(monitored);
            yield return new(
                "service-control",
                Severity.Warning,
                names,
                $"{DisplayNames.Join(names)} {Verb(monitored.Count, "sends", "send")} to ServiceControl's queues. " +
                $"Until [ServiceControl]({ServiceControlDocs}) has been installed and has created them, those messages have nowhere to go.");
        }

        var backed = selected.Where(_ => _.Community).ToList();
        if (backed.Count > 0)
        {
            var names = Names(backed);
            yield return new(
                "community-backing",
                Severity.Info,
                names,
                $"{DisplayNames.Join(names)} {Verb(backed.Count, "is an NServiceBus Community extension", "are NServiceBus Community extensions")}: " +
                $"MIT licensed, and developers who use {Them(backed.Count)} are expected to [become patrons]({CommunityBacking}).");
        }

        foreach (var result in StackRules(state))
        {
            yield return result;
        }
    }

    /// <summary>Rules about the transport and the persistence alone.</summary>
    static IEnumerable<InteractionResult> StackRules(WizardState state)
    {
        var development = new List<string>();
        if (state.Transport is { } transport &&
            Transports.For(transport).DevelopmentOnly)
        {
            development.Add($"{transport.Name()} transport");
        }

        // A send-only endpoint configures no persistence, so there is nothing to warn about.
        var storesData = state.Flow == Flow.Add || !state.Has(Features.SendOnly);
        if (storesData &&
            state.Persistence is { } persistence &&
            Persistences.For(persistence).DevelopmentOnly)
        {
            development.Add($"{persistence.Name()} persistence");
        }

        if (development.Count > 0)
        {
            yield return new(
                "development-only",
                Severity.Info,
                development,
                $"The {DisplayNames.Join(development)} {Verb(development.Count, "is", "are")} for development. Choose a production one before deploying.",
                Stack: true);
        }

        if (state is {Transport: Transport.SqlServer, Persistence: Persistence.SqlServer} &&
            storesData &&
            !state.Has(Features.Outbox))
        {
            yield return new(
                "shared-database",
                Severity.Info,
                ["SQL Server transport", "SQL Server persistence"],
                "The transport and the persistence are in one database, on one connection string. SQL persistence then uses the connection and transaction the transport receives the message on, " +
                "so the data and the messages a handler sends commit together without the outbox; see [combining SQL Server persistence and transport](https://docs.particular.net/persistence/sql/sqlserver-combining-persistence-wth-transport).",
                Stack: true);
        }
    }

    static IReadOnlyList<string> Names(IEnumerable<FeatureDefinition> features) =>
        [.. features.Select(_ => _.DisplayName)];

    /// <summary>The names when there are one or two; otherwise how many, since a card's badge has no room for a list.</summary>
    static string Few(IReadOnlyList<string> names)
    {
        if (names.Count > 2)
        {
            return $"{names.Count} features";
        }

        return DisplayNames.Join(names);
    }

    static string Them(int count) =>
        Verb(count, "it", "them");

    static string Verb(int count, string singular, string plural)
    {
        if (count == 1)
        {
            return singular;
        }

        return plural;
    }
}
