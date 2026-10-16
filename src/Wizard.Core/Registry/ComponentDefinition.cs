/// <summary>One option of a <see cref="ChoiceDefinition"/>.</summary>
/// <param name="Value">The url value, and what replaces the choice's placeholder in code.</param>
public sealed record ChoiceOption(string Value, string Label, string Hint = "");

/// <summary>
/// A decision a transport, a persistence or a feature raises, asked on the options step. Every choice
/// has a default, so nothing gates on it.
/// </summary>
/// <param name="Id">The url value. Code refers to the value in force as <c>{Id}</c>.</param>
/// <param name="Default">The <see cref="ChoiceOption.Value"/> in force until another is chosen; never written to the url.</param>
public sealed record ChoiceDefinition(
    string Id,
    string Label,
    string Hint,
    string Default,
    IReadOnlyList<ChoiceOption> Options);

/// <summary>
/// A connection string the generated <c>Program.cs</c> reads from configuration. Two components that
/// name the same one share it: a SQL Server transport and SQL Server persistence use one database.
/// </summary>
/// <param name="Name">The key under <c>ConnectionStrings</c> in appsettings.json.</param>
/// <param name="Variable">The local in <c>Program.cs</c> that holds it, which the component's code refers to.</param>
/// <param name="Development">
/// The value for the matching <see cref="InfrastructureDefinition"/> container, or empty when there
/// is nothing local to point at. <c>{Database}</c> and <c>{database}</c> are replaced.
/// </param>
/// <param name="Hint">Where a real value comes from, for the guide.</param>
public sealed record ConnectionDefinition(
    string Name,
    string Variable,
    string Development,
    string Hint);

/// <summary>A container the generated <c>docker-compose.yml</c> starts for local development.</summary>
/// <param name="Id">The compose service name.</param>
/// <param name="Compose">The service's yaml, indented for the <c>services</c> map. <c>{Database}</c> and <c>{database}</c> are replaced.</param>
/// <param name="Notes">What has to be done by hand once it is up, such as creating a database.</param>
public sealed record InfrastructureDefinition(
    string Id,
    string DisplayName,
    string Compose,
    IReadOnlyList<string> Notes);

/// <summary>
/// What a transport, a persistence, a serializer and a feature have in common: the packages they
/// reference, the code they add to <c>Program.cs</c>, and what that code needs around it.
/// </summary>
/// <remarks>
/// Code is a template. <c>{EndpointName}</c>, <c>{Namespace}</c>, <c>{Database}</c> and each choice's
/// <c>{id}</c> are replaced when it is rendered; see <see cref="Template"/>.
/// </remarks>
public abstract record ComponentDefinition
{
    public required string DisplayName { get; init; }

    /// <summary>One or two sentences for the card, without a trailing package name.</summary>
    public required string Description { get; init; }

    public required string DocsUrl { get; init; }

    /// <summary>Package ids the endpoint project references. Each needs a version in package-versions.json.</summary>
    public IReadOnlyList<string> Packages { get; init; } = [];

    /// <summary>
    /// Package ids a dependency brings in at a version with a known vulnerability. The generated
    /// Directory.Packages.props pins them with transitive pinning; nothing references them directly.
    /// Each needs a version in package-versions.json.
    /// </summary>
    public IReadOnlyList<string> TransitivePins { get; init; } = [];

    /// <summary>Namespaces <c>Program.cs</c> imports for <see cref="Code"/>.</summary>
    public IReadOnlyList<string> Usings { get; init; } = [];

    /// <summary>Statements that configure <c>endpointConfiguration</c>.</summary>
    public string Code { get; init; } = "";

    public IReadOnlyList<ConnectionDefinition> Connections { get; init; } = [];

    public IReadOnlyList<InfrastructureDefinition> Infrastructure { get; init; } = [];

    public IReadOnlyList<ChoiceDefinition> Choices { get; init; } = [];

    /// <summary>Things worth knowing, as markdown sentences. They go into the guide and the AI instructions.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}
