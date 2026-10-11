/// <summary>
/// The query string encoding of <see cref="WizardState"/>. Values stay readable, because shared links
/// are meant to be read. Defaults are omitted. Parsing is total: an unknown or malformed value is
/// dropped rather than throwing, so any url lands somewhere sensible.
/// </summary>
public static class WizardStateUrl
{
    public const string StepKey = "step";
    public const string HostingKey = "host";
    public const string TransportKey = "transport";
    public const string PersistenceKey = "persistence";
    public const string SerializerKey = "serializer";
    public const string TestsKey = "tests";
    public const string NameKey = "name";
    public const string FeaturesKey = "features";
    public const string ChoicesKey = "opt";
    public const string InstallersKey = "installers";

    public static string RoutePath(Flow flow) =>
        flow switch
        {
            Flow.New => "new",
            Flow.Add => "add",
            _ => throw new ArgumentOutOfRangeException(nameof(flow), flow, null)
        };

    /// <summary>Relative to the site base, e.g. <c>new?step=transport&amp;host=Worker</c>.</summary>
    public static string ToRelativeUrl(WizardState state)
    {
        var query = ToQuery(state);
        var path = RoutePath(state.Flow);
        if (query.Length == 0)
        {
            return path;
        }

        return $"{path}?{query}";
    }

    public static string ToAbsoluteUrl(WizardState state) =>
        $"{WizardLinks.Base}/{ToRelativeUrl(state)}";

    public static string ToQuery(WizardState state)
    {
        var pairs = new List<(string Key, string Value)>();

        void Add(string key, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                pairs.Add((key, value));
            }
        }

        Add(StepKey, state.Step);
        Add(HostingKey, state.Hosting?.ToString());
        Add(TransportKey, state.Transport?.ToString());
        Add(PersistenceKey, state.Persistence?.ToString());
        Add(SerializerKey, state.Serializer?.ToString());
        Add(TestsKey, state.TestFramework?.ToString());
        if (state.SolutionName != WizardState.DefaultSolutionName)
        {
            Add(NameKey, state.SolutionName);
        }

        // Registry order, not insertion order, so the same selection is always the same link.
        Add(FeaturesKey, string.Join(',', Features.Selected(state).Select(_ => _.Id)));
        Add(ChoicesKey, string.Join(',', state.Choices.OrderBy(_ => _.Key, StringComparer.Ordinal).Select(_ => $"{_.Key}:{_.Value}")));
        if (!state.Installers)
        {
            Add(InstallersKey, "false");
        }

        return string.Join('&', pairs.Select(_ => $"{_.Key}={Escape(_.Value)}"));
    }

    /// <param name="query">The query string, with or without the leading <c>?</c>.</param>
    public static WizardState Parse(Flow flow, string? query)
    {
        var values = ParseQuery(query);
        string? Get(string key) => values.GetValueOrDefault(key);

        var state = new WizardState
        {
            Flow = flow,
            Step = Get(StepKey) ?? "",
            Hosting = ParseEnum<Hosting>(Get(HostingKey)),
            Transport = ParseEnum<Transport>(Get(TransportKey)),
            Persistence = ParseEnum<Persistence>(Get(PersistenceKey)),
            Serializer = ParseEnum<Serializer>(Get(SerializerKey)),
            TestFramework = ParseEnum<TestFramework>(Get(TestsKey)),
            SolutionName = Get(NameKey) ?? WizardState.DefaultSolutionName,
            Installers = Get(InstallersKey) != "false",
            SelectedFeatures = new HashSet<string>(SplitList(Get(FeaturesKey)), StringComparer.Ordinal),
            Choices = ParseChoices(Get(ChoicesKey))
        };

        state.Normalize();
        return state;
    }

    /// <summary>
    /// Whether a query names a key at all. The browser's remembered values only fill in what a url
    /// leaves out, so a shared link always means the same thing.
    /// </summary>
    public static bool HasKey(string? query, string key) =>
        ParseQuery(query).ContainsKey(key);

    /// <summary>Only exact member names are accepted: numbers and case variants would make several urls mean one state.</summary>
    public static T? ParseEnum<T>(string? value)
        where T : struct, Enum
    {
        if (value != null &&
            Enum.GetNames<T>().Contains(value, StringComparer.Ordinal))
        {
            return Enum.Parse<T>(value);
        }

        return null;
    }

    /// <summary>
    /// Commas and colons separate the list and pair values, and both are legal unescaped in a query
    /// string, so they are put back: a shared link is meant to be read, and <c>%2C</c> is not.
    /// </summary>
    static string Escape(string value) =>
        Uri.EscapeDataString(value)
            .Replace("%2C", ",")
            .Replace("%3A", ":");

    /// <summary>A comma list. Empty entries are dropped; <see cref="WizardState.Normalize"/> drops unknown ids.</summary>
    static IEnumerable<string> SplitList(string? value)
    {
        if (value == null)
        {
            return [];
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal);
    }

    /// <summary>A comma list of <c>key:value</c>. <see cref="WizardState.Normalize"/> drops unknown pairs.</summary>
    static Dictionary<string, string> ParseChoices(string? value)
    {
        var choices = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in SplitList(value))
        {
            var separator = pair.IndexOf(':');
            if (separator > 0)
            {
                choices.TryAdd(pair[..separator], pair[(separator + 1)..]);
            }
        }

        return choices;
    }

    static Dictionary<string, string> ParseQuery(string? query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(query))
        {
            return values;
        }

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            // UnescapeDataString leaves a malformed escape as-is rather than throwing
            var key = Uri.UnescapeDataString(pair[..separator]);
            var value = Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' '));
            // first occurrence wins, so appending a duplicate key cannot change a shared link
            values.TryAdd(key, value);
        }

        return values;
    }
}
