/// <summary>
/// The answers the wizard remembers between visits, kept in localStorage through the
/// <c>nserviceBusWizard.storage*</c> helpers in <c>wwwroot/js/interop.js</c>. What to keep, and when a
/// kept value applies, is decided by <see cref="BrowserMemory"/>; this only reads and writes.
/// </summary>
public sealed class BrowserStorage(IJSRuntime js)
{
    public async Task<Remembered> ReadAsync() =>
        new(
            await GetAsync(BrowserMemory.TransportKey),
            await GetAsync(BrowserMemory.PersistenceKey));

    /// <summary>A null value is left alone.</summary>
    public async Task WriteAsync(Remembered remembered)
    {
        await SetAsync(BrowserMemory.TransportKey, remembered.Transport);
        await SetAsync(BrowserMemory.PersistenceKey, remembered.Persistence);
    }

    public async Task ForgetAsync()
    {
        foreach (var key in BrowserMemory.Keys)
        {
            await js.InvokeVoidAsync("nserviceBusWizard.storageRemove", key);
        }
    }

    ValueTask<string?> GetAsync(string key) =>
        js.InvokeAsync<string?>("nserviceBusWizard.storageGet", key);

    async Task SetAsync(string key, string? value)
    {
        if (value == null)
        {
            return;
        }

        await js.InvokeVoidAsync("nserviceBusWizard.storageSet", key, value);
    }
}
