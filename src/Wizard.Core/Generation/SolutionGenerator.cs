/// <summary>
/// The file tree of the download: a whole solution for a new endpoint, or the changes to merge into
/// one when features are added.
/// </summary>
public static class SolutionGenerator
{
    public static IReadOnlyList<GeneratedFile> Build(Plan plan)
    {
        if (plan.IsAddition)
        {
            return AdditionGenerator.Build(plan);
        }

        var files = new List<GeneratedFile>();
        void Add(string path, string text) =>
            files.Add(new(path, text));

        var ai = AiContentGenerator.Build(plan);
        var compose = ProjectFiles.DockerCompose(plan);

        var rootFiles = new List<string>
        {
            "global.json",
            "Directory.Build.props",
            "Directory.Packages.props",
            ".gitignore",
            ".editorconfig",
            "readme.md"
        };
        if (compose != null)
        {
            rootFiles.Add("docker-compose.yml");
        }

        Add("global.json", ProjectFiles.GlobalJson(plan));
        Add("Directory.Build.props", ProjectFiles.DirectoryBuildProps);
        Add("Directory.Packages.props", ProjectFiles.DirectoryPackagesProps(plan));
        Add(".gitignore", ProjectFiles.GitIgnore);
        Add(".editorconfig", ProjectFiles.EditorConfig);
        Add($"{plan.SolutionName}.slnx", ProjectFiles.Slnx(plan, rootFiles));
        Add("readme.md", DocsGenerator.Build(plan));
        Add("CLAUDE.md", ai);
        Add(".github/copilot-instructions.md", ai);
        if (compose != null)
        {
            Add("docker-compose.yml", compose);
        }

        var endpoint = $"src/{plan.EndpointProject}";
        Add($"{endpoint}/{plan.EndpointProject}.csproj", ProjectFiles.EndpointProject(plan));
        Add($"{endpoint}/Program.cs", ProgramGenerator.Build(plan));
        Add($"{endpoint}/appsettings.json", ProjectFiles.AppSettings(plan));
        var code = SampleFiles.Endpoint(plan)
            .Concat(plan.Features.SelectMany(_ => SampleFiles.Feature(_, plan)))
            .DistinctBy(_ => _.Path);
        foreach (var file in code)
        {
            Add($"{endpoint}/{file.Path}", file.Text);
        }

        if (plan.Tests is { } tests)
        {
            var testProject = $"src/{plan.TestProject}";
            Add($"{testProject}/{plan.TestProject}.csproj", ProjectFiles.TestProject(plan, tests));
            foreach (var file in TestFiles.For(plan))
            {
                Add($"{testProject}/{file.Path}", file.Text);
            }
        }

        return files;
    }
}
