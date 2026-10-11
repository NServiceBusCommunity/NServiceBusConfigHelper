/// <summary>
/// Everything the wizard knows. The UI mutates it; <see cref="WizardStateUrl"/> round-trips it through
/// the query string so every point in a flow is a bookmark; the generators read it.
/// </summary>
public sealed record WizardState
{
    public const string DefaultSolutionName = "SampleEndpoint";

    public Flow Flow { get; set; }

    public Hosting? Hosting { get; set; }
    public Transport? Transport { get; set; }
    public Persistence? Persistence { get; set; }
    public Serializer? Serializer { get; set; }
    public TestFramework? TestFramework { get; set; }

    /// <summary>The solution, the endpoint project, and the endpoint's name on the transport.</summary>
    public string SolutionName { get; set; } = DefaultSolutionName;

    /// <summary>Whether the endpoint creates its queues and tables on startup. On unless switched off.</summary>
    public bool Installers { get; set; } = true;

    /// <summary>
    /// Feature ids to include, as a set so membership is cheap; emitted in registry order everywhere,
    /// so the url and the generated output do not depend on insertion order. Collections are replaced
    /// rather than mutated, so <c>with { }</c> copies do not share them.
    /// </summary>
    public IReadOnlySet<string> SelectedFeatures { get; set; } = emptySet;

    /// <summary>Choice id to value, for the choices the selection raises. A default is never stored.</summary>
    public IReadOnlyDictionary<string, string> Choices { get; set; } = emptyChoices;

    /// <summary>The current step id; see <see cref="FlowSteps"/>.</summary>
    public string Step { get; set; } = "";

    static IReadOnlySet<string> emptySet = new HashSet<string>(StringComparer.Ordinal);
    static IReadOnlyDictionary<string, string> emptyChoices = new Dictionary<string, string>(StringComparer.Ordinal);

    public bool Has(string featureId) =>
        SelectedFeatures.Contains(featureId);

    public void Select(string featureId, bool selected)
    {
        var selection = new HashSet<string>(SelectedFeatures, StringComparer.Ordinal);
        if (selected)
        {
            selection.Add(featureId);
        }
        else
        {
            selection.Remove(featureId);
        }

        SelectedFeatures = selection;
    }

    public void SetChoice(string choiceId, string value) =>
        Choices = new Dictionary<string, string>(Choices, StringComparer.Ordinal)
        {
            [choiceId] = value
        };

    /// <summary>
    /// Why a feature cannot be selected with the answers given so far, or null when it can. An answer
    /// not given yet rules nothing out.
    /// </summary>
    public string? Unavailable(string featureId)
    {
        var definition = Features.ById[featureId];
        if (definition.NeedsOutboxStorage &&
            Persistence is { } persistence &&
            !Persistences.For(persistence).SupportsOutbox)
        {
            return $"{persistence.Name()} persistence cannot store the outbox. Choose another persistence to use it.";
        }

        return null;
    }

    /// <summary>
    /// The choices in force: the transport's, the persistence's, then each selected feature's, in the
    /// order the options step shows them.
    /// </summary>
    public IReadOnlyList<ChoiceDefinition> AvailableChoices()
    {
        var choices = new List<ChoiceDefinition>();
        if (Transport is { } transport)
        {
            choices.AddRange(Transports.For(transport).Choices);
        }

        if (Persistence is { } persistence)
        {
            choices.AddRange(Persistences.For(persistence).Choices);
        }

        choices.AddRange(Features.Selected(this).SelectMany(_ => _.Choices));
        return choices;
    }

    /// <summary>The value of a choice: the one chosen, or its default.</summary>
    public string ChoiceValue(ChoiceDefinition choice) =>
        Choices.GetValueOrDefault(choice.Id, choice.Default);

    /// <summary>
    /// Drops values that cannot apply: answers to questions the flow does not ask, a solution name that
    /// is not a usable project name, features the registry does not have or the persistence rules out,
    /// choices nothing selected raises, and a step id the flow does not have. Called after every parse
    /// and every change, so the url holds exactly what the state holds.
    /// </summary>
    public void Normalize()
    {
        SolutionName = SolutionNames.Clean(SolutionName);

        // Adding to an existing endpoint: only what the features' code depends on is asked.
        if (Flow == Flow.Add)
        {
            Hosting = null;
            Serializer = null;
            TestFramework = null;
            SolutionName = DefaultSolutionName;
            Installers = true;
        }

        SelectedFeatures = new HashSet<string>(
            SelectedFeatures.Where(_ => Features.Contains(_) && Unavailable(_) == null),
            StringComparer.Ordinal);

        var available = AvailableChoices().ToDictionary(_ => _.Id, StringComparer.Ordinal);
        Choices = new Dictionary<string, string>(
            Choices.Where(_ =>
                available.TryGetValue(_.Key, out var choice) &&
                choice.Options.Any(option => option.Value == _.Value) &&
                choice.Default != _.Value),
            StringComparer.Ordinal);

        Step = FlowSteps.Nearest(this, Step);
    }

    /// <summary>
    /// A record compares its members with <see cref="object.Equals(object?)"/>, which for a set or a
    /// dictionary is reference equality, so two states holding the same selection would differ. Every
    /// comparison here is about what the state says, which is also what the url carries.
    /// </summary>
    public bool Equals(WizardState? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Flow == other.Flow &&
               Hosting == other.Hosting &&
               Transport == other.Transport &&
               Persistence == other.Persistence &&
               Serializer == other.Serializer &&
               TestFramework == other.TestFramework &&
               SolutionName == other.SolutionName &&
               Installers == other.Installers &&
               Step == other.Step &&
               SelectedFeatures.SetEquals(other.SelectedFeatures) &&
               Choices.Count == other.Choices.Count &&
               Choices.All(_ => other.Choices.TryGetValue(_.Key, out var value) && value == _.Value);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Flow);
        hash.Add(Hosting);
        hash.Add(Transport);
        hash.Add(Persistence);
        hash.Add(Serializer);
        hash.Add(TestFramework);
        hash.Add(SolutionName);
        hash.Add(Installers);
        hash.Add(Step);
        hash.Add(SelectedFeatures.Count);
        hash.Add(Choices.Count);
        return hash.ToHashCode();
    }
}
