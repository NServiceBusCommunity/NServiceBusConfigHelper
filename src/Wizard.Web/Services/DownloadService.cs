/// <summary>Hands bytes to the <c>nserviceBusWizard.downloadFile</c> JS helper, which saves them in the browser.</summary>
public sealed class DownloadService(IJSRuntime js)
{
    public async ValueTask DownloadAsync(string fileName, string contentType, byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var reference = new DotNetStreamReference(stream);
        await js.InvokeVoidAsync("nserviceBusWizard.downloadFile", fileName, contentType, reference);
    }

    public ValueTask DownloadTextAsync(string fileName, string text) =>
        DownloadAsync(fileName, "text/markdown", Encoding.UTF8.GetBytes(text));
}
