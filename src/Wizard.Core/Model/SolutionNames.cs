public static class SolutionNames
{
    /// <summary>
    /// Keeps letters, digits, dots and underscores, so the name is safe as a folder, a project name, a
    /// namespace and an endpoint name. Falls back to <see cref="WizardState.DefaultSolutionName"/> when
    /// nothing usable is left.
    /// </summary>
    public static string Clean(string? name)
    {
        var cleaned = new string((name ?? "").Where(_ => char.IsAsciiLetterOrDigit(_) || _ is '.' or '_').ToArray())
            .Trim('.');
        if (cleaned.Length == 0 ||
            char.IsAsciiDigit(cleaned[0]))
        {
            return WizardState.DefaultSolutionName;
        }

        return cleaned;
    }

    /// <summary>
    /// A name for the database, container or table the persistence creates. Letters and digits only,
    /// which is the strictest rule among them: an Azure table name allows nothing else.
    /// </summary>
    public static string Database(string solutionName) =>
        new(solutionName.Where(char.IsAsciiLetterOrDigit).ToArray());
}
