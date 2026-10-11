/// <summary>
/// Answers nuget.org's flat container requests with a fixed version list, so tests that
/// show package versions do not change whenever a real package ships. Every package's newest stable
/// version is <see cref="Newest"/>; the list also holds an older one and a newer prerelease, so picking
/// the right one is exercised too.
/// </summary>
public sealed class FakeNuGet : HttpMessageHandler
{
    public const string Newest = "1.1.0";

    public const string Json = """{"versions":["1.0.0","1.1.0","2.0.0-beta.1"]}""";

    /// <summary>Set to make every request fail, as it would offline.</summary>
    public bool Offline { get; set; }

    public List<string> Requested { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, Cancel cancel)
    {
        lock (Requested)
        {
            Requested.Add(request.RequestUri!.AbsolutePath);
        }

        if (Offline)
        {
            throw new HttpRequestException("offline");
        }

        return Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Json, Encoding.UTF8, "application/json")
            });
    }
}
