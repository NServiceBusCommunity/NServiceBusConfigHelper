/// <summary>
/// One Verify.Playwright snapshot (PNG + HTML) per wizard screen, driven through the real WASM runtime.
/// The wizard bundles its own fonts, so layout is identical on every OS and the PNG baselines hold
/// cross-platform; SSIM with a lenient threshold (see ModuleInitializer) absorbs the remaining
/// rasterization differences. The HTML target pins exact markup.
/// </summary>
[ParallelLimiter<BrowserParallelLimit>]
public class ScreenSnapshotTests
{
    static PublishedWizard wizard => PublishedWizard.Shared;

    const string stack = "host=Worker&transport=RabbitMQ&persistence=PostgreSql&serializer=SystemJson&tests=XunitV3";
    const string output = $"/new?step=output&{stack}&features=Sagas,Outbox,Serilog";

    public static async Task<IPage> Open(string path, string readySelector)
    {
        var page = await wizard.NewPage();
        await page.GotoAsync(wizard.Url(path));
        await page.WaitForSelectorAsync(readySelector);
        return page;
    }

    /// <summary>
    /// The bundled webfonts are declared <c>font-display: block</c>, so text stays unpainted until
    /// they load and a screenshot taken too early captures a blank or differently-measured page.
    /// </summary>
    static async Task VerifyScreen(IPage page, bool fullPage = true)
    {
        await page.EvaluateAsync("async () => { await document.fonts.ready; }");
        // The output step regenerates once the version check answers; capture after that, not before.
        await page.WaitForFunctionAsync("() => !document.querySelector('[data-versions=pending]')");
        await Verify(page)
            .PageScreenshotOptions(
                new()
                {
                    FullPage = fullPage
                });
    }

    [Test]
    public async Task Home()
    {
        var page = await Open("/", ".entry-cards");
        await VerifyScreen(page);
    }

    [Test]
    public async Task NewHosting()
    {
        var page = await Open("/new", "#host-Worker");
        await VerifyScreen(page);
    }

    [Test]
    public async Task NewTransport()
    {
        var page = await Open("/new?step=transport&host=Worker&transport=RabbitMQ", "#transport-RabbitMQ.selected");
        await VerifyScreen(page);
    }

    /// <summary>The persistence that pairs with the transport is first.</summary>
    [Test]
    public async Task NewPersistence()
    {
        var page = await Open("/new?step=persistence&host=Worker&transport=SqlServer", "#persistence-SqlServer");
        await VerifyScreen(page);
    }

    [Test]
    public async Task NewSerializer()
    {
        var page = await Open("/new?step=serializer&host=Worker&transport=RabbitMQ&persistence=PostgreSql", "#serializer-SystemJson");
        await VerifyScreen(page);
    }

    [Test]
    public async Task NewTests()
    {
        var page = await Open("/new?step=tests&host=Worker&transport=RabbitMQ&persistence=PostgreSql&serializer=SystemJson", "#tests-XunitV3");
        await VerifyScreen(page);
    }

    [Test]
    public async Task NewFeatures()
    {
        var page = await Open($"/new?step=features&{stack}", ".feature-card[data-id=Sagas]");
        await VerifyScreen(page);
    }

    /// <summary>A selection, with the notices it raises above the cards.</summary>
    [Test]
    public async Task NewFeaturesWithNotices()
    {
        var page = await Open(
            $"/new?step=features&{stack}&features=Sagas,Outbox,Heartbeats,Serilog",
            ".interaction-notice[data-rule=community-backing]");
        await VerifyScreen(page, fullPage: false);
    }

    /// <summary>A conflict, which is what stops the step being left.</summary>
    [Test]
    public async Task NewFeaturesConflict()
    {
        var page = await Open(
            $"/new?step=features&{stack}&features=SendOnly,Sagas",
            ".interaction-notice.conflict");
        await VerifyScreen(page, fullPage: false);
    }

    /// <summary>A feature the persistence rules out is greyed out.</summary>
    [Test]
    public async Task NewFeaturesUnavailable()
    {
        var page = await Open(
            "/new?step=features&host=Worker&transport=Learning&persistence=Learning&serializer=SystemJson&tests=None",
            ".feature-card.unavailable[data-id=Outbox]");
        await VerifyScreen(page, fullPage: false);
    }

    [Test]
    public async Task NewOptions()
    {
        var page = await Open(
            $"/new?step=options&{stack}&features=Auditing,AuditFilter",
            ".choice[data-choice=audit-filter-default]");
        await VerifyScreen(page);
    }

    // The guide is long; the viewport shows the output step's controls and the start of the guide.
    [Test]
    public async Task NewOutputGuide()
    {
        var page = await Open(output, ".markdown h1");
        await VerifyScreen(page, fullPage: false);
    }

    [Test]
    public async Task NewOutputFiles()
    {
        var page = await Open(output, ".markdown h1");
        await page.ClickAsync("#tab-Files");
        await page.ClickAsync(".file-link >> text=src/SampleEndpoint/Program.cs");
        await page.WaitForSelectorAsync(".code-box-title >> text=Program.cs");
        await VerifyScreen(page);
    }

    [Test]
    public async Task NewOutputAi()
    {
        var page = await Open(output, ".markdown h1");
        await page.ClickAsync("#tab-AI");
        await page.WaitForSelectorAsync("pre.ai-markdown");
        await VerifyScreen(page, fullPage: false);
    }

    [Test]
    public async Task AddFeatures()
    {
        var page = await Open(
            "/add?step=features&transport=RabbitMQ&persistence=MongoDb&features=AuditFilter",
            ".interaction-notice[data-rule=needs-auditing]");
        await VerifyScreen(page, fullPage: false);
    }

    [Test]
    public async Task AddOutputGuide()
    {
        var page = await Open("/add?step=output&transport=RabbitMQ&persistence=MongoDb&features=Sagas,Serilog", ".markdown h1");
        await VerifyScreen(page, fullPage: false);
    }

    /// <summary>Below 768px the rail becomes a strip of chosen values above the step.</summary>
    [Test]
    public async Task NewNarrow()
    {
        var page = await Open("/new?step=persistence&host=Worker&transport=RabbitMQ", "#persistence-PostgreSql");
        await page.SetViewportSizeAsync(375, 812);
        await VerifyScreen(page);
    }
}
