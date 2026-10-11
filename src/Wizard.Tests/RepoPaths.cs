/// <summary>Locates real repo files so the contract tests can compare the wizard's baked values
/// against their sources (global.json, Directory.Build.props, the deploy workflow).</summary>
public static class RepoPaths
{
    public static string SrcDirectory { get; } = FindSrcDirectory();

    public static string RepoRoot { get; } = Path.GetFullPath(Path.Combine(SrcDirectory, ".."));

    static string FindSrcDirectory()
    {
        var directory = AppContext.BaseDirectory;
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "Wizard.slnx")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new($"Could not locate the src directory (Wizard.slnx) above {AppContext.BaseDirectory}");
    }

    public static string SrcFile(params string[] segments) =>
        Path.Combine([SrcDirectory, .. segments]);

    public static string RepoFile(params string[] segments) =>
        Path.Combine([RepoRoot, .. segments]);
}
