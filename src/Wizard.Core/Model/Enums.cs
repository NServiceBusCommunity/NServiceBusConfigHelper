// Member names are url values. Do not rename members: a shared link would stop meaning what it meant.

public enum Flow
{
    New,
    Add
}

/// <summary>What hosts the endpoint. Both run on the .NET generic host.</summary>
public enum Hosting
{
    Worker,
    AspNetCore
}

public enum Transport
{
    Learning,
    RabbitMQ,
    AzureServiceBus,
    AzureStorageQueues,
    AmazonSqs,
    SqlServer,
    PostgreSql
}

public enum Persistence
{
    Learning,
    NonDurable,
    SqlServer,
    PostgreSql,
    MySql,
    CosmosDb,
    AzureTable,
    DynamoDb,
    MongoDb,
    RavenDb
}

public enum Serializer
{
    SystemJson,
    NewtonsoftJson,
    Xml
}

public enum TestFramework
{
    XunitV3,
    NUnit,
    TUnit,
    MSTest,
    None
}
