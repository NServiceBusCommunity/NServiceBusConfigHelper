/// <summary>
/// Rewrites package-versions.json with the newest stable version of every package. Pure: the
/// version lists are passed in, so the rules are tested without a network. Pinned packages are left
/// alone, and the updated date only moves when a version does, so a week with nothing new makes no
/// pull request.
/// </summary>
public static class VersionRefresh
{
    static JsonSerializerOptions writeOptions = new()
    {
        WriteIndented = true,
        // the pinned reasons are prose, and "requires Expecto < 10" should read as written
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <param name="versionLists">Package id to the versions nuget.org lists; a missing id was not answered.</param>
    public static RefreshResult Refresh(string json, IReadOnlyDictionary<string, IReadOnlyList<string>> versionLists, Date today)
    {
        var document = JsonNode.Parse(json)!.AsObject();
        var packages = document["packages"]!.AsObject();
        var pinned = document["pinned"]?.AsObject();

        var changes = new List<(string, string, string)>();
        var unanswered = new List<string>();
        var updated = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, node) in packages)
        {
            var current = node!.GetValue<string>();
            var newest = current;
            if (pinned?.ContainsKey(id) != true)
            {
                if (versionLists.TryGetValue(id, out var versions) &&
                    StableVersion.Newest(versions) is { } found)
                {
                    newest = found;
                }
                else
                {
                    unanswered.Add(id);
                }
            }

            if (newest != current)
            {
                changes.Add((id, current, newest));
            }

            updated[id] = newest;
        }

        if (changes.Count == 0)
        {
            return new(json, changes, unanswered);
        }

        // Sorted case insensitively, the way the file has always been kept, so a refresh diff shows
        // only the versions that moved.
        var sorted = new JsonObject();
        foreach (var (id, version) in updated.OrderBy(_ => _.Key.ToLowerInvariant(), StringComparer.Ordinal))
        {
            sorted[id] = version;
        }

        var result = new JsonObject
        {
            ["updated"] = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["packages"] = sorted
        };
        if (pinned != null)
        {
            result["pinned"] = pinned.DeepClone();
        }

        // The indented writer uses the OS newline, and the repo keeps this file lf.
        var text = result.ToJsonString(writeOptions).ReplaceLineEndings("\n") + "\n";
        return new(text, changes, unanswered);
    }
}
