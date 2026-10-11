/// <summary>Solution-level and project files of the generated solution.</summary>
public static class ProjectFiles
{
    public static string GlobalJson(Plan plan)
    {
        var sdk =
            $$"""
              "sdk": {
                "version": "{{WizardDefaults.SdkVersion}}",
                "rollForward": "latestFeature"
              }
            """;
        if (plan.Tests == null)
        {
            return $"{{\n{sdk}\n}}\n";
        }

        // Every generated test project is a Microsoft.Testing.Platform app, and this is what makes
        // dotnet test run it as one.
        return
            $$"""
            {
            {{sdk}},
              "test": {
                "runner": "Microsoft.Testing.Platform"
              }
            }

            """;
    }

    public static string DirectoryBuildProps =>
        $"""
         <Project>
           <!-- Applies to every project under this directory. -->
           <PropertyGroup>
             <TargetFramework>{WizardDefaults.TargetFramework}</TargetFramework>
             <LangVersion>latest</LangVersion>
             <Nullable>enable</Nullable>
             <ImplicitUsings>enable</ImplicitUsings>
           </PropertyGroup>
         </Project>

         """;

    public static string DirectoryPackagesProps(Plan plan)
    {
        var builder = new StringBuilder();
        builder.Append(
            """
            <Project>
              <!-- Central Package Management: every package version is set here, once. Projects reference
                   packages with <PackageReference Include="..." /> and no Version.
                   https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management -->
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
              </PropertyGroup>

            """);
        foreach (var (comment, packages) in PackageGroups(plan))
        {
            AppendVersions(builder, comment, packages, plan);
        }

        builder.Append("</Project>\n");
        return builder.ToString();
    }

    /// <summary>
    /// The packages grouped by what brought them in, each listed once, under the first component that
    /// needs it: Microsoft.Data.SqlClient belongs to the SQL Server persistence, and SQL attachments
    /// then simply reuses it.
    /// </summary>
    public static IEnumerable<(string Comment, IReadOnlyList<string> Packages)> PackageGroups(Plan plan)
    {
        var listed = new HashSet<string>(StringComparer.Ordinal);

        IReadOnlyList<string> Unlisted(IEnumerable<string> packages) =>
            [.. packages.Where(listed.Add)];

        if (!plan.IsAddition)
        {
            yield return ("NServiceBus", Unlisted(["NServiceBus"]));
        }

        foreach (var component in plan.Components)
        {
            var packages = Unlisted(component.Packages);
            if (packages.Count > 0)
            {
                yield return ($"{Kind(component)}: {component.DisplayName}", packages);
            }
        }

        if (plan.Tests is { } tests)
        {
            yield return ($"Tests: {tests.Framework.Name()}", Unlisted(plan.TestPackages));
        }
    }

    static string Kind(ComponentDefinition component) =>
        component switch
        {
            TransportDefinition => "Transport",
            PersistenceDefinition => "Persistence",
            SerializerDefinition => "Serializer",
            _ => "Feature"
        };

    static void AppendVersions(StringBuilder builder, string comment, IEnumerable<string> packages, Plan plan)
    {
        builder.Append($"  <!-- {comment} -->\n");
        builder.Append("  <ItemGroup>\n");
        foreach (var package in packages)
        {
            builder.Append($"    <PackageVersion Include=\"{package}\" Version=\"{plan.Version(package)}\" />\n");
        }

        builder.Append("  </ItemGroup>\n");
    }

    public const string GitIgnore =
        """
        bin/
        obj/
        .vs/
        .idea/
        *.user

        # NServiceBus learning transport and learning persistence: messages and sagas as files
        .learningtransport/
        .sagas/

        # file share attachments, when kept under the build output
        attachments/

        """;

    public const string EditorConfig =
        """
        root = true

        [*]
        indent_style = space
        insert_final_newline = true

        [*.cs]
        indent_size = 4
        charset = utf-8

        [*.{csproj,props,targets,slnx,json,yml,yaml,config}]
        indent_size = 2

        """;

    public static string Slnx(Plan plan, IEnumerable<string> solutionItems)
    {
        var builder = new StringBuilder("<Solution>\n  <Folder Name=\"/Solution Items/\">\n");
        foreach (var item in solutionItems)
        {
            builder.Append($"    <File Path=\"{item}\" />\n");
        }

        builder.Append("  </Folder>\n");
        builder.Append($"  <Project Path=\"src/{plan.EndpointProject}/{plan.EndpointProject}.csproj\" />\n");
        if (plan.Tests != null)
        {
            builder.Append($"  <Project Path=\"src/{plan.TestProject}/{plan.TestProject}.csproj\" />\n");
        }

        builder.Append("</Solution>\n");
        return builder.ToString();
    }

    public static string EndpointProject(Plan plan)
    {
        var sdk = "Microsoft.NET.Sdk.Worker";
        if (plan.Hosting == Hosting.AspNetCore)
        {
            sdk = "Microsoft.NET.Sdk.Web";
        }

        var builder = new StringBuilder($"<Project Sdk=\"{sdk}\">\n");
        builder.Append("  <!-- The target framework and language settings are in Directory.Build.props; versions are in Directory.Packages.props. -->\n");
        builder.Append("  <ItemGroup>\n");
        foreach (var package in plan.EndpointPackages)
        {
            builder.Append($"    <PackageReference Include=\"{package}\" />\n");
        }

        builder.Append("  </ItemGroup>\n");
        builder.Append("</Project>\n");
        return builder.ToString();
    }

    public static string TestProject(Plan plan, TestFrameworkInfo tests)
    {
        var builder = new StringBuilder("<Project Sdk=\"Microsoft.NET.Sdk\">\n");
        if (tests.Properties.Count > 0)
        {
            builder.Append("  <PropertyGroup>\n");
            builder.Append("    <!-- Runs as a Microsoft.Testing.Platform app; dotnet test uses it through the runner set in global.json. -->\n");
            foreach (var (property, value) in tests.Properties)
            {
                builder.Append($"    <{property}>{value}</{property}>\n");
            }

            builder.Append("  </PropertyGroup>\n");
        }

        builder.Append("  <ItemGroup>\n");
        foreach (var package in plan.TestPackages)
        {
            builder.Append($"    <PackageReference Include=\"{package}\" />\n");
        }

        builder.Append("  </ItemGroup>\n");
        builder.Append("  <ItemGroup>\n");
        builder.Append($"    <ProjectReference Include=\"..\\{plan.EndpointProject}\\{plan.EndpointProject}.csproj\" />\n");
        builder.Append("  </ItemGroup>\n");
        builder.Append("</Project>\n");
        return builder.ToString();
    }

    /// <summary>
    /// Logging levels, and a connection string for each service the endpoint talks to. The values
    /// point at the containers in the generated compose file; one with nothing local to point at is
    /// left empty, and the endpoint says which when it starts.
    /// </summary>
    public static string AppSettings(Plan plan)
    {
        var levels = new List<(string, string)>
        {
            ("Default", "Information")
        };
        if (plan.Hosting == Hosting.AspNetCore)
        {
            levels.Add(("Microsoft.AspNetCore", "Warning"));
        }

        var builder = new StringBuilder("{\n  \"Logging\": {\n    \"LogLevel\": {\n");
        builder.Append(string.Join(",\n", levels.Select(_ => $"      \"{_.Item1}\": \"{_.Item2}\"")));
        builder.Append("\n    }\n  }");
        if (plan.Connections.Count > 0)
        {
            builder.Append(",\n  \"ConnectionStrings\": {\n");
            builder.Append(string.Join(",\n", plan.Connections.Select(_ => $"    \"{_.Name}\": \"{Json(DevelopmentValue(_, plan))}\"")));
            builder.Append("\n  }");
        }

        builder.Append("\n}\n");
        return builder.ToString();
    }

    public static string DevelopmentValue(ConnectionDefinition connection, Plan plan) =>
        Template.Render(connection.Development, plan);

    static string Json(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <returns>Null when nothing the endpoint uses has a container to run.</returns>
    public static string? DockerCompose(Plan plan)
    {
        if (plan.Infrastructure.Count == 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.Append($"# Generated by the NServiceBus wizard: {plan.WizardUrl}\n");
        builder.Append("# What the endpoint needs in order to run on a developer's machine. The passwords are for these\n");
        builder.Append("# local containers only; never reuse them.\n");
        builder.Append("services:\n");
        builder.Append(string.Join("\n\n", plan.Infrastructure.Select(_ => Template.Render(_.Compose, plan))));
        builder.Append('\n');
        return builder.ToString();
    }
}
