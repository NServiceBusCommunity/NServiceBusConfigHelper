/// <summary>
/// Everything that differs per test framework in the generated test project. Each runs as a
/// Microsoft.Testing.Platform app, which is what <c>dotnet test</c> uses through the runner set in the
/// generated <c>global.json</c>.
/// </summary>
/// <param name="Packages">Test project packages, besides NServiceBus.Testing.</param>
/// <param name="Properties">Test project properties that switch on Microsoft.Testing.Platform.</param>
/// <param name="TestAttribute">The attribute marking a method as a test.</param>
/// <param name="Equal">An assertion that two values are equal: <c>{0}</c> is the expected one, <c>{1}</c> the actual.</param>
/// <param name="True">An assertion that <c>{0}</c> is true.</param>
/// <param name="Cancellation">The cancellation token a test passes on.</param>
public sealed record TestFrameworkInfo(
    TestFramework Framework,
    IReadOnlyList<string> Packages,
    IReadOnlyList<(string Name, string Value)> Properties,
    string TestAttribute,
    string Equal,
    string True,
    string Cancellation)
{
    /// <summary>Namespaces a test file imports for the framework, where the package does not add them itself.</summary>
    public IReadOnlyList<string> Usings { get; init; } = [];

    /// <summary>Attributes on a generated test class, such as MSTest's <c>[TestClass]</c>.</summary>
    public IReadOnlyList<string> ClassAttributes { get; init; } = [];

    /// <summary>A file of assembly attributes the framework's analyzers ask for, or null.</summary>
    public string? AssemblyInfo { get; init; }

    public string AssertEqual(string expected, string actual) =>
        string.Format(CultureInfo.InvariantCulture, Equal, expected, actual);

    public string AssertTrue(string condition) =>
        string.Format(CultureInfo.InvariantCulture, True, condition);

    /// <returns>Null for <see cref="TestFramework.None"/>: no test project is generated.</returns>
    public static TestFrameworkInfo? For(TestFramework framework) =>
        framework switch
        {
            TestFramework.XunitV3 => xunitV3,
            TestFramework.NUnit => nunit,
            TestFramework.TUnit => tunit,
            TestFramework.MSTest => msTest,
            TestFramework.None => null,
            _ => throw new ArgumentOutOfRangeException(nameof(framework), framework, null)
        };

    static TestFrameworkInfo xunitV3 = new(
        TestFramework.XunitV3,
        ["xunit.v3"],
        [("OutputType", "Exe")],
        "[Fact]",
        "Assert.Equal({0}, {1});",
        "Assert.True({0});",
        // xunit's analyzers ask for the test's own token wherever one is accepted
        "TestContext.Current.CancellationToken")
    {
        Usings = ["Xunit"]
    };

    static TestFrameworkInfo nunit = new(
        TestFramework.NUnit,
        ["NUnit", "NUnit3TestAdapter"],
        [("OutputType", "Exe"), ("EnableNUnitRunner", "true")],
        "[Test]",
        "Assert.That({1}, Is.EqualTo({0}));",
        "Assert.That({0}, Is.True);",
        "CancellationToken.None")
    {
        Usings = ["NUnit.Framework"],
        ClassAttributes = ["[TestFixture]"]
    };

    // TUnit sets OutputType itself and is always a Microsoft.Testing.Platform app
    static TestFrameworkInfo tunit = new(
        TestFramework.TUnit,
        ["TUnit"],
        [],
        "[Test]",
        "await Assert.That({1}).IsEqualTo({0});",
        "await Assert.That({0}).IsTrue();",
        "CancellationToken.None");

    static TestFrameworkInfo msTest = new(
        TestFramework.MSTest,
        ["MSTest.TestAdapter", "MSTest.TestFramework"],
        [("OutputType", "Exe"), ("EnableMSTestRunner", "true")],
        "[TestMethod]",
        "Assert.AreEqual({0}, {1});",
        "Assert.IsTrue({0});",
        "CancellationToken.None")
    {
        Usings = ["Microsoft.VisualStudio.TestTools.UnitTesting"],
        ClassAttributes = ["[TestClass]"],
        AssemblyInfo =
            """
            // MSTest asks every test assembly to say whether its tests may run in parallel.
            [assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]
            """
    };
}
