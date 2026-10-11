using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.WebAssembly.Http;

/// <summary>
/// Checks nuget.org, from the browser, for the newest stable version of each package the output names
///. nuget.org's flat container allows cross-origin requests. The whole check has a time
/// budget and never blocks the output: whatever has not answered keeps its baked version. Answers are
/// kept for the session, so changing a choice does not ask again.
/// </summary>
public sealed class PackageVersionLookup(HttpClient client)
{
    static TimeSpan budget = TimeSpan.FromSeconds(5);

    Dictionary<string, string> found = new(StringComparer.OrdinalIgnoreCase);

    /// <returns>Package id to its newest stable version, for the ids that answered in time.</returns>
    public async Task<IReadOnlyDictionary<string, string>> NewestAsync(IEnumerable<string> packageIds)
    {
        var wanted = packageIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        using var cancel = new CancelSource(budget);
        await Task.WhenAll(wanted.Where(_ => !found.ContainsKey(_)).Select(_ => Fetch(_, cancel.Token)));

        return wanted
            .Where(found.ContainsKey)
            .ToDictionary(_ => _, _ => found[_], StringComparer.OrdinalIgnoreCase);
    }

    async Task Fetch(string packageId, Cancel cancel)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, StableVersion.IndexUrl(packageId));
            // A version published minutes ago should show up; the browser's cache would hide it.
            request.SetBrowserRequestCache(BrowserRequestCache.NoStore);
            using var response = await client.SendAsync(request, cancel);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            var index = await response.Content.ReadFromJsonAsync(VersionIndexJsonContext.Default.VersionIndex, cancel);
            if (index != null &&
                StableVersion.Newest(index.Versions) is { } newest)
            {
                lock (found)
                {
                    found[packageId] = newest;
                }
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or JsonException)
        {
            // Offline, blocked, or slower than the budget: the baked version stands.
        }
    }
}
