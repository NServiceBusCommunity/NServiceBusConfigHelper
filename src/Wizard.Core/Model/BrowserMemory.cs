/// <summary>What the browser remembers between visits, each as it appears in a url.</summary>
/// <param name="Transport">The transport of the endpoint features were last added to.</param>
/// <param name="Persistence">That endpoint's persistence.</param>
public sealed record Remembered(string? Transport, string? Persistence)
{
    public static Remembered None { get; } = new(null, null);
}

/// <summary>Which remembered values filled in a state, so the steps can say where an answer came from.</summary>
public sealed record Restored(bool Transport, bool Persistence)
{
    public bool Any => Transport || Persistence;
}

/// <summary>
/// The rules for the browser's memory. Someone adding features to an endpoint comes back for the next
/// one, often through a link in an extension's readme, so the add flow remembers what that endpoint
/// runs on. A url always wins: remembered values only fill in what a url leaves out, so a shared link
/// means the same thing in every browser. A new endpoint is a fresh decision each time, so that flow
/// neither reads nor writes anything.
/// </summary>
public static class BrowserMemory
{
    public const string Prefix = "nservicebus-wizard:";
    public const string TransportKey = Prefix + "transport";
    public const string PersistenceKey = Prefix + "persistence";

    public static IReadOnlyList<string> Keys { get; } = [TransportKey, PersistenceKey];

    public static bool Remembers(Flow flow) =>
        flow == Flow.Add;

    /// <summary>Fills in, from what the browser remembers, whatever <paramref name="query"/> leaves out.</summary>
    public static Restored Seed(WizardState state, string? query, Remembered remembered)
    {
        if (!Remembers(state.Flow))
        {
            return new(false, false);
        }

        var transport = false;
        if (!WizardStateUrl.HasKey(query, WizardStateUrl.TransportKey) &&
            WizardStateUrl.ParseEnum<Transport>(remembered.Transport) is { } rememberedTransport)
        {
            state.Transport = rememberedTransport;
            transport = true;
        }

        var persistence = false;
        if (!WizardStateUrl.HasKey(query, WizardStateUrl.PersistenceKey) &&
            WizardStateUrl.ParseEnum<Persistence>(remembered.Persistence) is { } rememberedPersistence)
        {
            state.Persistence = rememberedPersistence;
            persistence = true;
        }

        state.Normalize();
        return new(transport, persistence);
    }

    /// <summary>
    /// What to remember for a state: a value for each answer given, null meaning leave whatever is
    /// kept alone. An answer is never forgotten by being absent, only replaced.
    /// </summary>
    public static Remembered For(WizardState state)
    {
        if (!Remembers(state.Flow))
        {
            return Remembered.None;
        }

        return new(state.Transport?.ToString(), state.Persistence?.ToString());
    }
}
