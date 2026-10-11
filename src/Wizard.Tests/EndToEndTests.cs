/// <summary>Journeys through the published app in Chromium.</summary>
[ParallelLimiter<BrowserParallelLimit>]
public class EndToEndTests
{
    static PublishedWizard wizard => PublishedWizard.Shared;

    [Test]
    public async Task NewFlowToZipDownload()
    {
        var page = await ScreenSnapshotTests.Open("/", ".entry-cards");
        await page.ClickAsync("a.entry-card[href=new]");
        // The first choice on each of these steps moves on by itself.
        await page.ClickAsync("#host-AspNetCore");
        await page.ClickAsync("#transport-RabbitMQ");
        await page.ClickAsync("#persistence-PostgreSql");
        await page.ClickAsync("#serializer-SystemJson");
        await page.ClickAsync("#tests-NUnit");

        await page.ClickAsync(".feature-card[data-id=Sagas] input");
        await page.ClickAsync("button.primary");
        // The options step starts on the defaults, so it is passed by moving on.
        await page.WaitForSelectorAsync(".choice[data-choice=installers]");
        await page.ClickAsync("button.primary");

        await page.WaitForSelectorAsync("button.download-zip");
        await Assert.That(page.Url).EndsWith("/new?step=output&host=AspNetCore&transport=RabbitMQ&persistence=PostgreSql&serializer=SystemJson&tests=NUnit&features=Sagas");

        var download = await page.RunAndWaitForDownloadAsync(() => page.ClickAsync("button.download-zip"));
        await Assert.That(download.SuggestedFilename).IsEqualTo("SampleEndpoint.zip");
        var path = Path.Combine(Path.GetTempPath(), $"wizard-{Guid.NewGuid():N}.zip");
        await download.SaveAsAsync(path);
        try
        {
            await using var archive = await ZipFile.OpenReadAsync(path);
            var entries = archive.Entries.Select(_ => _.FullName).ToList();
            await Assert.That(entries).Contains("SampleEndpoint/src/SampleEndpoint/Program.cs");
            await Assert.That(entries).Contains("SampleEndpoint/src/SampleEndpoint/Sagas/OrderSaga.cs");
            await Assert.That(entries).Contains("SampleEndpoint/docker-compose.yml");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task SharedLinkOpensTheSameState()
    {
        const string url = "/new?step=output&host=Worker&transport=SqlServer&persistence=SqlServer&serializer=Xml&tests=None&name=Acme&features=Outbox&installers=false";
        var first = await ScreenSnapshotTests.Open(url, "button.download-zip");
        var second = await ScreenSnapshotTests.Open(new Uri(first.Url).PathAndQuery, "button.download-zip");

        await Assert.That(second.Url).IsEqualTo(first.Url);
        await Assert.That(await second.InputValueAsync("#solutionName")).IsEqualTo("Acme");
        var values = await second.Locator(".breadcrumb .step-value").AllTextContentsAsync();
        await Assert.That(string.Join(" | ", values)).IsEqualTo("Worker service | SQL Server | SQL Server | XML | No tests | Outbox | No installers");
    }

    /// <summary>Selecting send-only with a saga blocks the step, and the notice says why. Deselecting one unblocks it.</summary>
    [Test]
    public async Task ConflictingFeaturesBlockTheStep()
    {
        var page = await ScreenSnapshotTests.Open(
            "/new?step=features&host=Worker&transport=Learning&persistence=NonDurable&serializer=SystemJson&tests=None&features=SendOnly,Sagas",
            ".interaction-notice[data-rule=send-only]");

        var next = page.Locator("button.primary");
        await Assert.That(await next.IsDisabledAsync()).IsTrue();

        await page.ClickAsync(".feature-card[data-id=Sagas] input");
        await page.WaitForSelectorAsync(".interaction-notice[data-rule=send-only]", new() {State = WaitForSelectorState.Detached});
        await Assert.That(await next.IsDisabledAsync()).IsFalse();
    }

    /// <summary>
    /// The notices are below a long list of cards. The count beside the filter is there for a conflict
    /// whose notice is out of sight, and brings it into view.
    /// </summary>
    [Test]
    public async Task TheConflictCountJumpsToTheNotices()
    {
        var page = await ScreenSnapshotTests.Open(
            "/new?step=features&host=Worker&transport=Learning&persistence=NonDurable&serializer=SystemJson&tests=None&features=SendOnly,Sagas",
            ".conflict-count");

        var notice = page.Locator(".interaction-notice[data-rule=send-only]");
        await Assertions.Expect(notice).Not.ToBeInViewportAsync();
        // the cards say it without scrolling
        await Assertions.Expect(page.Locator(".feature-card.conflict[data-id=Sagas] .badge.conflict")).ToBeInViewportAsync();

        await page.ClickAsync(".conflict-count");
        await Assertions.Expect(notice).ToBeInViewportAsync();
        await Assert.That(page.Url).Contains("/new?step=features&");
    }

    /// <summary>The add flow reads a chosen Directory.Packages.props in the browser and answers its first two steps.</summary>
    [Test]
    public async Task APackagesPropsAnswersWhatTheEndpointRunsOn()
    {
        var page = await ScreenSnapshotTests.Open("/add", "#packagesProps");
        await page.SetInputFilesAsync(
            "#packagesProps",
            new FilePayload
            {
                Name = PackagesProps.FileName,
                MimeType = "text/xml",
                Buffer = """
                         <Project>
                           <ItemGroup>
                             <PackageVersion Include="NServiceBus.Transport.SqlServer" Version="1.0.0" />
                             <PackageVersion Include="NServiceBus.Persistence.Sql" Version="1.0.0" />
                             <PackageVersion Include="Microsoft.Data.SqlClient" Version="1.0.0" />
                           </ItemGroup>
                         </Project>
                         """u8.ToArray()
            });

        await page.WaitForSelectorAsync(".packages-result");
        await Assert.That(await page.TextContentAsync(".packages-result"))
            .IsEqualTo("Directory.Packages.props: found the SQL Server transport and SQL Server persistence.");
        await Assert.That(page.Url).EndsWith("/add?step=transport&transport=SqlServer&persistence=SqlServer");
    }

    /// <summary>
    /// An extension readme's deep link, in a browser that remembers what the endpoint runs on: the
    /// first two steps are already answered, and the download is the changes to merge.
    /// </summary>
    [Test]
    public async Task AddFlowFromADeepLinkWithRememberedAnswers()
    {
        var page = await wizard.NewIsolatedPage();
        try
        {
            await page.GotoAsync(wizard.Url("/add"));
            await page.ClickAsync("#transport-RabbitMQ");
            await page.ClickAsync("#persistence-MongoDb");
            await page.WaitForSelectorAsync(".feature-card[data-id=Serilog]");

            await page.GotoAsync(wizard.Url("/add/Serilog"));
            await page.WaitForSelectorAsync(".restored");
            await Assert.That(page.Url).EndsWith("/add?step=transport&transport=RabbitMQ&persistence=MongoDb&features=Serilog");

            await page.GotoAsync(wizard.Url("/add?step=output&transport=RabbitMQ&persistence=MongoDb&features=Serilog"));
            var download = await page.RunAndWaitForDownloadAsync(() => page.ClickAsync("button.download-zip"));
            await Assert.That(download.SuggestedFilename).IsEqualTo("nservicebus-additions.zip");
            var path = Path.Combine(Path.GetTempPath(), $"wizard-{Guid.NewGuid():N}.zip");
            await download.SaveAsAsync(path);
            try
            {
                await using var archive = await ZipFile.OpenReadAsync(path);
                var fragment = archive.GetEntry($"nservicebus-additions/{AdditionGenerator.ProgramFragment}");
                await Assert.That(fragment).IsNotNull();
                using var reader = new StreamReader(await fragment!.OpenAsync());
                await Assert.That(await reader.ReadToEndAsync()).Contains("builder.Logging.AddSerilog();");
            }
            finally
            {
                File.Delete(path);
            }
        }
        finally
        {
            await page.Context.CloseAsync();
        }
    }

    [Test]
    public async Task BackAndForwardMoveBetweenSteps()
    {
        var page = await ScreenSnapshotTests.Open("/new", "#host-Worker");
        await page.ClickAsync("#host-Worker");
        await page.WaitForSelectorAsync("#transport-Learning");

        await page.GoBackAsync();
        await page.WaitForSelectorAsync("#host-Worker.selected");

        await page.GoForwardAsync();
        await page.WaitForSelectorAsync("#transport-Learning");
    }

    [Test]
    public async Task NextStartsTheNextStepAtTheTop()
    {
        var page = await ScreenSnapshotTests.Open("/new?step=features&host=Worker&transport=Learning&persistence=NonDurable&serializer=SystemJson&tests=None", ".feature-card[data-id=Sagas]");
        await page.EvaluateAsync("() => window.scrollTo(0, document.body.scrollHeight)");
        await Assert.That(await page.EvaluateAsync<double>("() => window.scrollY")).IsGreaterThan(0);

        await page.ClickAsync("button.primary");
        await page.WaitForSelectorAsync(".choice[data-choice=installers]");
        await page.WaitForFunctionAsync("() => window.scrollY === 0");
    }
}
