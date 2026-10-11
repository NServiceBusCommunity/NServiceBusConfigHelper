// Refreshes package-versions.json from nuget.org. Run by refresh-versions.yml:
//   dotnet run --project src/Wizard.VersionRefresh -- <package-versions.json> <summary.md>
// Writes the file only when a version moved. The summary is the pull request body. When running in
// GitHub Actions it also sets the step output changed=true|false.

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: Wizard.VersionRefresh <package-versions.json> <summary.md>");
    return 2;
}

var path = args[0];
var summaryPath = args[1];
var json = await File.ReadAllTextAsync(path);
var file = JsonSerializer.Deserialize<PackageVersionsDocument>(json, new JsonSerializerOptions {PropertyNameCaseInsensitive = true})!;

using var client = new HttpClient {Timeout = TimeSpan.FromSeconds(30)};
var lists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
using var gate = new SemaphoreSlim(8);
await Task.WhenAll(
    file.Packages.Keys.Select(async id =>
    {
        await gate.WaitAsync();
        try
        {
            if (await Fetch(client, id) is { } versions)
            {
                lock (lists)
                {
                    lists[id] = versions;
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }));

var result = VersionRefresh.Refresh(json, lists, Date.FromDateTime(DateTime.UtcNow));
if (result.Changed)
{
    await File.WriteAllTextAsync(path, result.Json);
}

await File.WriteAllTextAsync(summaryPath, result.Summary());
Console.WriteLine(result.Summary());

if (Environment.GetEnvironmentVariable("GITHUB_OUTPUT") is { Length: > 0 } output)
{
    await File.AppendAllTextAsync(output, $"changed={result.Changed.ToString().ToLowerInvariant()}\n");
}

// Every package unanswered means nuget.org itself is unreachable; better to fail the run than to open
// nothing and look healthy.
if (lists.Count == 0)
{
    return 1;
}

return 0;

// Two attempts, because one failed request in a hundred is normal and not worth a lost week.
static async Task<IReadOnlyList<string>?> Fetch(HttpClient client, string id)
{
    for (var attempt = 0; attempt < 2; attempt++)
    {
        try
        {
            using var response = await client.GetAsync(StableVersion.IndexUrl(id));
            if (!response.IsSuccessStatusCode)
            {
                continue;
            }

            var index = await response.Content.ReadFromJsonAsync(VersionIndexJsonContext.Default.VersionIndex);
            return index?.Versions;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
        }
    }

    return null;
}

sealed record PackageVersionsDocument(Dictionary<string, string> Packages);
