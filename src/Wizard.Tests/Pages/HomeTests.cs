public class HomeTests : WebTestContext
{
    [Test]
    public async Task RendersTheTwoEntryPoints()
    {
        var cut = Render<Home>();

        var hrefs = cut.FindAll("a.entry-card").Select(_ => _.GetAttribute("href"));
        await Assert.That(string.Join(' ', hrefs)).IsEqualTo("new add");
    }

    /// <summary>"Forget them" clears every key the wizard keeps.</summary>
    [Test]
    public async Task ForgetClearsEveryRememberedAnswer()
    {
        var cut = Render<Home>();
        await cut.Find("button.link-button").ClickAsync(new());

        var removed = JSInterop.Invocations
            .Where(_ => _.Identifier == "nserviceBusWizard.storageRemove")
            .Select(_ => (string) _.Arguments[0]!);
        await Assert.That(removed).IsEquivalentTo(BrowserMemory.Keys);
        await Assert.That(cut.Find("button.link-button").TextContent).IsEqualTo("Forgotten");
    }

    /// <summary>
    /// The markup only, as html so it is pretty printed. Verifying the component itself adds an info
    /// part (instance and node count) that carries nothing worth reviewing.
    /// </summary>
    [Test]
    public Task Markup() =>
        Verify(Render<Home>().Markup, "html");
}
