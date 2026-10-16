/// <summary>Where sagas, the outbox and subscriptions are stored.</summary>
public sealed record PersistenceDefinition :
    ComponentDefinition
{
    public required Persistence Id { get; init; }

    /// <summary>Not for production: the guide and the options step say so.</summary>
    public bool DevelopmentOnly { get; init; }

    /// <summary>Whether it can store the outbox. A feature that needs the outbox is unavailable without it.</summary>
    public bool SupportsOutbox { get; init; } = true;

    /// <summary>
    /// What the outbox needs from this persistence beyond being switched on, as a markdown sentence,
    /// or null when enabling it is all there is.
    /// </summary>
    public string? OutboxNote { get; init; }
}

public static class Persistences
{
    const string sqlPersistence = "https://docs.particular.net/persistence/sql/";

    const string sqlScripts =
        "SQL persistence generates its table scripts at build time, into `bin/<Configuration>/<TargetFramework>/NServiceBus.Persistence.Sql`. " +
        "Installers run them on startup; without installers, run them as a deployment step.";

    public static IReadOnlyList<PersistenceDefinition> All { get; } =
    [
        new()
        {
            Id = Persistence.Learning,
            DisplayName = "Learning",
            Description = "Sagas are json files in a .sagas folder under the build output. Nothing to install; for development only.",
            DocsUrl = "https://docs.particular.net/persistence/learning/",
            DevelopmentOnly = true,
            SupportsOutbox = false,
            Code = "endpointConfiguration.UsePersistence<LearningPersistence>();",
            Notes =
            [
                "The learning persistence keeps each saga as a json file under `.sagas` in the build output folder. It has no outbox, and is not for production."
            ]
        },
        new()
        {
            Id = Persistence.NonDurable,
            DisplayName = "Non-durable",
            Description = "Everything is kept in memory and lost when the process stops. For tests and demos.",
            DocsUrl = "https://docs.particular.net/persistence/non-durable/",
            DevelopmentOnly = true,
            Packages = ["NServiceBus.Persistence.NonDurable"],
            Code = "endpointConfiguration.UsePersistence<NonDurablePersistence>();",
            Notes =
            [
                "Non-durable persistence loses every saga and outbox record when the process stops, so a restart can process a message twice or lose a saga. It is for tests and demos."
            ]
        },
        new()
        {
            Id = Persistence.SqlServer,
            DisplayName = "SQL Server",
            Description = "SQL persistence on SQL Server, with plain ADO.NET and scripts generated at build time.",
            DocsUrl = sqlPersistence,
            Packages = ["NServiceBus.Persistence.Sql", "Microsoft.Data.SqlClient"],
            TransitivePins = ["System.Security.Cryptography.Pkcs"],
            Usings = ["Microsoft.Data.SqlClient"],
            Connections = [Connections.SqlServer],
            Infrastructure = [Infrastructure.SqlServer],
            Code =
                """
                var persistence = endpointConfiguration.UsePersistence<SqlPersistence>();
                persistence.SqlDialect<SqlDialect.MsSqlServer>();
                persistence.ConnectionBuilder(() => new SqlConnection(sqlServerConnectionString));
                """,
            Notes = [sqlScripts]
        },
        new()
        {
            Id = Persistence.PostgreSql,
            DisplayName = "PostgreSQL",
            Description = "SQL persistence on PostgreSQL, storing saga data as jsonb.",
            DocsUrl = sqlPersistence,
            Packages = ["NServiceBus.Persistence.Sql", "Npgsql"],
            Usings = ["Npgsql", "NpgsqlTypes"],
            Connections = [Connections.PostgreSql],
            Infrastructure = [Infrastructure.PostgreSql],
            Code =
                """
                var persistence = endpointConfiguration.UsePersistence<SqlPersistence>();
                var dialect = persistence.SqlDialect<SqlDialect.PostgreSql>();
                // Saga data is stored as jsonb, which Npgsql has to be told for each parameter.
                dialect.JsonBParameterModifier(parameter =>
                {
                    var npgsqlParameter = (NpgsqlParameter) parameter;
                    npgsqlParameter.NpgsqlDbType = NpgsqlDbType.Jsonb;
                });
                persistence.ConnectionBuilder(() => new NpgsqlConnection(postgreSqlConnectionString));
                """,
            Notes = [sqlScripts]
        },
        new()
        {
            Id = Persistence.MySql,
            DisplayName = "MySQL",
            Description = "SQL persistence on MySQL.",
            DocsUrl = sqlPersistence,
            Packages = ["NServiceBus.Persistence.Sql", "MySqlConnector"],
            Usings = ["MySqlConnector"],
            Connections = [Connections.MySql],
            Infrastructure = [Infrastructure.MySql],
            Code =
                """
                var persistence = endpointConfiguration.UsePersistence<SqlPersistence>();
                persistence.SqlDialect<SqlDialect.MySql>();
                persistence.ConnectionBuilder(() => new MySqlConnection(mySqlConnectionString));
                """,
            Notes = [sqlScripts]
        },
        new()
        {
            Id = Persistence.CosmosDb,
            DisplayName = "Azure Cosmos DB",
            Description = "Sagas and the outbox as documents in a Cosmos DB container.",
            DocsUrl = "https://docs.particular.net/persistence/cosmosdb/",
            Packages = ["NServiceBus.Persistence.CosmosDB"],
            Usings = ["Microsoft.Azure.Cosmos"],
            Connections = [Connections.CosmosDb],
            Code =
                """
                var persistence = endpointConfiguration.UsePersistence<CosmosPersistence>();
                persistence.CosmosClient(new CosmosClient(cosmosDbConnectionString));
                persistence.DatabaseName("{Database}");
                persistence.DefaultContainer("{Database}", "/id");
                """,
            OutboxNote =
                "With Cosmos DB the outbox record gets a partition key made from the endpoint name and the message id unless one is mapped. " +
                "A handler that also writes its own documents needs them in the same partition to commit with the outbox record: map the key with `persistence.TransactionInformation()`; see [transactions](https://docs.particular.net/persistence/cosmosdb/transactions).",
            Notes =
            [
                "With installers on, the default container is created when it does not exist."
            ]
        },
        new()
        {
            Id = Persistence.AzureTable,
            DisplayName = "Azure Table Storage",
            Description = "Sagas and the outbox as entities in Azure Table Storage, or the Cosmos DB Table API.",
            DocsUrl = "https://docs.particular.net/persistence/azure-table/",
            Packages = ["NServiceBus.Persistence.AzureTable"],
            Usings = ["Azure.Data.Tables"],
            Connections = [Connections.AzureStorage],
            Infrastructure = [Infrastructure.Azurite],
            Code =
                """
                var persistence = endpointConfiguration.UsePersistence<AzureTablePersistence>();
                persistence.UseTableServiceClient(new TableServiceClient(azureStorageConnectionString));
                persistence.DefaultTable("{Database}");
                """,
            OutboxNote =
                "With Azure Table Storage, saga data, the outbox record and a handler's own entities only commit together when they share one partition of one table, which takes a pipeline behavior that sets the partition key; see [transactions](https://docs.particular.net/persistence/azure-table/transactions)."
        },
        new()
        {
            Id = Persistence.DynamoDb,
            DisplayName = "Amazon DynamoDB",
            Description = "Sagas and the outbox as items in a DynamoDB table.",
            DocsUrl = "https://docs.particular.net/persistence/dynamodb/",
            Packages = ["NServiceBus.Persistence.DynamoDB"],
            Code =
                """
                // Credentials and the region come from the AWS SDK's default chain, as for any AWS client.
                endpointConfiguration.UsePersistence<DynamoPersistence>();
                """,
            Notes =
            [
                "Sagas and the outbox share one table, named `NServiceBus.Storage` unless configured otherwise. With installers on, it is created when it does not exist."
            ]
        },
        new()
        {
            Id = Persistence.MongoDb,
            DisplayName = "MongoDB",
            Description = "Sagas and the outbox as documents in MongoDB. Needs a replica set, for transactions.",
            DocsUrl = "https://docs.particular.net/persistence/mongodb/",
            Packages = ["NServiceBus.Storage.MongoDB"],
            Usings = ["MongoDB.Driver"],
            Connections = [Connections.MongoDb],
            Infrastructure = [Infrastructure.MongoDb],
            Code =
                """
                var persistence = endpointConfiguration.UsePersistence<MongoPersistence>();
                persistence.MongoClient(new MongoClient(mongoDbConnectionString));
                persistence.DatabaseName("{Database}");
                """,
            Notes =
            [
                "MongoDB persistence uses transactions by default, which a standalone server does not have. Run a replica set, even of one node, as the generated compose file does."
            ]
        },
        new()
        {
            Id = Persistence.RavenDb,
            DisplayName = "RavenDB",
            Description = "Sagas and the outbox as documents in RavenDB.",
            DocsUrl = "https://docs.particular.net/persistence/ravendb/",
            Packages = ["NServiceBus.RavenDB"],
            Usings = ["Raven.Client.Documents"],
            Connections = [Connections.RavenDb],
            Infrastructure = [Infrastructure.RavenDb],
            Code =
                """
                var documentStore = new DocumentStore
                {
                    Urls = [ravenDbUrl],
                    Database = "{Database}"
                };
                documentStore.Initialize();
                var persistence = endpointConfiguration.UsePersistence<RavenDBPersistence>();
                persistence.SetDefaultDocumentStore(documentStore);
                """
        }
    ];

    public static PersistenceDefinition For(Persistence persistence) =>
        All.Single(_ => _.Id == persistence);
}

/// <summary>How message bodies are written. The serializers built into NServiceBus need no package.</summary>
public sealed record SerializerDefinition :
    ComponentDefinition
{
    public required Serializer Id { get; init; }
}

public static class Serializers
{
    public static IReadOnlyList<SerializerDefinition> All { get; } =
    [
        new()
        {
            Id = Serializer.SystemJson,
            DisplayName = "System.Text.Json",
            Description = "Json with the serializer built into .NET. No extra package; the usual choice.",
            DocsUrl = "https://docs.particular.net/nservicebus/serialization/system-json",
            Code = "endpointConfiguration.UseSerialization<SystemJsonSerializer>();"
        },
        new()
        {
            Id = Serializer.NewtonsoftJson,
            DisplayName = "Newtonsoft.Json",
            Description = "Json with Json.NET, for systems whose messages already rely on its behaviour.",
            DocsUrl = "https://docs.particular.net/nservicebus/serialization/newtonsoft",
            Packages = ["NServiceBus.Newtonsoft.Json"],
            Code = "endpointConfiguration.UseSerialization<NewtonsoftJsonSerializer>();"
        },
        new()
        {
            Id = Serializer.Xml,
            DisplayName = "XML",
            Description = "The XML serializer built into NServiceBus, for older systems that already exchange XML.",
            DocsUrl = "https://docs.particular.net/nservicebus/serialization/xml",
            Code = "endpointConfiguration.UseSerialization<XmlSerializer>();",
            Notes =
            [
                "The XML serializer needs message classes with a parameterless constructor and settable properties, which is how the sample messages are written."
            ]
        }
    ];

    public static SerializerDefinition For(Serializer serializer) =>
        All.Single(_ => _.Id == serializer);
}
