/// <summary>
/// Picks the newest stable version from a nuget.org version list. One rule, shared by the
/// weekly refresh and the browser's live lookup, so the two cannot pick differently. Stable means no
/// prerelease label; build metadata is ignored. Written here rather than taken from NuGet.Versioning,
/// which the browser would otherwise have to download.
/// </summary>
public static class StableVersion
{
    /// <returns>Null when the list has no stable version.</returns>
    public static string? Newest(IEnumerable<string> versions)
    {
        string? newest = null;
        var newestParts = Array.Empty<long>();
        foreach (var version in versions)
        {
            if (!TryParseStable(version, out var parts))
            {
                continue;
            }

            if (newest == null ||
                Compare(parts, newestParts) > 0)
            {
                newest = version;
                newestParts = parts;
            }
        }

        return newest;
    }

    /// <summary>Numeric parts of a version without a prerelease label, such as 1.2.3 or 1.2.3.4.</summary>
    public static bool TryParseStable(string version, out long[] parts)
    {
        parts = [];
        var core = version.Split('+')[0];
        if (core.Contains('-'))
        {
            return false;
        }

        var segments = core.Split('.');
        if (segments.Length is < 2 or > 4)
        {
            return false;
        }

        var parsed = new long[4];
        for (var index = 0; index < segments.Length; index++)
        {
            if (!long.TryParse(segments[index], NumberStyles.None, CultureInfo.InvariantCulture, out parsed[index]))
            {
                return false;
            }
        }

        parts = parsed;
        return true;
    }

    static int Compare(long[] left, long[] right)
    {
        for (var index = 0; index < 4; index++)
        {
            var comparison = left[index].CompareTo(right[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    /// <summary>The flat container index nuget.org serves a package's version list from, CORS enabled.</summary>
    public static string IndexUrl(string packageId) =>
        $"https://api.nuget.org/v3-flatcontainer/{packageId.ToLowerInvariant()}/index.json";
}

public sealed record VersionIndex(string[] Versions);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(VersionIndex))]
public partial class VersionIndexJsonContext : JsonSerializerContext;
