/// <summary>
/// The package versions emitted into generated output. Baked from package-versions.json,
/// which a weekly workflow refreshes; the web app overlays live nuget.org lookups with
/// <see cref="With"/>.
/// </summary>
public sealed class PackageVersions
{
    readonly IReadOnlyDictionary<string, string> versions;

    PackageVersions(
        Date updated,
        IReadOnlyDictionary<string, string> versions,
        IReadOnlyDictionary<string, string> pinned,
        bool live)
    {
        Updated = updated;
        this.versions = versions;
        Pinned = pinned;
        Live = live;
    }

    /// <summary>When the baked versions were last refreshed.</summary>
    public Date Updated { get; }

    /// <summary>
    /// Packages held at a version that is not the newest stable one, with the reason. Neither the
    /// refresh nor the live lookup moves them.
    /// </summary>
    public IReadOnlyDictionary<string, string> Pinned { get; }

    /// <summary>Whether the versions were checked against nuget.org just now, rather than baked.</summary>
    public bool Live { get; }

    public IEnumerable<string> Ids => versions.Keys;

    public static PackageVersions Baked { get; } = Load();

    public bool IsPinned(string packageId) =>
        Pinned.ContainsKey(packageId);

    public string this[string packageId]
    {
        get
        {
            if (versions.TryGetValue(packageId, out var version))
            {
                return version;
            }

            throw new($"package-versions.json has no version for '{packageId}'.");
        }
    }

    /// <summary>
    /// A copy with some versions replaced by the ones found on nuget.org. A pinned package keeps its
    /// version whatever the lookup found.
    /// </summary>
    /// <param name="live">Whether every package the output names was checked, so the guide can say so.</param>
    public PackageVersions With(IReadOnlyDictionary<string, string> overrides, bool live = true)
    {
        var merged = new Dictionary<string, string>(versions, StringComparer.OrdinalIgnoreCase);
        foreach (var (id, version) in overrides)
        {
            if (!IsPinned(id))
            {
                merged[id] = version;
            }
        }

        return new(Updated, merged, Pinned, live);
    }

    /// <summary>For tests: a fixed set, so snapshots do not change every time the baked file is refreshed.</summary>
    public static PackageVersions Create(Date updated, IReadOnlyDictionary<string, string> versions) =>
        new(
            updated,
            new Dictionary<string, string>(versions, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            live: false);

    static PackageVersions Load()
    {
        using var stream = typeof(PackageVersions).Assembly.GetManifestResourceStream("package-versions.json")!;
        var file = JsonSerializer.Deserialize(stream, PackageVersionsJsonContext.Default.PackageVersionsFile)!;
        return new(
            Date.ParseExact(file.Updated, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            new Dictionary<string, string>(file.Packages, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(file.Pinned ?? [], StringComparer.OrdinalIgnoreCase),
            live: false);
    }
}

public sealed record PackageVersionsFile(
    string Updated,
    Dictionary<string, string> Packages,
    Dictionary<string, string>? Pinned);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PackageVersionsFile))]
partial class PackageVersionsJsonContext : JsonSerializerContext;
