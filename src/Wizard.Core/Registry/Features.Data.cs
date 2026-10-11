public static partial class Features
{
    const string community = "https://github.com/NServiceBusCommunity";

    /// <summary>
    /// Both attachment extensions ship the same sample: a command whose content travels beside the
    /// message, the handler that reads it, and the one place that sends it.
    /// </summary>
    static IReadOnlyList<FeatureFile> attachmentFiles =
    [
        new(
            "{CommandsFolder}/StoreDocument.cs",
            """
            namespace {Commands};

            /// <summary>The document's content travels as an attachment named "content", not in the message body.</summary>
            public class StoreDocument{ICommand}
            {
                public string Name { get; set; } = "";
            }
            """),
        new(
            "Handlers/StoreDocumentHandler.cs",
            """
            {UsingCommands}

            namespace {Namespace};

            public class StoreDocumentHandler(ILogger<StoreDocumentHandler> logger) :
                IHandleMessages<StoreDocument>
            {
                public async Task Handle(StoreDocument message, IMessageHandlerContext context)
                {
                    // Read on demand: nothing is loaded until it is asked for, and a large attachment can
                    // be copied to a stream instead with CopyTo.
                    var attachments = context.Attachments();
                    var content = await attachments.GetString("content", cancel: context.CancellationToken);
                    logger.LogInformation("Document {Name} has {Length} characters", message.Name, content.Value.Length);
                }
            }
            """,
            Receiving: true),
        new(
            "Attachments/DocumentSender.cs",
            """
            {UsingCommands}

            namespace {Namespace};

            /// <summary>Sends a <see cref="StoreDocument"/> with its content attached.</summary>
            public static class DocumentSender
            {
                public static Task Send(IMessageSession session, string name, string content)
                {
                    var options = new SendOptions();
                    options.RouteToThisEndpoint(); //:receiving
                    var attachments = options.Attachments();
                    attachments.AddString("content", content);
                    return session.Send(
                        new StoreDocument
                        {
                            Name = name
                        },
                        options);
                }
            }
            """)
    ];

    public static IReadOnlyList<FeatureDefinition> All { get; } =
    [
        new()
        {
            Id = "SendOnly",
            Category = FeatureCategory.Messaging,
            DisplayName = "Send-only",
            Description = "The endpoint sends and publishes but never receives: no input queue, no handlers. Typical for a web front end.",
            DocsUrl = "https://docs.particular.net/nservicebus/endpoints/#send-only",
            Code = "endpointConfiguration.SendOnly();",
            Notes =
            [
                "A send-only endpoint has no input queue, so nothing can be sent to it, and it cannot subscribe to events. Commands it sends need a route to the endpoint that handles them."
            ]
        },
        new()
        {
            Id = "Conventions",
            Category = FeatureCategory.Messaging,
            DisplayName = "Message conventions",
            Description = "Commands and events are recognised by namespace, so message classes need no NServiceBus interface or reference.",
            DocsUrl = "https://docs.particular.net/nservicebus/messaging/conventions",
            Code =
                """
                var conventions = endpointConfiguration.Conventions();
                conventions.DefiningCommandsAs(type => type.Namespace == "{Commands}");
                conventions.DefiningEventsAs(type => type.Namespace == "{Events}");
                """,
            Notes =
            [
                "Message classes are plain classes: what makes one a command or an event is its namespace. Every endpoint that exchanges these messages needs the same conventions."
            ]
        },
        new()
        {
            Id = "Sagas",
            Category = FeatureCategory.Messaging,
            DisplayName = "Sagas",
            Description = "A long-running process with its own state: a sample saga that is started by an event and completes on a timeout.",
            DocsUrl = "https://docs.particular.net/nservicebus/sagas/",
            NeedsReceiving = true,
            UsesSamples = true,
            Files =
            [
                new(
                    "Sagas/OrderSaga.cs",
                    """
                    {UsingEvents}

                    namespace {Namespace};

                    /// <summary>
                    /// Starts when an order is placed, then waits for the cancellation window to pass. There is one
                    /// instance per order, found by <see cref="OrderSagaData.OrderId"/>.
                    /// </summary>
                    public class OrderSaga(ILogger<OrderSaga> logger) :
                        Saga<OrderSagaData>,
                        IAmStartedByMessages<OrderPlaced>,
                        IHandleTimeouts<CancellationWindowElapsed>
                    {
                        protected override void ConfigureHowToFindSaga(SagaPropertyMapper<OrderSagaData> mapper) =>
                            mapper.MapSaga(saga => saga.OrderId)
                                .ToMessage<OrderPlaced>(message => message.OrderId);

                        public async Task Handle(OrderPlaced message, IMessageHandlerContext context)
                        {
                            logger.LogInformation("Order {OrderId}: waiting for the cancellation window", message.OrderId);
                            await RequestTimeout<CancellationWindowElapsed>(context, TimeSpan.FromSeconds(10));
                        }

                        public Task Timeout(CancellationWindowElapsed state, IMessageHandlerContext context)
                        {
                            logger.LogInformation("Order {OrderId}: cancellation window elapsed, order confirmed", Data.OrderId);
                            MarkAsComplete();
                            return Task.CompletedTask;
                        }
                    }
                    """,
                    Receiving: true),
                new(
                    "Sagas/OrderSagaData.cs",
                    """
                    namespace {Namespace};

                    /// <summary>What <see cref="OrderSaga"/> remembers between messages. The persistence stores one per order.</summary>
                    public class OrderSagaData :
                        ContainSagaData
                    {
                        public Guid OrderId { get; set; }
                    }
                    """,
                    Receiving: true),
                new(
                    "Sagas/CancellationWindowElapsed.cs",
                    """
                    namespace {Namespace};

                    /// <summary>The timeout <see cref="OrderSaga"/> requests. It only ever comes back to the saga that asked for it.</summary>
                    public class CancellationWindowElapsed;
                    """,
                    Receiving: true)
            ],
            Notes =
            [
                "A saga's state is stored by the persistence. Messages for one saga instance are handled one at a time: a concurrent update fails and is retried.",
                "A timeout is a message sent to the saga itself with a delay, so it survives a restart."
            ]
        },
        new()
        {
            Id = "HandlerOrdering",
            Category = FeatureCategory.Messaging,
            DisplayName = "Handler ordering",
            Description = "When several handlers take the same message, each says which handlers it runs after, and the order is worked out at startup.",
            DocsUrl = $"{community}/NServiceBus.Community.HandlerOrdering",
            Community = true,
            NeedsReceiving = true,
            UsesSamples = true,
            Packages = ["NServiceBus.Community.HandlerOrdering"],
            Usings = ["HandlerOrdering"],
            Code = "endpointConfiguration.ApplyInterfaceHandlerOrdering();",
            Files =
            [
                new(
                    "Handlers/NotifyWarehouseHandler.cs",
                    """
                    using HandlerOrdering;
                    {UsingCommands}

                    namespace {Namespace};

                    /// <summary>
                    /// A second handler for <see cref="PlaceOrder"/>. <see cref="IWantToRunAfter{THandler}"/> makes it
                    /// run after <see cref="PlaceOrderHandler"/>, without one list somewhere naming every handler.
                    /// </summary>
                    public class NotifyWarehouseHandler(ILogger<NotifyWarehouseHandler> logger) :
                        IHandleMessages<PlaceOrder>,
                        IWantToRunAfter<PlaceOrderHandler>
                    {
                        public Task Handle(PlaceOrder message, IMessageHandlerContext context)
                        {
                            logger.LogInformation("Order {OrderId}: warehouse notified", message.OrderId);
                            return Task.CompletedTask;
                        }
                    }
                    """,
                    Receiving: true)
            ]
        },
        new()
        {
            Id = "Recoverability",
            Category = FeatureCategory.Reliability,
            DisplayName = "Retry policy",
            Description = "Sets how many times a failing message is retried, immediately and then with a delay, before it goes to the error queue.",
            DocsUrl = "https://docs.particular.net/nservicebus/recoverability/",
            NeedsReceiving = true,
            Code =
                """
                var recoverability = endpointConfiguration.Recoverability();
                recoverability.Immediate(immediate => immediate.NumberOfRetries(3));
                recoverability.Delayed(delayed =>
                {
                    delayed.NumberOfRetries(2);
                    delayed.TimeIncrease(TimeSpan.FromSeconds(15));
                });
                """,
            Notes =
            [
                "An immediate retry runs the handler again straight away. A delayed retry puts the message back with a delay that grows by the time increase each round. When both are used up the message goes to the error queue."
            ]
        },
        new()
        {
            Id = "Outbox",
            Category = FeatureCategory.Reliability,
            DisplayName = "Outbox",
            Description = "Stores a handler's outgoing messages with its data in one transaction, so a redelivered message is neither processed nor sent twice.",
            DocsUrl = "https://docs.particular.net/nservicebus/outbox/",
            NeedsReceiving = true,
            NeedsOutboxStorage = true,
            Code =
                """
                // The outbox takes over from the transport's transaction, which then only covers the receive.
                transport.TransportTransactionMode = TransportTransactionMode.ReceiveOnly;
                endpointConfiguration.EnableOutbox();
                """,
            Notes =
            [
                "With the outbox on, the transport has to receive in `ReceiveOnly` mode, which `Program.cs` sets on the transport: consistency between the data and the outgoing messages comes from the outbox record, not from a transaction spanning the queue and the database."
            ]
        },
        new()
        {
            Id = "Auditing",
            Category = FeatureCategory.Monitoring,
            DisplayName = "Auditing",
            Description = "Copies every successfully processed message to an audit queue, which ServiceControl reads to show message flows.",
            DocsUrl = "https://docs.particular.net/nservicebus/operations/auditing",
            NeedsReceiving = true,
            Code =
                """
                endpointConfiguration.AuditProcessedMessagesTo("audit");
                """,
            Notes =
            [
                "Something has to read the `audit` queue, normally ServiceControl. Left alone it grows without bound."
            ]
        },
        new()
        {
            Id = "AuditFilter",
            Category = FeatureCategory.Monitoring,
            DisplayName = "Audit filter",
            Description = "Chooses which message types go to the audit queue, with an attribute on the message or a default for the rest.",
            DocsUrl = $"{community}/NServiceBus.Community.AuditFilter",
            Community = true,
            NeedsReceiving = true,
            Requires = ["Auditing"],
            Packages = ["NServiceBus.Community.AuditFilter"],
            Usings = ["NServiceBus.AuditFilter"],
            Code = "endpointConfiguration.FilterAuditQueue(FilterResult.{audit-filter-default});",
            Choices =
            [
                new(
                    "audit-filter-default",
                    "Messages with no audit attribute",
                    "What happens to a message type that is marked neither [IncludeInAudit] nor [ExcludeFromAudit].",
                    "IncludeInAudit",
                    [
                        new("IncludeInAudit", "Audit them", "Everything is audited except the types marked [ExcludeFromAudit]."),
                        new("ExcludeFromAudit", "Leave them out", "Only the types marked [IncludeInAudit] are audited.")
                    ])
            ],
            Notes =
            [
                "Mark a message class with `[IncludeInAudit]` or `[ExcludeFromAudit]` to decide for that type, whatever the default is."
            ]
        },
        new()
        {
            Id = "Heartbeats",
            Category = FeatureCategory.Monitoring,
            DisplayName = "Heartbeats",
            Description = "Tells ServiceControl at a regular interval that the endpoint is alive, so ServicePulse can show when it stops.",
            DocsUrl = "https://docs.particular.net/monitoring/heartbeats/",
            NeedsServiceControl = true,
            Packages = ["NServiceBus.Heartbeat"],
            Code =
                """
                endpointConfiguration.SendHeartbeatTo("Particular.ServiceControl");
                """
        },
        new()
        {
            Id = "CustomChecks",
            Category = FeatureCategory.Monitoring,
            DisplayName = "Custom checks",
            Description = "Reports the result of health checks written for this endpoint to ServiceControl: a sample check of free disk space.",
            DocsUrl = "https://docs.particular.net/monitoring/custom-checks/",
            NeedsServiceControl = true,
            Packages = ["NServiceBus.CustomChecks"],
            Code =
                """
                endpointConfiguration.ReportCustomChecksTo("Particular.ServiceControl");
                """,
            Files =
            [
                new(
                    "CustomChecks/DiskSpaceCheck.cs",
                    """
                    using NServiceBus.CustomChecks;

                    namespace {Namespace};

                    /// <summary>Runs every five minutes, and fails when the drive the endpoint runs from is nearly full.</summary>
                    public class DiskSpaceCheck() :
                        CustomCheck("Disk space", "Infrastructure", TimeSpan.FromMinutes(5))
                    {
                        const long minimumFreeBytes = 1_000_000_000;

                        public override Task<CheckResult> PerformCheck(CancellationToken cancellationToken = default)
                        {
                            var drive = new DriveInfo(Path.GetPathRoot(AppContext.BaseDirectory)!);
                            if (drive.AvailableFreeSpace < minimumFreeBytes)
                            {
                                return Task.FromResult(CheckResult.Failed($"Less than 1 GB free on {drive.Name}"));
                            }

                            return Task.FromResult(CheckResult.Pass);
                        }
                    }
                    """)
            ]
        },
        new()
        {
            Id = "Metrics",
            Category = FeatureCategory.Monitoring,
            DisplayName = "Performance metrics",
            Description = "Sends processing time, queue length and throughput to a ServiceControl monitoring instance, for ServicePulse's monitoring view.",
            DocsUrl = "https://docs.particular.net/monitoring/metrics/",
            NeedsReceiving = true,
            NeedsServiceControl = true,
            Packages = ["NServiceBus.Metrics.ServiceControl"],
            Code =
                """
                var serviceControlMetrics = endpointConfiguration.EnableMetrics();
                serviceControlMetrics.SendMetricDataToServiceControl("Particular.Monitoring", TimeSpan.FromSeconds(10));
                """
        },
        new()
        {
            Id = "SagaAudit",
            Category = FeatureCategory.Monitoring,
            DisplayName = "Saga audit",
            Description = "Sends every saga state change to ServiceControl, so the history of a saga can be followed.",
            DocsUrl = "https://docs.particular.net/nservicebus/sagas/saga-audit",
            NeedsReceiving = true,
            NeedsServiceControl = true,
            Requires = ["Sagas"],
            Packages = ["NServiceBus.SagaAudit"],
            Code =
                """
                endpointConfiguration.AuditSagaStateChanges("Particular.ServiceControl");
                """,
            Notes =
            [
                "Saga audit sends a message for every state change, which costs throughput. It is meant for development and for diagnosing a problem, not for every production endpoint."
            ]
        },
        new()
        {
            Id = "OpenTelemetry",
            Category = FeatureCategory.Observability,
            DisplayName = "OpenTelemetry",
            Description = "Exports NServiceBus traces and metrics over OTLP, so a message can be followed across endpoints in any OpenTelemetry backend.",
            DocsUrl = "https://docs.particular.net/nservicebus/operations/opentelemetry",
            Packages = ["OpenTelemetry.Extensions.Hosting", "OpenTelemetry.Exporter.OpenTelemetryProtocol"],
            Usings = ["OpenTelemetry.Metrics", "OpenTelemetry.Resources", "OpenTelemetry.Trace"],
            Host =
                """
                builder.Services.AddOpenTelemetry()
                    .ConfigureResource(resource => resource.AddService("{EndpointName}"))
                    .WithTracing(tracing => tracing
                        .AddSource("NServiceBus.Core")
                        .AddOtlpExporter())
                    .WithMetrics(metering => metering
                        .AddMeter("NServiceBus.Core.Pipeline.Incoming")
                        .AddOtlpExporter());
                """,
            Notes =
            [
                "NServiceBus emits traces and metrics without being asked; this registers its sources with the OpenTelemetry SDK and exports them.",
                "Traces and metrics go to `http://localhost:4317` unless the `OTEL_EXPORTER_OTLP_ENDPOINT` environment variable names another collector."
            ]
        },
        new()
        {
            Id = "Serilog",
            Category = FeatureCategory.Observability,
            DisplayName = "Serilog",
            Description = "Logs through Serilog, with NServiceBus exception details as structured properties, and a log event for each message handled and sent.",
            DocsUrl = $"{community}/NServiceBus.Community.Serilog",
            Community = true,
            Packages = ["NServiceBus.Community.Serilog", "Serilog.Extensions.Logging", "Serilog.Sinks.Console"],
            Usings = ["Serilog"],
            Startup =
                """
                var loggerConfiguration = new LoggerConfiguration();
                loggerConfiguration.Enrich.WithNsbExceptionDetails();
                loggerConfiguration.WriteTo.Console();
                Log.Logger = loggerConfiguration.CreateLogger();
                """,
            Host =
                """
                // Serilog takes the place of the host's own providers, so each event is written once.
                builder.Logging.ClearProviders();
                builder.Logging.AddSerilog();
                """,
            Code =
                """
                var serilogTracing = endpointConfiguration.EnableSerilogTracing(Log.Logger);
                serilogTracing.EnableMessageTracing();
                """,
            Notes =
            [
                "Message tracing writes an event for every message received and sent, enriched with the message id, type, correlation id and conversation id. Inside a handler, `context.Logger()` is a logger that already carries them."
            ]
        },
        new()
        {
            Id = "FluentValidation",
            Category = FeatureCategory.Validation,
            DisplayName = "FluentValidation",
            Description = "Validates messages with FluentValidation as they are received and sent: a sample validator for the sample command.",
            DocsUrl = $"{community}/NServiceBus.Community.Validation#nservicebusfluentvalidation",
            Community = true,
            UsesSamples = true,
            Packages = ["NServiceBus.Community.FluentValidation"],
            Usings = ["FluentValidation"],
            Host = "builder.Services.AddValidatorsFromAssemblyContaining<PlaceOrderValidator>();",
            Code = "endpointConfiguration.UseFluentValidation();",
            Files =
            [
                new(
                    "Validation/PlaceOrderValidator.cs",
                    """
                    using FluentValidation;
                    {UsingCommands}

                    namespace {Namespace};

                    public class PlaceOrderValidator :
                        AbstractValidator<PlaceOrder>
                    {
                        public PlaceOrderValidator() =>
                            RuleFor(order => order.Product)
                                .NotEmpty();
                    }
                    """)
            ],
            Notes =
            [
                "A message that fails validation on the way in is not retried: it goes straight to the error queue. One that fails on the way out throws where it is sent. Pass `incoming: false` or `outgoing: false` to `UseFluentValidation` to check one direction only."
            ]
        },
        new()
        {
            Id = "DataAnnotations",
            Category = FeatureCategory.Validation,
            DisplayName = "DataAnnotations",
            Description = "Validates messages with the attributes in System.ComponentModel.DataAnnotations as they are received and sent.",
            DocsUrl = $"{community}/NServiceBus.Community.Validation#nservicebusdataannotations",
            Community = true,
            Packages = ["NServiceBus.Community.DataAnnotations"],
            Code = "endpointConfiguration.UseDataAnnotationsValidation();",
            Notes =
            [
                "Put the attributes on the message's properties, for example `[Required]`. A message that fails validation on the way in is not retried: it goes straight to the error queue."
            ]
        },
        new()
        {
            Id = "AttachmentsSql",
            Category = FeatureCategory.Attachments,
            DisplayName = "SQL attachments",
            Description = "The claim check pattern on SQL Server: large content travels beside the message in a table, and is streamed when a handler asks for it.",
            DocsUrl = $"{community}/NServiceBus.Community.Attachments/blob/main/docs/sql.md",
            Community = true,
            Packages = ["NServiceBus.Community.Attachments.Sql", "Microsoft.Data.SqlClient"],
            Usings = ["Microsoft.Data.SqlClient", "NServiceBus.Attachments.Sql"],
            Connections = [Connections.SqlServer],
            Infrastructure = [Infrastructure.SqlServer],
            Code =
                """
                endpointConfiguration.EnableAttachments(
                    () => new SqlConnection(sqlServerConnectionString),
                    TimeToKeep.Default);
                """,
            Files = attachmentFiles,
            Notes =
            [
                "Attachments are kept in a `MessageAttachments` table, which installers create. `TimeToKeep.Default` keeps an attachment for ten days, or for twice the message's time to be received when it has one."
            ]
        },
        new()
        {
            Id = "AttachmentsFileShare",
            Category = FeatureCategory.Attachments,
            DisplayName = "File share attachments",
            Description = "The claim check pattern on a directory: large content travels beside the message as a file every endpoint can reach.",
            DocsUrl = $"{community}/NServiceBus.Community.Attachments/blob/main/docs/fileshare.md",
            Community = true,
            Packages = ["NServiceBus.Community.Attachments.FileShare"],
            Usings = ["NServiceBus.Attachments.FileShare"],
            Code =
                """
                // Every endpoint that exchanges attachments has to reach this directory: in production, a share.
                endpointConfiguration.EnableAttachments(
                    Path.Combine(AppContext.BaseDirectory, "attachments"),
                    TimeToKeep.Default);
                """,
            Files = attachmentFiles,
            Notes =
            [
                "The directory under the build output only works while every endpoint runs from one machine. Replace it with a share before deploying. `TimeToKeep.Default` keeps an attachment for ten days, or for twice the message's time to be received when it has one."
            ]
        }
    ];
}
