/// <summary>Reading what an endpoint runs on out of its solution's Directory.Packages.props.</summary>
public class PackagesPropsTests
{
    static async Task<string> Read(params string[] packages)
    {
        var items = string.Join("", packages.Select(_ => $"""<PackageVersion Include="{_}" Version="1.0.0" />"""));
        await Assert.That(PackagesProps.TryRead($"<Project><ItemGroup>{items}</ItemGroup></Project>", out var transport, out var persistence)).IsTrue();
        return $"{transport?.ToString() ?? "none"}/{persistence?.ToString() ?? "none"}";
    }

    [Test]
    public async Task TransportAndPersistence() =>
        await Assert.That(await Read("NServiceBus", "nservicebus.rabbitmq", "NServiceBus.Storage.MongoDB")).IsEqualTo("RabbitMQ/MongoDb");

    /// <summary>The SQL persistences share a package, and the database client tells them apart.</summary>
    [Test]
    [Arguments("Microsoft.Data.SqlClient", "SqlServer")]
    [Arguments("Npgsql", "PostgreSql")]
    [Arguments("MySqlConnector", "MySql")]
    public async Task SqlPersistenceByItsClient(string client, string persistence) =>
        await Assert.That(await Read("NServiceBus.Persistence.Sql", client)).IsEqualTo($"none/{persistence}");

    [Test]
    public async Task SqlPersistenceWithTwoClientsIsNotGuessed() =>
        await Assert.That(await Read("NServiceBus.Persistence.Sql", "Npgsql", "Microsoft.Data.SqlClient")).IsEqualTo("none/none");

    [Test]
    public async Task TwoTransportsAreNotGuessed() =>
        await Assert.That(await Read("NServiceBus.RabbitMQ", "NServiceBus.AmazonSQS", "NServiceBus.RavenDB")).IsEqualTo("none/RavenDb");

    /// <summary>Non-durable persistence beside a real one is what the tests use.</summary>
    [Test]
    public async Task NonDurableGivesWayToARealPersistence()
    {
        await Assert.That(await Read("NServiceBus.Persistence.NonDurable", "NServiceBus.Persistence.CosmosDB")).IsEqualTo("none/CosmosDb");
        await Assert.That(await Read("NServiceBus.Persistence.NonDurable")).IsEqualTo("none/NonDurable");
    }

    /// <summary>The learning transport and persistence are in the NServiceBus package itself.</summary>
    [Test]
    public async Task NServiceBusAloneSaysNothing() =>
        await Assert.That(await Read("NServiceBus")).IsEqualTo("none/none");

    [Test]
    public async Task NotXml() =>
        await Assert.That(PackagesProps.TryRead("not a props file", out _, out _)).IsFalse();
}
