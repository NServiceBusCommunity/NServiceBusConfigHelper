using Microsoft.AspNetCore.Components.Routing;

namespace Wizard.Web.Components;

/// <summary>
/// One of the two flows. The state lives in the url: it is parsed on load and on browser
/// back/forward, and every change is written back. Moving between steps adds a history entry, so back
/// goes to the previous step; editing a value replaces the current entry. On first load the browser's
/// memory fills in whatever the url leaves out.
/// </summary>
public partial class FlowPage : IDisposable
{
    [Inject] public NavigationManager Navigation { get; set; } = null!;
    [Inject] public TimeProvider Time { get; set; } = null!;
    [Inject] public BrowserStorage Storage { get; set; } = null!;
    [Inject] public IJSRuntime JS { get; set; } = null!;

    [Parameter, EditorRequired] public Flow Flow { get; set; }

    /// <summary>A feature to start with selected, from a deep link such as <c>/add/Serilog</c>.</summary>
    [Parameter] public string? SeedFeature { get; set; }

    public WizardState State { get; private set; } = new();

    Date today;
    string lastUrl = "";
    bool ready;
    bool scrollToTop;
    Restored restored = new(false, false);
    Remembered written = Remembered.None;

    IReadOnlyList<StepDefinition> Steps => FlowSteps.For(State);

    StepDefinition Current => Steps.Single(_ => _.Id == State.Step);

    int CurrentIndex => Steps.ToList().IndexOf(Current);

    StepDefinition? PreviousStep
    {
        get
        {
            if (CurrentIndex == 0)
            {
                return null;
            }

            return Steps[CurrentIndex - 1];
        }
    }

    StepDefinition? NextStep
    {
        get
        {
            if (CurrentIndex == Steps.Count - 1)
            {
                return null;
            }

            return Steps[CurrentIndex + 1];
        }
    }

    protected override async Task OnInitializedAsync()
    {
        today = Date.FromDateTime(Time.GetUtcNow().UtcDateTime);
        var query = new Uri(Navigation.Uri).Query;
        State = WizardStateUrl.Parse(Flow, query);

        if (SeedFeature is { } seed &&
            Features.Contains(seed))
        {
            State.Select(seed, true);
            State.Normalize();
        }

        var remembered = await Storage.ReadAsync();
        restored = BrowserMemory.Seed(State, query, remembered);
        written = remembered;
        State.Step = FlowSteps.Reachable(State).Id;

        // A url that landed past an incomplete step, held values the state dropped, or left out
        // something the browser remembered, is corrected in place, so the link on screen is complete.
        var url = WizardStateUrl.ToRelativeUrl(State);
        if (url != Navigation.ToBaseRelativePath(Navigation.Uri))
        {
            Navigate(url, replace: true);
        }

        Navigation.LocationChanged += LocationChanged;
        ready = true;
    }

    void LocationChanged(object? sender, LocationChangedEventArgs args)
    {
        // our own navigation echoes back here; only back/forward or an edited url needs a reload
        if (Navigation.ToBaseRelativePath(args.Location) == lastUrl)
        {
            return;
        }

        State = WizardStateUrl.Parse(Flow, new Uri(Navigation.Uri).Query);
        State.Step = FlowSteps.Reachable(State).Id;
        lastUrl = WizardStateUrl.ToRelativeUrl(State);
        StateHasChanged();
    }

    void Navigate(string url, bool replace)
    {
        lastUrl = url;
        Navigation.NavigateTo(url, replace: replace);
    }

    Task Set(Action<WizardState> change)
    {
        change(State);
        return Changed();
    }

    /// <summary>The first answer on a step moves on to the next one; changing an earlier answer stays put.</summary>
    async Task SetAndAdvance(bool firstAnswer, Action<WizardState> change)
    {
        await Set(change);
        if (firstAnswer &&
            NextStep is { } next)
        {
            GoTo(next.Id);
        }
    }

    Task Changed()
    {
        State.Normalize();
        Navigate(WizardStateUrl.ToRelativeUrl(State), replace: true);
        return Remember();
    }

    /// <summary>Writes only what changed, so a step that never touches an answer never rewrites it.</summary>
    Task Remember()
    {
        var current = BrowserMemory.For(State);
        var changes = new Remembered(
            Difference(current.Transport, written.Transport),
            Difference(current.Persistence, written.Persistence));
        if (changes == Remembered.None)
        {
            return Task.CompletedTask;
        }

        written = new(current.Transport ?? written.Transport, current.Persistence ?? written.Persistence);
        return Storage.WriteAsync(changes);
    }

    /// <summary>The value to write, or null when nothing changed.</summary>
    static string? Difference(string? current, string? kept)
    {
        if (current == kept)
        {
            return null;
        }

        return current;
    }

    void GoTo(string stepId)
    {
        State.Step = stepId;
        State.Step = FlowSteps.Reachable(State).Id;
        Navigate(WizardStateUrl.ToRelativeUrl(State), replace: false);
        scrollToTop = true;
    }

    /// <summary>Moves on from the features step with nothing selected.</summary>
    async Task Skip(string nextId)
    {
        State.SelectedFeatures = new HashSet<string>(StringComparer.Ordinal);
        await Changed();
        GoTo(nextId);
    }

    // Moving between steps stays on the same page, so the browser keeps the scroll position of the
    // step just left, which after Next is usually its bottom.
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!scrollToTop)
        {
            return;
        }

        scrollToTop = false;
        await JS.InvokeVoidAsync("scrollTo", 0, 0);
    }

    public void Dispose() =>
        Navigation.LocationChanged -= LocationChanged;

    static IReadOnlyList<Choice<Hosting>> hostingChoices =
    [
        new(Hosting.Worker, "Worker service", "A console application that only processes messages. Runs as a service, or in a container."),
        new(Hosting.AspNetCore, "ASP.NET Core", "A web application that also hosts the endpoint, with an http endpoint that sends a command.")
    ];

    static IReadOnlyList<Choice<Transport>> transportChoices =
        Transports.All
            .Select(_ => new Choice<Transport>(_.Id, _.DisplayName, _.Description))
            .ToList();

    /// <summary>The persistence that lives in the same service as the chosen transport is offered first, and says why.</summary>
    IReadOnlyList<Choice<Persistence>> PersistenceChoices
    {
        get
        {
            Persistence? pair = null;
            if (State.Transport is { } transport)
            {
                pair = Transports.For(transport).PairsWith;
            }

            return Persistences.All
                .OrderBy(_ => _.Id != pair)
                .Select(_ => new Choice<Persistence>(_.Id, _.DisplayName, PersistenceDescription(_, pair)))
                .ToList();
        }
    }

    string PersistenceDescription(PersistenceDefinition persistence, Persistence? pair)
    {
        if (persistence.Id != pair)
        {
            return persistence.Description;
        }

        return $"{persistence.Description} Pairs with the {State.Transport!.Value.Name()} transport.";
    }

    static IReadOnlyList<Choice<Serializer>> serializerChoices =
        Serializers.All
            .Select(_ => new Choice<Serializer>(_.Id, _.DisplayName, _.Description))
            .ToList();

    static IReadOnlyList<Choice<TestFramework>> testFrameworkChoices =
    [
        new(TestFramework.XunitV3, "xUnit v3"),
        new(TestFramework.NUnit, "NUnit"),
        new(TestFramework.TUnit, "TUnit"),
        new(TestFramework.MSTest, "MSTest"),
        new(TestFramework.None, "No tests", "Only the endpoint project is generated.")
    ];
}
