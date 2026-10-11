/// <summary>The add flow against bunit's fake NavigationManager, with what the browser remembers stubbed.</summary>
public class AddTests : WebTestContext
{
    NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    IRenderedComponent<Add> Open(string url, string? featureId = null)
    {
        Navigation.NavigateTo(url);
        return Render<Add>(_ => _.Add(add => add.FeatureId, featureId));
    }

    string CurrentUrl => Navigation.ToBaseRelativePath(Navigation.Uri);

    void Remember(string transport, string persistence)
    {
        JSInterop.Setup<string?>("nserviceBusWizard.storageGet", BrowserMemory.TransportKey).SetResult(transport);
        JSInterop.Setup<string?>("nserviceBusWizard.storageGet", BrowserMemory.PersistenceKey).SetResult(persistence);
    }

    [Test]
    public async Task StartsByAskingWhatTheEndpointRunsOn()
    {
        var page = Open("add");
        await Assert.That(CurrentUrl).IsEqualTo("add?step=transport");
        await Assert.That(page.FindAll(".breadcrumb li").Count).IsEqualTo(5);
    }

    /// <summary>A readme's deep link starts with its feature selected.</summary>
    [Test]
    public async Task ADeepLinkSelectsItsFeature()
    {
        Open("add", "Serilog");
        await Assert.That(CurrentUrl).IsEqualTo("add?step=transport&features=Serilog");
    }

    [Test]
    public async Task AnUnknownDeepLinkSelectsNothing()
    {
        Open("add", "Nope");
        await Assert.That(CurrentUrl).IsEqualTo("add?step=transport");
    }

    /// <summary>What the endpoint runs on is remembered, so a second visit lands on the features.</summary>
    [Test]
    public async Task RememberedAnswersAreRestored()
    {
        Remember("RabbitMQ", "MongoDb");
        var page = Open("add", "Serilog");
        await Assert.That(CurrentUrl).IsEqualTo("add?step=transport&transport=RabbitMQ&persistence=MongoDb&features=Serilog");
        await Assert.That(page.Find(".restored").TextContent).IsEqualTo("Restored from this browser.");
    }

    [Test]
    public async Task ChoosingATransportIsRemembered()
    {
        var page = Open("add");
        await page.Find("#transport-SqlServer").ClickAsync(new());

        var written = JSInterop.Invocations
            .Where(_ => _.Identifier == "nserviceBusWizard.storageSet")
            .Select(_ => $"{_.Arguments[0]}={_.Arguments[1]}");
        await Assert.That(written).IsEquivalentTo([$"{BrowserMemory.TransportKey}=SqlServer"]);
    }

    /// <summary>Adding nothing is not an addition.</summary>
    [Test]
    public async Task NextNeedsAFeature()
    {
        var page = Open("add?step=features&transport=Learning&persistence=NonDurable");
        await Assert.That(page.Find("button.primary").HasAttribute("disabled")).IsTrue();
        await Assert.That(page.FindAll("button.skip")).IsEmpty();

        await page.Find(".feature-card[data-id=Outbox] input").ChangeAsync(new() {Value = true});
        await Assert.That(page.Find("button.primary").HasAttribute("disabled")).IsFalse();
    }

    /// <summary>The endpoint may already have what a feature configures, so it is a warning and Next stays enabled.</summary>
    [Test]
    public async Task ARequiredFeatureIsOnlyAWarning()
    {
        var page = Open("add?step=features&transport=Learning&persistence=NonDurable&features=AuditFilter");
        await Assert.That(page.Find(".interaction-notice.warning[data-rule=needs-auditing]").TextContent).Contains("If the endpoint does not have it yet");
        await Assert.That(page.Find("button.primary").HasAttribute("disabled")).IsFalse();
        await Assert.That(page.FindAll(".feature-card.conflict")).IsEmpty();
        await Assert.That(page.FindAll(".conflict-count")).IsEmpty();
    }

    /// <summary>The download is the changes to merge, and says so.</summary>
    [Test]
    public async Task OutputIsTheChangesToMerge()
    {
        var page = Open("add?step=output&transport=RabbitMQ&persistence=MongoDb&features=Sagas,Serilog");
        await Assert.That(page.Find("button.download-zip").TextContent).IsEqualTo("Download changes (.zip)");
        await Assert.That(page.FindAll("#solutionName")).IsEmpty();

        await page.Find("#tab-Files").ClickAsync(new());
        var files = page.FindAll(".file-link").Select(_ => _.TextContent).ToList();
        await Assert.That(page.Find(".file-root").TextContent).IsEqualTo("nservicebus-additions/");
        await Assert.That(files).Contains(AdditionGenerator.ProgramFragment);
        await Assert.That(files).Contains("Sagas/OrderSaga.cs");
        await Assert.That(files).Contains("Samples/Messages/OrderPlaced.cs");
    }
}
