/// <summary>The new-endpoint flow against bunit's fake NavigationManager: url in, url out.</summary>
public class NewTests : WebTestContext
{
    const string stack = "host=Worker&transport=Learning&persistence=NonDurable&serializer=SystemJson&tests=XunitV3";

    NavigationManager Navigation => Services.GetRequiredService<NavigationManager>();

    IRenderedComponent<New> Open(string url)
    {
        Navigation.NavigateTo(url);
        return Render<New>();
    }

    string CurrentUrl => Navigation.ToBaseRelativePath(Navigation.Uri);

    [Test]
    public async Task StartsAtTheFirstStep()
    {
        var page = Open("new");
        await Assert.That(page.Find("section.step-body").GetAttribute("data-step")).IsEqualTo("host");
        await Assert.That(CurrentUrl).IsEqualTo("new?step=host");
    }

    [Test]
    public async Task RestoresStateFromTheUrl()
    {
        var page = Open("new?step=serializer&host=AspNetCore&transport=RabbitMQ&persistence=MongoDb");
        await Assert.That(page.Find("section.step-body").GetAttribute("data-step")).IsEqualTo("serializer");
        await Assert.That(page.Find("li.done button[data-step=transport] .step-value").TextContent).IsEqualTo("RabbitMQ");
    }

    [Test]
    public async Task UrlPastAnIncompleteStepIsCorrected()
    {
        Open("new?step=output&host=Worker");
        await Assert.That(CurrentUrl).IsEqualTo("new?step=transport&host=Worker");
    }

    /// <summary>The first answer on each step moves on; the steps are asked in order.</summary>
    [Test]
    public async Task EachFirstAnswerMovesOn()
    {
        var page = Open("new");
        await page.Find("#host-Worker").ClickAsync(new());
        await Assert.That(CurrentUrl).IsEqualTo("new?step=transport&host=Worker");

        await page.Find("#transport-RabbitMQ").ClickAsync(new());
        await Assert.That(CurrentUrl).IsEqualTo("new?step=persistence&host=Worker&transport=RabbitMQ");

        await page.Find("#persistence-PostgreSql").ClickAsync(new());
        await page.Find("#serializer-SystemJson").ClickAsync(new());
        await page.Find("#tests-NUnit").ClickAsync(new());
        await Assert.That(CurrentUrl).IsEqualTo("new?step=features&host=Worker&transport=RabbitMQ&persistence=PostgreSql&serializer=SystemJson&tests=NUnit");
    }

    [Test]
    public async Task ChangingAnAnswerStaysOnTheStep()
    {
        var page = Open("new?step=transport&host=Worker&transport=Learning");
        await page.Find("#transport-SqlServer").ClickAsync(new());
        await Assert.That(CurrentUrl).IsEqualTo("new?step=transport&host=Worker&transport=SqlServer");
    }

    /// <summary>The persistence in the same service as the transport is offered first, and says why.</summary>
    [Test]
    public async Task ThePersistenceThatPairsWithTheTransportComesFirst()
    {
        var page = Open("new?step=persistence&host=Worker&transport=AmazonSqs");
        var first = page.FindAll(".mode-card")[0];
        await Assert.That(first.Id).IsEqualTo("persistence-DynamoDb");
        await Assert.That(first.TextContent).Contains("Pairs with the Amazon SQS transport.");
    }

    [Test]
    public async Task NextIsGatedByTheCurrentStep()
    {
        var page = Open("new");
        await Assert.That(page.Find("button.primary").HasAttribute("disabled")).IsTrue();

        // Features are optional, so that step's Next is enabled from the start.
        page = Open($"new?step=features&{stack}");
        await Assert.That(page.Find("button.primary").HasAttribute("disabled")).IsFalse();
    }

    [Test]
    public async Task SkipOnFeaturesClearsTheSelection()
    {
        var page = Open($"new?step=features&{stack}&features=Sagas");
        await page.Find("button.skip").ClickAsync(new());
        await Assert.That(CurrentUrl).IsEqualTo($"new?step=options&{stack}");
    }

    [Test]
    public async Task OnlyTheFeaturesStepHasSkip()
    {
        var page = Open("new?step=host");
        await Assert.That(page.FindAll("button.skip")).IsEmpty();
    }

    /// <summary>Toggling a card writes the selection into the url, in registry order.</summary>
    [Test]
    public async Task SelectingAFeatureUpdatesTheUrl()
    {
        var page = Open($"new?step=features&{stack}");
        await page.Find(".feature-card[data-id=Serilog] input").ChangeAsync(new() {Value = true});
        await page.Find(".feature-card[data-id=Sagas] input").ChangeAsync(new() {Value = true});
        await Assert.That(CurrentUrl).EndsWith("&features=Sagas,Serilog");

        // Nothing selected is the default, so the url drops the key rather than saying so.
        await page.Find(".feature-card[data-id=Serilog] input").ChangeAsync(new() {Value = false});
        await page.Find(".feature-card[data-id=Sagas] input").ChangeAsync(new() {Value = false});
        await Assert.That(CurrentUrl).DoesNotContain("features=");
    }

    /// <summary>The outbox is greyed out on a persistence that cannot store it, says why, and is dropped from the url.</summary>
    [Test]
    public async Task TheOutboxIsUnavailableOnLearningPersistence()
    {
        var page = Open("new?step=features&host=Worker&transport=Learning&persistence=Learning&serializer=Xml&tests=None&features=Outbox,Sagas");
        await Assert.That(CurrentUrl).EndsWith("&features=Sagas");

        var card = page.Find(".feature-card[data-id=Outbox]");
        await Assert.That(card.ClassList.Contains("unavailable")).IsTrue();
        await Assert.That(card.GetAttribute("title")).StartsWith("Learning persistence cannot store the outbox.");
        await Assert.That(page.Find(".feature-card[data-id=Outbox] input").HasAttribute("disabled")).IsTrue();
    }

    /// <summary>A send-only endpoint with a saga cannot be generated, so the step cannot be left.</summary>
    [Test]
    public async Task ConflictingFeaturesBlockNext()
    {
        var page = Open($"new?step=features&{stack}&features=SendOnly,Sagas");
        await Assert.That(page.Find("button.primary").HasAttribute("disabled")).IsTrue();
        await Assert.That(page.Find(".interaction-notice[data-rule=send-only]").TextContent).Contains("never receives a message");

        await page.Find(".feature-card[data-id=SendOnly] input").ChangeAsync(new() {Value = false});
        await Assert.That(page.Find("button.primary").HasAttribute("disabled")).IsFalse();
    }

    /// <summary>
    /// The notice is at the end of a long list, so the conflict also shows where it was caused: on
    /// both cards, and as a count beside the filter that jumps to the notices.
    /// </summary>
    [Test]
    public async Task AConflictIsMarkedOnTheCardsInvolved()
    {
        var page = Open($"new?step=features&{stack}&features=SendOnly,Sagas");
        await Assert.That(page.Find(".feature-card.conflict[data-id=SendOnly] .badge.conflict").TextContent).IsEqualTo("Conflicts with Sagas");
        await Assert.That(page.Find(".feature-card.conflict[data-id=Sagas] .badge.conflict").TextContent).IsEqualTo("Conflicts with Send-only");
        await Assert.That(page.FindAll(".feature-card.conflict").Count).IsEqualTo(2);
        await Assert.That(page.Find(".conflict-count").TextContent).IsEqualTo("1 has to change");

        await page.Find(".conflict-count").ClickAsync(new());
        var scrolled = JSInterop.Invocations.Single(_ => _.Identifier == "nserviceBusWizard.scrollIntoView");
        await Assert.That(page.Find($"#{scrolled.Arguments[0]}").ClassList.Contains("interactions")).IsTrue();

        await page.Find(".feature-card[data-id=Sagas] input").ChangeAsync(new() {Value = false});
        await Assert.That(page.FindAll(".feature-card.conflict")).IsEmpty();
        await Assert.That(page.FindAll(".conflict-count")).IsEmpty();
    }

    /// <summary>What is not a conflict stays a notice only: the count and the red cards mean Next is held back.</summary>
    [Test]
    public async Task WarningsDoNotMarkCards()
    {
        var page = Open($"new?step=features&{stack}&features=Heartbeats,Serilog");
        await Assert.That(page.FindAll(".interaction-notice").Count).IsEqualTo(2);
        await Assert.That(page.FindAll(".feature-card.conflict")).IsEmpty();
        await Assert.That(page.FindAll(".conflict-count")).IsEmpty();
    }

    /// <summary>The card to select to resolve a conflict stays in view whatever the filter says.</summary>
    [Test]
    public async Task TheFilterKeepsMarkedCards()
    {
        var page = Open($"new?step=features&{stack}&features=AuditFilter");
        await page.Find("#featureFilter").InputAsync(new() {Value = "serilog"});

        var shown = page.FindAll(".feature-card").Select(_ => _.GetAttribute("data-id")!);
        await Assert.That(shown).IsEquivalentTo(["Auditing", "AuditFilter", "Serilog"]);
    }

    /// <summary>A feature that configures another needs it selected.</summary>
    [Test]
    public async Task ARequiredFeatureBlocksNextUntilSelected()
    {
        var page = Open($"new?step=features&{stack}&features=AuditFilter");
        await Assert.That(page.Find("button.primary").HasAttribute("disabled")).IsTrue();
        // the card that resolves it is marked though it is not selected
        var needed = page.Find(".feature-card.conflict[data-id=Auditing]");
        await Assert.That(needed.ClassList.Contains("selected")).IsFalse();
        await Assert.That(needed.QuerySelector(".badge.conflict")!.TextContent).IsEqualTo("Needed by Audit filter");
        await Assert.That(page.Find(".feature-card[data-id=AuditFilter] .badge.conflict").TextContent).IsEqualTo("Needs Auditing");

        await page.Find(".feature-card[data-id=Auditing] input").ChangeAsync(new() {Value = true});
        await Assert.That(page.FindAll(".interaction-notice[data-rule=needs-auditing]")).IsEmpty();
        await Assert.That(page.Find("button.primary").HasAttribute("disabled")).IsFalse();
    }

    [Test]
    public async Task OptionsAreCarriedInTheUrl()
    {
        var page = Open("new?step=options&host=Worker&transport=RabbitMQ&persistence=NonDurable&serializer=SystemJson&tests=None");
        await page.Find("#installers-off").ClickAsync(new());
        await page.Find("fieldset[data-choice=rabbitmq-queue-type] input[value=Classic]").ChangeAsync(new());
        await Assert.That(CurrentUrl).EndsWith("&opt=rabbitmq-queue-type:Classic&installers=false");
        await Assert.That(page.Find("li.current").TextContent.Trim()).IsEqualTo("Options");
    }

    /// <summary>The options step says what the transport and the persistence imply, which the features step leaves out.</summary>
    [Test]
    public async Task StackNoticesAreOnTheOptionsStepOnly()
    {
        var features = Open($"new?step=features&{stack}");
        await Assert.That(features.FindAll(".interaction-notice[data-rule=development-only]")).IsEmpty();

        var options = Open($"new?step=options&{stack}");
        await Assert.That(options.Find(".interaction-notice[data-rule=development-only]").TextContent).Contains("Learning transport and Non-durable persistence are for development");
    }

    /// <summary>The output uses nuget.org's newest versions once they arrive.</summary>
    [Test]
    public async Task OutputUsesTheNewestVersionsOnNuGet()
    {
        var page = Open($"new?step=output&{stack}");
        await page.WaitForStateAsync(() => page.Find("[data-versions]").GetAttribute("data-versions") == "live");

        await Assert.That(page.Find(".versions").TextContent).IsEqualTo("Package versions are the newest stable releases on nuget.org.");
        await page.Find("#tab-Files").ClickAsync(new());
        await page.FindAll(".file-link").Single(_ => _.TextContent == "Directory.Packages.props").ClickAsync(new());
        await Assert.That(page.Find(".code-box-content").TextContent).Contains($"<PackageVersion Include=\"NServiceBus\" Version=\"{FakeNuGet.Newest}\" />");
    }

    /// <summary>Offline, the output stands with the baked versions, and says how old they are.</summary>
    [Test]
    public async Task OfflineKeepsTheBakedVersions()
    {
        NuGet.Offline = true;
        var page = Open($"new?step=output&{stack}");
        await page.WaitForStateAsync(() => page.Find("[data-versions]").GetAttribute("data-versions") == "baked");
        await Assert.That(page.Find(".versions").TextContent).StartsWith("nuget.org could not be reached");
    }

    [Test]
    public Task OutputMarkup()
    {
        var page = Open($"new?step=output&{stack}");
        return Verify(page.Find("section.step-body .output-actions").OuterHtml, "html");
    }

    [Test]
    public async Task RenamingTheEndpointRenamesTheFiles()
    {
        var page = Open($"new?step=output&{stack}");
        await page.Find("#solutionName").ChangeAsync(new() {Value = "Acme Sales!"});
        await Assert.That(CurrentUrl).Contains("&name=AcmeSales");

        await page.Find("#tab-Files").ClickAsync(new());
        var files = page.FindAll(".file-link").Select(_ => _.TextContent).ToList();
        await Assert.That(page.Find(".file-root").TextContent).IsEqualTo("AcmeSales/");
        await Assert.That(files).Contains("src/AcmeSales.Tests/AcmeSales.Tests.csproj");
    }
}
