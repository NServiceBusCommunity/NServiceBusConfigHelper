/// <summary>
/// Canonical urls into the deployed wizard. One place so the generators, the docs and the tests
/// can't drift. It lives in Core because generated files link back to the wizard state they came from.
/// </summary>
public static class WizardLinks
{
    public const string Base = "https://nservicebuscommunity.github.io/NServiceBusConfigHelper";
    public const string New = Base + "/new";
    public const string Add = Base + "/add";

    /// <summary>The add flow seeded with one feature, for an extension's readme to link to.</summary>
    public static string AddFeature(string featureId) =>
        $"{Add}/{Uri.EscapeDataString(featureId.Trim())}";
}
