/// <param name="Id">The url value of <c>step</c>.</param>
/// <param name="Summary">The chosen value shown next to a completed step in the breadcrumb, or null when nothing is chosen.</param>
/// <param name="IsComplete">Whether the step's input is valid, which gates moving past it.</param>
public sealed record StepDefinition(
    string Id,
    string Title,
    Func<WizardState, string?> Summary,
    Func<WizardState, bool> IsComplete);

/// <summary>The ordered steps of each flow.</summary>
public static class FlowSteps
{
    public static readonly StepDefinition Hosting = new(
        "host",
        "Hosting",
        _ => _.Hosting?.Name(),
        _ => _.Hosting != null);

    public static readonly StepDefinition Transport = new(
        "transport",
        "Transport",
        _ => _.Transport?.Name(),
        _ => _.Transport != null);

    public static readonly StepDefinition Persistence = new(
        "persistence",
        "Persistence",
        _ => _.Persistence?.Name(),
        _ => _.Persistence != null);

    public static readonly StepDefinition Serializer = new(
        "serializer",
        "Serializer",
        _ => _.Serializer?.Name(),
        _ => _.Serializer != null);

    public static readonly StepDefinition Tests = new(
        "tests",
        "Tests",
        _ => _.TestFramework?.Name(),
        _ => _.TestFramework != null);

    /// <summary>
    /// Optional for a new endpoint. Adding to an existing one needs something to add. Either way a
    /// conflict has to be resolved here: the generated code would not compile, or would do nothing.
    /// </summary>
    public static readonly StepDefinition FeaturesStep = new(
        "features",
        "Features",
        FeatureSummary,
        _ => !InteractionRules.Blocked(_) &&
             (_.Flow == Flow.New || _.SelectedFeatures.Count > 0));

    /// <summary>Every option has a default, so there is nothing to gate on.</summary>
    public static readonly StepDefinition Options = new(
        "options",
        "Options",
        OptionsSummary,
        _ => true);

    public static readonly StepDefinition Output = new(
        "output",
        "Result",
        _ => null,
        _ => false);

    static string? FeatureSummary(WizardState state)
    {
        var selected = Features.Selected(state);
        if (selected.Count == 0)
        {
            return "None";
        }

        if (selected.Count > 2)
        {
            return $"{selected.Count} features";
        }

        return string.Join(", ", selected.Select(_ => _.DisplayName));
    }

    static string? OptionsSummary(WizardState state)
    {
        var parts = new List<string>();
        if (!state.Installers)
        {
            parts.Add("No installers");
        }

        if (state.Choices.Count > 0)
        {
            parts.Add($"{state.Choices.Count} changed");
        }

        if (parts.Count == 0)
        {
            return "Defaults";
        }

        return string.Join(", ", parts);
    }

    static IReadOnlyList<StepDefinition> newFlow =
    [
        Hosting,
        Transport,
        Persistence,
        Serializer,
        Tests,
        FeaturesStep,
        Options,
        Output
    ];

    // Adding to an existing endpoint: only the transport and the persistence are asked, because they
    // decide which features are available and what a feature's code refers to.
    static IReadOnlyList<StepDefinition> addFlow =
    [
        Transport,
        Persistence,
        FeaturesStep,
        Options,
        Output
    ];

    public static IReadOnlyList<StepDefinition> For(WizardState state) =>
        For(state.Flow);

    /// <summary>The steps of a flow, in order.</summary>
    public static IReadOnlyList<StepDefinition> For(Flow flow) =>
        flow switch
        {
            Flow.New => newFlow,
            Flow.Add => addFlow,
            _ => throw new ArgumentOutOfRangeException(nameof(flow), flow, null)
        };

    /// <summary>The step asked for, or the flow's first step when the flow does not have it.</summary>
    public static string Nearest(WizardState state, string stepId)
    {
        var steps = For(state);
        if (steps.Any(_ => _.Id == stepId))
        {
            return stepId;
        }

        return steps[0].Id;
    }

    /// <summary>
    /// The step a visitor can be on: the requested one, unless an earlier step is incomplete, in which
    /// case the first incomplete step. Keeps a shared or hand-edited url from landing past a gap.
    /// </summary>
    public static StepDefinition Reachable(WizardState state)
    {
        var steps = For(state);
        foreach (var step in steps)
        {
            if (step.Id == state.Step ||
                !step.IsComplete(state))
            {
                return step;
            }
        }

        return steps[^1];
    }
}
