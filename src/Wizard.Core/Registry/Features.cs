/// <summary>
/// The feature catalogue. The entries are in <c>Features.Data.cs</c>, in the order they are offered,
/// written into a url, and configured in the generated <c>Program.cs</c>.
/// </summary>
public static partial class Features
{
    static Dictionary<string, FeatureDefinition>? byId;

    // Built on first use rather than in a field initializer: initializers in a partial class run in an
    // order that depends on which file the compiler reads first.
    public static IReadOnlyDictionary<string, FeatureDefinition> ById =>
        byId ??= All.ToDictionary(_ => _.Id, StringComparer.Ordinal);

    public static bool Contains(string id) =>
        ById.ContainsKey(id);

    /// <summary>The selected features, in registry order.</summary>
    public static IReadOnlyList<FeatureDefinition> Selected(WizardState state) =>
        [.. All.Where(_ => state.Has(_.Id))];

    public const string SendOnly = "SendOnly";
    public const string Conventions = "Conventions";
    public const string Sagas = "Sagas";
    public const string Outbox = "Outbox";
    public const string DataAnnotations = "DataAnnotations";
    public const string AttachmentsSql = "AttachmentsSql";
    public const string AttachmentsFileShare = "AttachmentsFileShare";
}
