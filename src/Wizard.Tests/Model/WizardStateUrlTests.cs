public class WizardStateUrlTests
{
    public static IEnumerable<Func<WizardState>> States()
    {
        yield return () => new()
        {
            Flow = Flow.New,
            Step = "host"
        };
        yield return GeneratorTests.State;
        yield return () => GeneratorTests.State() with
        {
            Hosting = Hosting.AspNetCore,
            Transport = Transport.RabbitMQ,
            Persistence = Persistence.PostgreSql,
            Serializer = Serializer.NewtonsoftJson,
            TestFramework = TestFramework.None,
            SolutionName = "Acme.Sales",
            Installers = false
        };
        yield return GeneratorTests.Everything;
        yield return () =>
        {
            var state = GeneratorTests.WithFeatures(
                GeneratorTests.State() with
                {
                    Transport = Transport.RabbitMQ
                },
                "Auditing", "AuditFilter");
            state.SetChoice("rabbitmq-queue-type", "Classic");
            state.SetChoice("audit-filter-default", "ExcludeFromAudit");
            return state;
        };
        yield return () => GeneratorTests.Addition(Transport.SqlServer, Persistence.SqlServer, "Outbox", "Serilog");
    }

    [Test]
    [MethodDataSource(nameof(States))]
    public async Task RoundTrips(WizardState state)
    {
        // the url carries the normalized state: values that cannot apply are not kept
        state.Normalize();
        var url = WizardStateUrl.ToRelativeUrl(state);
        var query = "";
        if (url.Contains('?'))
        {
            query = url[(url.IndexOf('?') + 1)..];
        }

        var parsed = WizardStateUrl.Parse(state.Flow, query);
        await Assert.That(parsed).IsEqualTo(state);
    }

    [Test]
    public Task Urls() =>
        Verify(States().Select(_ => WizardStateUrl.ToAbsoluteUrl(_())));

    /// <summary>Answers to questions a flow does not ask are dropped, so the link holds only what matters.</summary>
    [Test]
    public async Task QuestionsTheAddFlowDoesNotAskAreDropped()
    {
        var state = WizardStateUrl.Parse(Flow.Add, "host=Worker&transport=RabbitMQ&persistence=MongoDb&serializer=Xml&tests=NUnit&name=Acme&installers=false&features=Sagas");
        await Assert.That(WizardStateUrl.ToRelativeUrl(state)).IsEqualTo("add?step=transport&transport=RabbitMQ&persistence=MongoDb&features=Sagas");
    }

    [Test]
    public async Task UnknownFeaturesAndStaleChoicesAreDropped()
    {
        var state = WizardStateUrl.Parse(Flow.New, "transport=Learning&features=Sagas,NotAFeature&opt=nope:1,rabbitmq-queue-type:Classic");
        await Assert.That(state.SelectedFeatures).IsEquivalentTo(["Sagas"]);
        // the choice belongs to a transport that is not the one chosen
        await Assert.That(state.Choices).IsEmpty();
    }

    /// <summary>A default is what an absent key means, so it is never written.</summary>
    [Test]
    public async Task ADefaultChoiceIsNotWritten()
    {
        var state = WizardStateUrl.Parse(Flow.New, "transport=RabbitMQ&opt=rabbitmq-queue-type:Quorum");
        await Assert.That(WizardStateUrl.ToQuery(state)).DoesNotContain("opt=");
    }

    /// <summary>The order ids were added in must not change the link.</summary>
    [Test]
    public async Task FeatureOrderIsTheRegistryOrder()
    {
        var one = WizardStateUrl.Parse(Flow.New, "features=Serilog,Sagas");
        var other = WizardStateUrl.Parse(Flow.New, "features=Sagas,Serilog");
        await Assert.That(WizardStateUrl.ToQuery(one)).IsEqualTo(WizardStateUrl.ToQuery(other));
        await Assert.That(WizardStateUrl.ToQuery(one)).EndsWith("features=Sagas,Serilog");
    }

    /// <summary>Changing the persistence to one with no outbox drops the outbox, and the link with it.</summary>
    [Test]
    public async Task AFeatureThePersistenceRulesOutIsDropped()
    {
        var state = WizardStateUrl.Parse(Flow.New, "persistence=Learning&features=Outbox");
        await Assert.That(state.SelectedFeatures).IsEmpty();
    }

    [Test]
    [Arguments("")]
    [Arguments("?")]
    [Arguments("?host=worker&transport=42&persistence=&step=nope&tests=Xunit")]
    [Arguments("?%zz=%zz&=&&host")]
    public async Task JunkParsesToDefaults(string query)
    {
        var state = WizardStateUrl.Parse(Flow.New, query);
        await Assert.That(state.Hosting).IsNull();
        await Assert.That(state.Transport).IsNull();
        await Assert.That(state.Persistence).IsNull();
        await Assert.That(state.TestFramework).IsNull();
        await Assert.That(state.Step).IsEqualTo("host");
    }

    [Test]
    public async Task FirstOccurrenceWins()
    {
        var state = WizardStateUrl.Parse(Flow.New, "transport=RabbitMQ&transport=Learning");
        await Assert.That(state.Transport).IsEqualTo(Transport.RabbitMQ);
    }

    [Test]
    public async Task UrlCannotSkipAnIncompleteStep()
    {
        var state = WizardStateUrl.Parse(Flow.New, "step=output&host=Worker&transport=Learning");
        await Assert.That(FlowSteps.Reachable(state).Id).IsEqualTo("persistence");
    }

    /// <summary>Adding nothing is not an addition, so the features step holds until something is selected.</summary>
    [Test]
    public async Task TheAddFlowNeedsAFeature()
    {
        var state = WizardStateUrl.Parse(Flow.Add, "step=output&transport=Learning&persistence=Learning");
        await Assert.That(FlowSteps.Reachable(state).Id).IsEqualTo("features");
    }

    [Test]
    [Arguments("SampleEndpoint", "SampleEndpoint")]
    [Arguments("  My Endpoint! ", "MyEndpoint")]
    [Arguments("9lives", "SampleEndpoint")]
    [Arguments("...", "SampleEndpoint")]
    [Arguments("Acme.Sales", "Acme.Sales")]
    public async Task SolutionNamesAreCleaned(string input, string expected) =>
        await Assert.That(SolutionNames.Clean(input)).IsEqualTo(expected);

    [Test]
    public async Task DatabaseNamesAreLettersAndDigits() =>
        await Assert.That(SolutionNames.Database("Acme.Sales_2")).IsEqualTo("AcmeSales2");
}
