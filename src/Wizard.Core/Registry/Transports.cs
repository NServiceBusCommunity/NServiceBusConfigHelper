/// <summary>
/// How messages move between endpoints. <see cref="ComponentDefinition.Code"/> ends with a local
/// named <c>transport</c>, which <c>Program.cs</c> then hands to <c>UseTransport</c>.
/// </summary>
public sealed record TransportDefinition :
    ComponentDefinition
{
    public required Transport Id { get; init; }

    /// <summary>Not for production: the guide and the options step say so.</summary>
    public bool DevelopmentOnly { get; init; }

    /// <summary>The persistence that lives in the same service, offered first on the persistence step.</summary>
    public Persistence? PairsWith { get; init; }
}

public static class Transports
{
    public static IReadOnlyList<TransportDefinition> All { get; } =
    [
        new()
        {
            Id = Transport.Learning,
            DisplayName = "Learning",
            Description = "Messages are files in a .learningtransport folder. Nothing to install; for development only.",
            DocsUrl = "https://docs.particular.net/transports/learning/",
            DevelopmentOnly = true,
            PairsWith = Persistence.Learning,
            Code = "var transport = new LearningTransport();",
            Notes =
            [
                "The learning transport writes each message as a file under `.learningtransport` at the solution root, which makes them easy to inspect. It is not for production: choose a real transport before deploying."
            ]
        },
        new()
        {
            Id = Transport.RabbitMQ,
            DisplayName = "RabbitMQ",
            Description = "A broker to run anywhere. Native publish and subscribe, and delayed delivery.",
            DocsUrl = "https://docs.particular.net/transports/rabbitmq/",
            Packages = ["NServiceBus.RabbitMQ"],
            Connections = [Connections.RabbitMq],
            Infrastructure = [Infrastructure.RabbitMq],
            Code =
                """
                var transport = new RabbitMQTransport(
                    RoutingTopology.Conventional(QueueType.{rabbitmq-queue-type}),
                    rabbitMqConnectionString);
                """,
            Choices =
            [
                new(
                    "rabbitmq-queue-type",
                    "RabbitMQ queue type",
                    "The kind of queue the endpoint creates. Every endpoint that exchanges messages has to agree.",
                    "Quorum",
                    [
                        new("Quorum", "Quorum queues", "Replicated across the cluster's nodes. What NServiceBus recommends."),
                        new("Classic", "Classic queues", "Not replicated. For a broker that predates quorum queues.")
                    ])
            ],
            Notes =
            [
                "The conventional routing topology creates one exchange per message type, and binds a queue to the events its endpoint handles."
            ]
        },
        new()
        {
            Id = Transport.AzureServiceBus,
            DisplayName = "Azure Service Bus",
            Description = "Azure's managed broker. Native publish and subscribe, transactions and delayed delivery.",
            DocsUrl = "https://docs.particular.net/transports/azure-service-bus/",
            PairsWith = Persistence.CosmosDb,
            Packages = ["NServiceBus.Transport.AzureServiceBus"],
            Connections = [Connections.AzureServiceBus],
            Code =
                """
                var transport = new AzureServiceBusTransport(
                    azureServiceBusConnectionString,
                    TopicTopology.Default);
                """,
            Notes =
            [
                "`TopicTopology.Default` publishes each event type to a topic of its own. An endpoint joining a system that still uses the single `bundle-1` topic needs the migration topology instead; see [topology](https://docs.particular.net/transports/azure-service-bus/topology)."
            ]
        },
        new()
        {
            Id = Transport.AzureStorageQueues,
            DisplayName = "Azure Storage Queues",
            Description = "Queues in an Azure Storage account. Low cost, with delayed delivery kept in a table.",
            DocsUrl = "https://docs.particular.net/transports/azure-storage-queues/",
            PairsWith = Persistence.AzureTable,
            Packages = ["NServiceBus.Transport.AzureStorageQueues"],
            Connections = [Connections.AzureStorage],
            Infrastructure = [Infrastructure.Azurite],
            Code = "var transport = new AzureStorageQueueTransport(azureStorageConnectionString);",
            Notes =
            [
                "Storage queue names allow only lower case letters, digits and hyphens, up to 63 characters. An endpoint name with anything else, such as a dot, needs `transport.QueueNameSanitizer`; see [sanitization](https://docs.particular.net/transports/azure-storage-queues/sanitization)."
            ]
        },
        new()
        {
            Id = Transport.AmazonSqs,
            DisplayName = "Amazon SQS",
            Description = "Queues on SQS, with publish and subscribe through SNS.",
            DocsUrl = "https://docs.particular.net/transports/sqs/",
            PairsWith = Persistence.DynamoDb,
            Packages = ["NServiceBus.AmazonSQS"],
            Code =
                """
                // Credentials and the region come from the AWS SDK's default chain: environment
                // variables, a shared profile, or the role of the instance or task.
                var transport = new SqsTransport();
                """,
            Notes =
            [
                "There is no connection string: the AWS SDK finds credentials and a region on its own. For a local run, set `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY` and `AWS_REGION`, or point `AWS_ENDPOINT_URL` at an emulator such as LocalStack.",
                "A message too large for SQS needs an S3 bucket configured on the transport; see [the configuration options](https://docs.particular.net/transports/sqs/configuration-options)."
            ]
        },
        new()
        {
            Id = Transport.SqlServer,
            DisplayName = "SQL Server",
            Description = "Queues are tables in a SQL Server database. No broker to run, and it can share a transaction with the data.",
            DocsUrl = "https://docs.particular.net/transports/sql/",
            PairsWith = Persistence.SqlServer,
            Packages = ["NServiceBus.Transport.SqlServer"],
            Connections = [Connections.SqlServer],
            Infrastructure = [Infrastructure.SqlServer],
            Code = "var transport = new SqlServerTransport(sqlServerConnectionString);"
        },
        new()
        {
            Id = Transport.PostgreSql,
            DisplayName = "PostgreSQL",
            Description = "Queues are tables in a PostgreSQL database. No broker to run.",
            DocsUrl = "https://docs.particular.net/transports/postgresql/",
            PairsWith = Persistence.PostgreSql,
            Packages = ["NServiceBus.Transport.PostgreSql"],
            Connections = [Connections.PostgreSql],
            Infrastructure = [Infrastructure.PostgreSql],
            Code = "var transport = new PostgreSqlTransport(postgreSqlConnectionString);"
        }
    ];

    public static TransportDefinition For(Transport transport) =>
        All.Single(_ => _.Id == transport);
}
