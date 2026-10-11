/// <summary>
/// The containers the generated <c>docker-compose.yml</c> can start, and the connection strings that
/// point at them. Kept in one place because a transport and a persistence often share one: SQL Server
/// for both is one container and one connection string.
/// </summary>
/// <remarks>
/// The passwords are for a container on a developer's own machine and are written into the generated
/// files in the clear. The guide says so, and says not to reuse them.
/// </remarks>
public static class Infrastructure
{
    public const string SqlServerPassword = "NServiceBus-Dev-1";
    public const string DatabasePassword = "nservicebus";

    public static readonly InfrastructureDefinition RabbitMq = new(
        "rabbitmq",
        "RabbitMQ",
        """
          rabbitmq:
            image: rabbitmq:4-management
            ports:
              - "5672:5672"
              # management ui: http://localhost:15672 (guest / guest)
              - "15672:15672"
        """,
        []);

    public static readonly InfrastructureDefinition SqlServer = new(
        "sqlserver",
        "SQL Server",
        $"""
           sqlserver:
             image: mcr.microsoft.com/mssql/server:2022-latest
             environment:
               ACCEPT_EULA: "Y"
               MSSQL_SA_PASSWORD: "{SqlServerPassword}"
             ports:
               - "1433:1433"
         """,
        [
            "SQL Server starts with no database. Create it once the container is up: " +
            $"`docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P {SqlServerPassword} -C -Q \"CREATE DATABASE [{{Database}}]\"`."
        ]);

    public static readonly InfrastructureDefinition PostgreSql = new(
        "postgres",
        "PostgreSQL",
        $$"""
            postgres:
              image: postgres:17
              environment:
                POSTGRES_PASSWORD: {{DatabasePassword}}
                POSTGRES_DB: {database}
              ports:
                - "5432:5432"
          """,
        []);

    public static readonly InfrastructureDefinition MySql = new(
        "mysql",
        "MySQL",
        $$"""
            mysql:
              image: mysql:8.4
              environment:
                MYSQL_ROOT_PASSWORD: {{DatabasePassword}}
                MYSQL_DATABASE: {database}
              ports:
                - "3306:3306"
          """,
        []);

    public static readonly InfrastructureDefinition Azurite = new(
        "azurite",
        "Azurite (Azure Storage emulator)",
        """
          azurite:
            image: mcr.microsoft.com/azure-storage/azurite
            ports:
              - "10000:10000"
              - "10001:10001"
              - "10002:10002"
        """,
        []);

    public static readonly InfrastructureDefinition MongoDb = new(
        "mongodb",
        "MongoDB",
        """
          mongodb:
            image: mongo:8
            # NServiceBus uses transactions, which MongoDB only has on a replica set. This is a set of one;
            # the health check initiates it the first time the container starts.
            command: ["--replSet", "rs0", "--bind_ip_all"]
            ports:
              - "27017:27017"
            healthcheck:
              test: mongosh --quiet --eval "try { rs.status().ok } catch (error) { rs.initiate({_id:'rs0',members:[{_id:0,host:'localhost:27017'}]}).ok }"
              interval: 5s
              timeout: 10s
              retries: 10
        """,
        []);

    public static readonly InfrastructureDefinition RavenDb = new(
        "ravendb",
        "RavenDB",
        """
          ravendb:
            image: ravendb/ravendb
            environment:
              RAVEN_Setup_Mode: None
              RAVEN_License_Eula_Accepted: "true"
              RAVEN_Security_UnsecuredAccessAllowed: PublicNetwork
            ports:
              # studio: http://localhost:8080
              - "8080:8080"
        """,
        [
            "RavenDB starts with no database. Create one named `{Database}` in the studio at http://localhost:8080 before the first run."
        ]);
}

/// <summary>The connection strings components name. Shared, so two components naming one share its variable.</summary>
public static class Connections
{
    public static readonly ConnectionDefinition RabbitMq = new(
        "RabbitMQ",
        "rabbitMqConnectionString",
        "host=localhost;username=guest;password=guest",
        "The broker's host and credentials, in [the transport's connection string format](https://docs.particular.net/transports/rabbitmq/connection-settings).");

    public static readonly ConnectionDefinition AzureServiceBus = new(
        "AzureServiceBus",
        "azureServiceBusConnectionString",
        "",
        "The namespace's connection string, from **Shared access policies** in the Azure portal. Azure Service Bus has no local emulator NServiceBus can create its topology on, so this needs a real namespace.");

    public static readonly ConnectionDefinition AzureStorage = new(
        "AzureStorage",
        "azureStorageConnectionString",
        "UseDevelopmentStorage=true",
        "The storage account's connection string, from **Access keys** in the Azure portal.");

    public static readonly ConnectionDefinition SqlServer = new(
        "SqlServer",
        "sqlServerConnectionString",
        $"Server=localhost,1433;Database={{Database}};User Id=sa;Password={Infrastructure.SqlServerPassword};Encrypt=false",
        "A SQL Server database the endpoint can create tables in.");

    public static readonly ConnectionDefinition PostgreSql = new(
        "PostgreSql",
        "postgreSqlConnectionString",
        $"Host=localhost;Port=5432;Database={{database}};Username=postgres;Password={Infrastructure.DatabasePassword}",
        "A PostgreSQL database the endpoint can create tables in.");

    public static readonly ConnectionDefinition MySql = new(
        "MySql",
        "mySqlConnectionString",
        $"Server=localhost;Port=3306;Database={{database}};User ID=root;Password={Infrastructure.DatabasePassword};AllowUserVariables=True;AutoEnlist=false",
        "A MySQL database. Keep `AllowUserVariables=True` and `AutoEnlist=false`, which SQL persistence requires.");

    public static readonly ConnectionDefinition CosmosDb = new(
        "CosmosDb",
        "cosmosDbConnectionString",
        "",
        "The account's primary connection string, from **Keys** in the Azure portal, or the Cosmos DB emulator's.");

    public static readonly ConnectionDefinition MongoDb = new(
        "MongoDb",
        "mongoDbConnectionString",
        "mongodb://localhost:27017/?replicaSet=rs0",
        "A MongoDB replica set or sharded cluster; a standalone server has no transactions.");

    public static readonly ConnectionDefinition RavenDb = new(
        "RavenDb",
        "ravenDbUrl",
        "http://localhost:8080",
        "The url of a RavenDB server.");
}
