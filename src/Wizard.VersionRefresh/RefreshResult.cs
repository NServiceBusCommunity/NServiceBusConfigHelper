/// <param name="Changes">Package id, old version, new version, in file order.</param>
/// <param name="Unanswered">Ids nuget.org gave no answer for, left as they were.</param>
public sealed record RefreshResult(
    string Json,
    IReadOnlyList<(string Id, string From, string To)> Changes,
    IReadOnlyList<string> Unanswered)
{
    public bool Changed => Changes.Count > 0;

    /// <summary>The pull request body: what moved, and what could not be checked.</summary>
    public string Summary()
    {
        var builder = new StringBuilder();
        if (Changed)
        {
            builder.Append(
                """
                Newest stable versions on nuget.org.

                | Package | From | To |
                |---|---|---|

                """);
            foreach (var (id, from, to) in Changes)
            {
                builder.Append($"| {id} | {from} | {to} |\n");
            }
        }
        else
        {
            builder.Append("Every package is already at its newest stable version.\n");
        }

        if (Unanswered.Count > 0)
        {
            builder.Append(
                $"""

                 Not checked, because nuget.org did not answer: {string.Join(", ", Unanswered)}.

                 """);
        }

        return builder.ToString();
    }
}
