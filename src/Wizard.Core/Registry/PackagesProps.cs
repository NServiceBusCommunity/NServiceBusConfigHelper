using System.Xml;
using System.Xml.Linq;

/// <summary>
/// Finds what an existing endpoint runs on from its solution's <c>Directory.Packages.props</c>, so
/// the add flow's first two steps can be answered from the file.
/// </summary>
public static class PackagesProps
{
    public const string FileName = "Directory.Packages.props";

    static readonly string[] itemNames = ["PackageVersion", "GlobalPackageReference"];

    /// <summary>
    /// The transport and the persistence the file's packages point at, each null when the packages
    /// do not single one out. False when the file is not xml.
    /// </summary>
    /// <remarks>
    /// The learning transport and persistence ship in the NServiceBus package, so nothing identifies
    /// them. A file that lists two transports, as one shared by several endpoints can, names neither.
    /// </remarks>
    public static bool TryRead(string xml, out Transport? transport, out Persistence? persistence)
    {
        transport = null;
        persistence = null;

        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (XmlException)
        {
            return false;
        }

        var packages = document
            .Descendants()
            .Where(_ => itemNames.Contains(_.Name.LocalName))
            .SelectMany(PackageIds)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var transports = Transports.All.Where(_ => Referenced(_, packages)).ToList();
        if (transports.Count == 1)
        {
            transport = transports[0].Id;
        }

        var persistences = Persistences.All.Where(_ => Referenced(_, packages)).ToList();
        // Non-durable persistence next to a real one is there for the tests.
        if (persistences.Count > 1)
        {
            persistences = persistences.Where(_ => !_.DevelopmentOnly).ToList();
        }

        if (persistences.Count == 1)
        {
            persistence = persistences[0].Id;
        }

        return true;
    }

    /// <summary>
    /// Every package, not only the first: the SQL persistences share NServiceBus.Persistence.Sql, and
    /// it is the database client next to it that tells them apart.
    /// </summary>
    static bool Referenced(ComponentDefinition definition, HashSet<string> packages) =>
        definition.Packages.Count > 0 &&
        definition.Packages.All(packages.Contains);

    static IEnumerable<string> PackageIds(XElement item)
    {
        var value = item.Attribute("Include")?.Value ?? item.Attribute("Update")?.Value ?? "";
        return value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
