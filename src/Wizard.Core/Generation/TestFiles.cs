/// <summary>
/// The test project: one test per kind of NServiceBus.Testing fake. A message session for code that
/// sends from outside a handler, a handler context for a handler, and the same context for a saga.
/// </summary>
public static class TestFiles
{
    /// <summary>The files of the test project, with paths relative to it. Empty when no tests are wanted.</summary>
    public static IEnumerable<GeneratedFile> For(Plan plan)
    {
        if (plan.Tests is not { } tests)
        {
            yield break;
        }

        if (tests.AssemblyInfo is { } assemblyInfo)
        {
            yield return new("AssemblyInfo.cs", Normalize(assemblyInfo));
        }

        yield return new("OrderSubmitterTests.cs", Submitter(plan, tests));
        if (!plan.Receives)
        {
            yield break;
        }

        yield return new("PlaceOrderHandlerTests.cs", Handler(plan, tests));
        if (plan.Has(Features.Sagas))
        {
            yield return new("OrderSagaTests.cs", Saga(plan, tests));
        }
    }

    static string Submitter(Plan plan, TestFrameworkInfo tests) =>
        TestClass(
            plan,
            tests,
            "OrderSubmitterTests",
            [plan.CommandsNamespace, "NServiceBus.Testing"],
            "SendsPlaceOrder",
            $"""
             var session = new TestableMessageSession();
             var submitter = new OrderSubmitter(session);

             var orderId = await submitter.Submit("Widget", {tests.Cancellation});

             var command = session.SentMessages.Single().Message<PlaceOrder>();
             {tests.AssertEqual("orderId", "command.OrderId")}
             {tests.AssertEqual("\"Widget\"", "command.Product")}
             """);

    static string Handler(Plan plan, TestFrameworkInfo tests) =>
        TestClass(
            plan,
            tests,
            "PlaceOrderHandlerTests",
            [plan.CommandsNamespace, plan.EventsNamespace, "Microsoft.Extensions.Logging.Abstractions", "NServiceBus.Testing"],
            "PublishesOrderPlaced",
            $$"""
              var handler = new PlaceOrderHandler(NullLogger<PlaceOrderHandler>.Instance);
              var context = new TestableMessageHandlerContext();
              var command = new PlaceOrder
              {
                  OrderId = Guid.NewGuid(),
                  Product = "Widget"
              };

              await handler.Handle(command, context);

              var published = context.PublishedMessages.Single().Message<OrderPlaced>();
              {{tests.AssertEqual("command.OrderId", "published.OrderId")}}
              """);

    static string Saga(Plan plan, TestFrameworkInfo tests) =>
        TestClass(
            plan,
            tests,
            "OrderSagaTests",
            [plan.EventsNamespace, "Microsoft.Extensions.Logging.Abstractions", "NServiceBus.Testing"],
            "RequestsATimeoutThenCompletes",
            $$"""
              var saga = new OrderSaga(NullLogger<OrderSaga>.Instance)
              {
                  Data = new()
                  {
                      OrderId = Guid.NewGuid()
                  }
              };
              var context = new TestableMessageHandlerContext();
              var placed = new OrderPlaced
              {
                  OrderId = saga.Data.OrderId
              };

              await saga.Handle(placed, context);
              var timeout = (CancellationWindowElapsed) context.TimeoutMessages.Single().Message;

              await saga.Timeout(timeout, context);
              {{tests.AssertTrue("saga.Completed")}}
              """);

    static string TestClass(
        Plan plan,
        TestFrameworkInfo tests,
        string name,
        IReadOnlyList<string> usings,
        string method,
        string body)
    {
        var builder = new StringBuilder();
        foreach (var import in usings.Concat(tests.Usings).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            builder.Append($"using {import};\n");
        }

        builder.Append($"\nnamespace {plan.Namespace}.Tests;\n\n");
        foreach (var attribute in tests.ClassAttributes)
        {
            builder.Append($"{attribute}\n");
        }

        builder.Append($"public class {name}\n{{\n");
        builder.Append($"    {tests.TestAttribute}\n");
        builder.Append($"    public async Task {method}()\n    {{\n");
        builder.Append(Template.Indent(Normalize(body).TrimEnd('\n'), "        "));
        builder.Append("\n    }\n}\n");
        return builder.ToString();
    }

    static string Normalize(string text) =>
        text.Replace("\r\n", "\n").Trim('\n') + "\n";
}
