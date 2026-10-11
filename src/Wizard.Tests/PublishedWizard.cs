/// <summary>
/// Shared plumbing for browser-based tests: serves the published Blazor output
/// (bin/&lt;Configuration&gt;/blazor-publish) from an in-memory Kestrel host and provides a Chromium
/// instance, exercising the actual WASM runtime (which the bunit tests do not).
/// </summary>
public sealed class PublishedWizard : IAsyncDisposable
{
    readonly WebApplication app;
    readonly IPlaywright playwright;

    public IBrowser Browser { get; }
    public int Port { get; }

    static PublishedWizard? shared;

    /// <summary>
    /// One Kestrel host and one Chromium for the whole assembly. A per-class instance meant several cold
    /// WASM boots racing each other on a two-core CI agent, and the losers blew Playwright's 30s default
    /// before the runtime finished starting.
    /// </summary>
    public static PublishedWizard Shared =>
        shared ?? throw new("PublishedWizard has not been started.");

    [Before(HookType.Assembly)]
    public static async Task StartShared() => shared = await Start();

    [After(HookType.Assembly)]
    public static async Task StopShared()
    {
        if (shared != null)
        {
            await shared.DisposeAsync();
            shared = null;
        }
    }

    /// <summary>
    /// The one context every page opens in. <see cref="IBrowser.NewPageAsync"/> creates a fresh context
    /// per page, and a context is an isolated profile with its own HTTP cache, so each test would
    /// re-fetch and re-compile the WASM runtime from cold. Sharing it would share localStorage too, which
    /// the wizard reads on load, so every page in it gets a storage of its own; see
    /// <see cref="PageStorage"/>.
    /// </summary>
    readonly IBrowserContext context;

    /// <summary>
    /// Replaces localStorage, in each page of the shared context, with one that lives and dies with that
    /// page. Tests run in parallel against one context, so a real, shared storage would let one test's
    /// answers be restored into another's page. A test about remembering across visits uses
    /// <see cref="NewIsolatedPage"/> instead, where storage is real and belongs to that test alone.
    /// </summary>
    const string PageStorage =
        """
        (() => {
            const items = new Map();
            const storage = {
                getItem: key => items.has(key) ? items.get(key) : null,
                setItem: (key, value) => items.set(key, String(value)),
                removeItem: key => items.delete(key),
                clear: () => items.clear(),
                key: index => Array.from(items.keys())[index] ?? null,
                get length() { return items.size; }
            };
            Object.defineProperty(window, 'localStorage', { value: storage, configurable: true });
        })();
        """;

    PublishedWizard(WebApplication app, IPlaywright playwright, IBrowser browser, IBrowserContext context, int port)
    {
        this.app = app;
        this.playwright = playwright;
        this.context = context;
        Browser = browser;
        Port = port;
    }

    public string Url(string path = "/") => $"http://localhost:{Port}{path}";

    public async Task<IPage> NewPage()
    {
        var page = await context.NewPageAsync();
        await page.AddInitScriptAsync(PageStorage);
        return page;
    }

    /// <summary>
    /// A page in a context of its own, with real storage that no other test can see: for journeys about
    /// what the browser remembers between visits. Its runtime boots cold, which takes longer.
    /// </summary>
    public async Task<IPage> NewIsolatedPage()
    {
        var isolated = await Browser.NewContextAsync(ContextOptions);
        await isolated.Clock.SetFixedTimeAsync(FixedTime);
        await RouteNuGet(isolated);
        var page = await isolated.NewPageAsync();
        page.SetDefaultTimeout(120_000);
        return page;
    }

    // Fixed viewport and locale so screenshots are deterministic across machines: date and month
    // pickers render in the browser's locale.
    static BrowserNewContextOptions ContextOptions =>
        new()
        {
            Locale = "en-US",
            ViewportSize = new()
            {
                Width = 1280,
                Height = 900
            }
        };

    /// <summary>
    /// The output step asks nuget.org for the newest versions. The tests answer for it, so a
    /// package shipping does not change a screenshot, and nothing depends on the network.
    /// </summary>
    static Task RouteNuGet(IBrowserContext context) =>
        context.RouteAsync(
            "https://api.nuget.org/**",
            _ => _.FulfillAsync(
                new()
                {
                    Status = 200,
                    ContentType = "application/json",
                    Body = FakeNuGet.Json,
                    Headers = new Dictionary<string, string>
                    {
                        ["Access-Control-Allow-Origin"] = "*"
                    }
                }));

    /// <summary>What every page believes "now" is, so dates the wizard derives are the same every run.</summary>
    public static readonly DateTime FixedTime = new(2026, 9, 22, 10, 0, 0, DateTimeKind.Utc);

    public static async Task<PublishedWizard> Start()
    {
        var installExitCode = Program.Main(["install", "chromium"]);
        if (installExitCode != 0)
        {
            throw new($"Playwright Chromium install failed with exit code {installExitCode}.");
        }

        var port = GetAvailablePort();

        var testAssemblyDirectory = Path.GetDirectoryName(typeof(PublishedWizard).Assembly.Location)!;
        var wwwroot = Path.Combine(testAssemblyDirectory, "..", "blazor-publish", "wwwroot");

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://localhost:{port}");
        builder.Logging.ClearProviders();
        var app = builder.Build();

        var contentTypes = new FileExtensionContentTypeProvider
        {
            Mappings =
            {
                [".wasm"] = "application/wasm"
            }
        };
        var files = new PhysicalFileProvider(wwwroot);

        app.UseDefaultFiles(new DefaultFilesOptions {FileProvider = files});
        app.UseStaticFiles(
            new StaticFileOptions
            {
                FileProvider = files,
                ContentTypeProvider = contentTypes,
                ServeUnknownFileTypes = true
            });
        // The pattern-less MapFallbackToFile registers {*path:nonfile}, which treats a dotted last
        // segment (a deep link such as /add/Verify.X) as a file request and 404s it. GitHub Pages serves
        // 404.html (a copy of index.html) for any missing path, so the host matches that with an
        // unconstrained pattern. That pattern also matches every asset url, and the static file
        // middleware steps aside once routing has picked an endpoint, so routing has to run after the
        // static files rather than at the start of the pipeline.
        app.UseRouting();
        app.MapFallbackToFile(
            "{*path}",
            "index.html",
            new()
            {
                FileProvider = files
            });

        await app.StartAsync();

        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync();
        var context = await browser.NewContextAsync(ContextOptions);
        // Fixed on the context rather than per page: the wizard derives dates from today, and installing
        // the clock on a page that has not navigated yet fails, because the script it calls into is
        // injected on navigation.
        await context.Clock.SetFixedTimeAsync(FixedTime);
        await RouteNuGet(context);

        var wizard = new PublishedWizard(app, playwright, browser, context, port);
        await wizard.WarmUp();
        return wizard;
    }

    /// <summary>
    /// Boot the app once, serially, before any test runs. The first load downloads and initializes the
    /// WASM runtime; on a cold CI agent that alone can outlast Playwright's 30s default. After this the
    /// shared context's HTTP cache is warm.
    /// </summary>
    async Task WarmUp()
    {
        var page = await NewPage();
        await page.GotoAsync(Url());
        await page.WaitForSelectorAsync(
            ".entry-cards",
            new()
            {
                Timeout = 120_000
            });
        await page.CloseAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await context.CloseAsync();
        await Browser.CloseAsync();
        playwright.Dispose();
        await app.StopAsync();
        await app.DisposeAsync();
    }

    static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint) listener.LocalEndpoint).Port;
    }
}
