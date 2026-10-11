/// <summary>Display order on the features step.</summary>
public enum FeatureCategory
{
    Messaging,
    Reliability,
    Monitoring,
    Observability,
    Validation,
    Attachments
}

/// <summary>A file a feature adds to the endpoint project.</summary>
/// <param name="Path">Relative to the project, with forward slashes. A template, like <paramref name="Content"/>.</param>
/// <param name="Receiving">Only makes sense on an endpoint that receives, such as a handler; left out of a send-only endpoint.</param>
public sealed record FeatureFile(string Path, string Content, bool Receiving = false);

/// <summary>
/// One thing an endpoint can be configured to do: part of NServiceBus itself, a Particular package,
/// or an NServiceBus Community extension. Everything is data, so a feature is added by adding an
/// entry to <see cref="Features"/>; <c>RegistryTests</c> checks most of what is described here.
/// </summary>
public sealed record FeatureDefinition :
    ComponentDefinition
{
    /// <summary>The url value. Never rename one: links and deep links hold it.</summary>
    public required string Id { get; init; }

    public required FeatureCategory Category { get; init; }

    /// <summary>
    /// An NServiceBus Community extension, which asks its users to become patrons. The features step
    /// and the guide say so whenever one is selected.
    /// </summary>
    public bool Community { get; init; }

    /// <summary>Statements that run before the host builder exists, such as creating a logger.</summary>
    public string Startup { get; init; } = "";

    /// <summary>Statements on <c>builder</c>: service registrations and logging.</summary>
    public string Host { get; init; } = "";

    public IReadOnlyList<FeatureFile> Files { get; init; } = [];

    /// <summary>Does nothing unless the endpoint processes messages, so it conflicts with send-only.</summary>
    public bool NeedsReceiving { get; init; }

    /// <summary>Unavailable with a persistence that cannot store the outbox.</summary>
    public bool NeedsOutboxStorage { get; init; }

    /// <summary>Sends to ServiceControl's queues, which have to exist.</summary>
    public bool NeedsServiceControl { get; init; }

    /// <summary>Ids of the features this one configures, which have to be selected with it.</summary>
    public IReadOnlyList<string> Requires { get; init; } = [];

    /// <summary>
    /// The files refer to the sample messages and handler every new endpoint gets. When features are
    /// added to an existing endpoint those come along as placeholders.
    /// </summary>
    public bool UsesSamples { get; init; }
}

public static class FeatureCategories
{
    public static string Label(this FeatureCategory category) =>
        category switch
        {
            FeatureCategory.Messaging => "Messaging",
            FeatureCategory.Reliability => "Reliability",
            FeatureCategory.Monitoring => "Auditing and monitoring",
            FeatureCategory.Observability => "Logging and telemetry",
            FeatureCategory.Validation => "Validation",
            FeatureCategory.Attachments => "Attachments",
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
        };
}
