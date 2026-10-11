/// <summary>What the browser remembers, and when it applies.</summary>
public class BrowserMemoryTests
{
    static Remembered everything = new("RabbitMQ", "MongoDb");

    [Test]
    public async Task RememberedAnswersFillInWhatTheUrlLeavesOut()
    {
        var state = WizardStateUrl.Parse(Flow.Add, "");

        var restored = BrowserMemory.Seed(state, "", everything);

        await Assert.That(restored).IsEqualTo(new(Transport: true, Persistence: true));
        await Assert.That(state.Transport).IsEqualTo(Transport.RabbitMQ);
        await Assert.That(state.Persistence).IsEqualTo(Persistence.MongoDb);
    }

    /// <summary>A shared link means the same thing in every browser.</summary>
    [Test]
    public async Task TheUrlWins()
    {
        const string query = "transport=SqlServer&persistence=SqlServer";
        var state = WizardStateUrl.Parse(Flow.Add, query);

        var restored = BrowserMemory.Seed(state, query, everything);

        await Assert.That(restored.Any).IsFalse();
        await Assert.That(state.Transport).IsEqualTo(Transport.SqlServer);
        await Assert.That(state.Persistence).IsEqualTo(Persistence.SqlServer);
    }

    /// <summary>A new endpoint is a fresh decision, so that flow neither reads nor writes.</summary>
    [Test]
    public async Task TheNewFlowRemembersNothing()
    {
        var state = WizardStateUrl.Parse(Flow.New, "");

        var restored = BrowserMemory.Seed(state, "", everything);

        await Assert.That(restored.Any).IsFalse();
        await Assert.That(state.Transport).IsNull();
        await Assert.That(BrowserMemory.For(GeneratorTests.State())).IsEqualTo(Remembered.None);
    }

    [Test]
    public async Task UnknownRememberedValuesAreDropped()
    {
        var state = WizardStateUrl.Parse(Flow.Add, "");
        var restored = BrowserMemory.Seed(state, "", new("Pigeon", "MongoDb"));

        await Assert.That(restored).IsEqualTo(new(Transport: false, Persistence: true));
        await Assert.That(state.Transport).IsNull();
    }

    /// <summary>A remembered persistence with no outbox rules the outbox out of a deep link to it.</summary>
    [Test]
    public async Task ARememberedPersistenceAppliesItsRules()
    {
        const string query = "features=Outbox,Sagas";
        var state = WizardStateUrl.Parse(Flow.Add, query);
        BrowserMemory.Seed(state, query, new("Learning", "Learning"));
        await Assert.That(state.SelectedFeatures).IsEquivalentTo(["Sagas"]);
    }
}
