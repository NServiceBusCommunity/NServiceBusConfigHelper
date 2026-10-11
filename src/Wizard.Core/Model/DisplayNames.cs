public static class DisplayNames
{
    public static string Name(this Flow flow) =>
        flow switch
        {
            Flow.New => "New endpoint",
            Flow.Add => "Add features",
            _ => throw new ArgumentOutOfRangeException(nameof(flow), flow, null)
        };

    public static string Name(this Hosting hosting) =>
        hosting switch
        {
            Hosting.Worker => "Worker service",
            Hosting.AspNetCore => "ASP.NET Core",
            _ => throw new ArgumentOutOfRangeException(nameof(hosting), hosting, null)
        };

    public static string Name(this Transport transport) =>
        Transports.For(transport).DisplayName;

    public static string Name(this Persistence persistence) =>
        Persistences.For(persistence).DisplayName;

    public static string Name(this Serializer serializer) =>
        Serializers.For(serializer).DisplayName;

    public static string Name(this TestFramework framework) =>
        framework switch
        {
            TestFramework.XunitV3 => "xUnit v3",
            TestFramework.NUnit => "NUnit",
            TestFramework.TUnit => "TUnit",
            TestFramework.MSTest => "MSTest",
            TestFramework.None => "No tests",
            _ => throw new ArgumentOutOfRangeException(nameof(framework), framework, null)
        };

    /// <summary>"a", "a and b", "a, b and c".</summary>
    public static string Join(IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return "";
        }

        if (items.Count == 1)
        {
            return items[0];
        }

        return $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}";
    }
}
