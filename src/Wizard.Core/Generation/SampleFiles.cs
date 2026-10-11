/// <summary>
/// The sample every new endpoint gets, so that it does something the moment it runs: a command, the
/// event its handler publishes, and the one class that sends the command. Small enough to delete.
/// </summary>
public static class SampleFiles
{
    /// <summary>The endpoint a send-only endpoint's command is routed to. A stand-in: the solution does not contain it.</summary>
    public const string Destination = "Sales";

    /// <summary>The files of the endpoint project, with paths relative to it.</summary>
    public static IEnumerable<GeneratedFile> Endpoint(Plan plan)
    {
        yield return File("{CommandsFolder}/PlaceOrder.cs", PlaceOrder(plan), plan);
        yield return File("OrderSubmitter.cs", OrderSubmitter(plan), plan);
        if (plan.Hosting == Hosting.Worker)
        {
            yield return File("SampleOrders.cs", sampleOrders, plan);
        }

        if (!plan.Receives)
        {
            yield break;
        }

        foreach (var file in Handling(plan))
        {
            yield return file;
        }
    }

    /// <summary>
    /// The messages and the handler that feature samples refer to. When features are added to an
    /// existing endpoint these come along as placeholders for its own.
    /// </summary>
    public static IEnumerable<GeneratedFile> Placeholders(Plan plan)
    {
        yield return File("{CommandsFolder}/PlaceOrder.cs", PlaceOrder(plan), plan);
        yield return File("{EventsFolder}/OrderPlaced.cs", orderPlaced, plan);
        yield return File("Handlers/PlaceOrderHandler.cs", placeOrderHandler, plan);
    }

    static IEnumerable<GeneratedFile> Handling(Plan plan)
    {
        yield return File("{EventsFolder}/OrderPlaced.cs", orderPlaced, plan);
        yield return File("Handlers/PlaceOrderHandler.cs", placeOrderHandler, plan);
        yield return File("Handlers/OrderPlacedHandler.cs", orderPlacedHandler, plan);
    }

    /// <summary>A feature's own files, leaving out handlers and sagas on an endpoint that never receives.</summary>
    public static IEnumerable<GeneratedFile> Feature(FeatureDefinition feature, Plan plan) =>
        feature.Files
            .Where(_ => plan.Receives || !_.Receiving)
            .Select(_ => File(_.Path, _.Content, plan));

    static GeneratedFile File(string path, string content, Plan plan) =>
        new(Template.Render(path, plan), Template.Render(content, plan) + "\n");

    static string PlaceOrder(Plan plan)
    {
        // DataAnnotations validates the attributes on a message, so the sample command gets one.
        if (plan.Has(Features.DataAnnotations))
        {
            return
                """
                using System.ComponentModel.DataAnnotations;

                namespace {Commands};

                /// <summary>A command: a request for one endpoint to do something. It has exactly one handler.</summary>
                public class PlaceOrder{ICommand}
                {
                    public Guid OrderId { get; set; }

                    [Required]
                    public string Product { get; set; } = "";
                }
                """;
        }

        return
            """
            namespace {Commands};

            /// <summary>A command: a request for one endpoint to do something. It has exactly one handler.</summary>
            public class PlaceOrder{ICommand}
            {
                public Guid OrderId { get; set; }
                public string Product { get; set; } = "";
            }
            """;
    }

    const string orderPlaced =
        """
        namespace {Events};

        /// <summary>An event: something that has happened. Any number of endpoints can subscribe to it.</summary>
        public class OrderPlaced{IEvent}
        {
            public Guid OrderId { get; set; }
        }
        """;

    const string placeOrderHandler =
        """
        {UsingMessages}

        namespace {Namespace};

        public class PlaceOrderHandler(ILogger<PlaceOrderHandler> logger) :
            IHandleMessages<PlaceOrder>
        {
            public async Task Handle(PlaceOrder message, IMessageHandlerContext context)
            {
                logger.LogInformation("Order {OrderId}: placed for {Product}", message.OrderId, message.Product);

                // Publishing through the handler's context makes the event part of handling this message:
                // it is only sent once the handler has succeeded.
                await context.Publish(
                    new OrderPlaced
                    {
                        OrderId = message.OrderId
                    });
            }
        }
        """;

    const string orderPlacedHandler =
        """
        {UsingEvents}

        namespace {Namespace};

        /// <summary>Handling an event is what subscribes the endpoint to it.</summary>
        public class OrderPlacedHandler(ILogger<OrderPlacedHandler> logger) :
            IHandleMessages<OrderPlaced>
        {
            public Task Handle(OrderPlaced message, IMessageHandlerContext context)
            {
                logger.LogInformation("Order {OrderId}: event received", message.OrderId);
                return Task.CompletedTask;
            }
        }
        """;

    static string OrderSubmitter(Plan plan)
    {
        var send =
            """
                    // SendLocal puts the command on this endpoint's own queue.
                    await session.SendLocal(command, cancellationToken);
            """;
        if (!plan.Receives)
        {
            send =
                """
                        // Where the command goes is decided by its route in Program.cs.
                        await session.Send(command, cancellationToken);
                """;
        }

        return
            $$"""
              {UsingCommands}

              namespace {Namespace};

              /// <summary>
              /// The one place that sends <see cref="PlaceOrder"/>. Code outside a handler sends through
              /// <see cref="IMessageSession"/>; a handler uses the context it is given instead.
              /// </summary>
              public class OrderSubmitter(IMessageSession session)
              {
                  public async Task<Guid> Submit(string product, CancellationToken cancellationToken)
                  {
                      var command = new PlaceOrder
                      {
                          OrderId = Guid.NewGuid(),
                          Product = product
                      };
              {{send.Replace("\r\n", "\n").TrimEnd('\n')}}
                      return command.OrderId;
                  }
              }
              """;
    }

    const string sampleOrders =
        """
        namespace {Namespace};

        /// <summary>
        /// Submits one order when the host starts, so there is a message to watch. Delete it, and its
        /// registration in Program.cs, once the endpoint has real work.
        /// </summary>
        public class SampleOrders(OrderSubmitter submitter, ILogger<SampleOrders> logger) :
            BackgroundService
        {
            protected override async Task ExecuteAsync(CancellationToken stoppingToken)
            {
                var orderId = await submitter.Submit("Widget", stoppingToken);
                logger.LogInformation("Order {OrderId}: submitted", orderId);
            }
        }
        """;
}
