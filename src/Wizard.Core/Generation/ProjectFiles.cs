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
        var groups = PackageGroups(plan).Select(_ => Versions(_.Comment, _.Packages, plan)).ToList();
        var pinning = "";
        var pins = plan.TransitivePins;
        if (pins.Count > 0)
        {
            pinning = "\n    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>";
            groups.Add(Versions("Pinned above the version a dependency asks for, which has a known vulnerability", pins, plan));
        }

        return
            $"""
             <Project>
               <!-- Central Package Management: every package version is set here, once. Projects reference
                    packages with <PackageReference Include="..." /> and no Version.
                    https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management -->
               <PropertyGroup>
                 <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>{pinning}
               </PropertyGroup>
             {Lines(groups)}
             </Project>

             """;
    }

    /// <summary>Items that fill a hole in a template, one per line. Each carries its own indentation.</summary>
    static string Lines(IEnumerable<string> lines) =>
        string.Join('\n', lines);

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

    static string Versions(string comment, IEnumerable<string> packages, Plan plan)
    {
        var versions = packages.Select(_ => $"""    <PackageVersion Include="{_}" Version="{plan.Version(_)}" />""");
        return
            $"""
               <!-- {comment} -->
               <ItemGroup>
             {Lines(versions)}
               </ItemGroup>
             """;
    }

    static string PackageReferences(IEnumerable<string> packages) =>
        Lines(packages.Select(_ => $"""    <PackageReference Include="{_}" />"""));

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
        var items = solutionItems.Select(_ => $"""    <File Path="{_}" />""");
        var projects = new List<string>
        {
            plan.EndpointProject
        };
        if (plan.Tests != null)
        {
            projects.Add(plan.TestProject);
        }

        return
            $"""
             <Solution>
               <Folder Name="/Solution Items/">
             {Lines(items)}
               </Folder>
             {Lines(projects.Select(_ => $"""  <Project Path="src/{_}/{_}.csproj" />"""))}
             </Solution>

             """;
    }

    public static string EndpointProject(Plan plan)
    {
        var sdk = "Microsoft.NET.Sdk.Worker";
        if (plan.Hosting == Hosting.AspNetCore)
        {
            sdk = "Microsoft.NET.Sdk.Web";
        }

        return
            $"""
             <Project Sdk="{sdk}">
               <!-- The target framework and language settings are in Directory.Build.props; versions are in Directory.Packages.props. -->
               <ItemGroup>
             {PackageReferences(plan.EndpointPackages)}
               </ItemGroup>
             </Project>

             """;
    }

    public static string TestProject(Plan plan, TestFrameworkInfo tests)
    {
        var groups = new List<string>();
        if (tests.Properties.Count > 0)
        {
            var properties = tests.Properties.Select(_ => $"    <{_.Name}>{_.Value}</{_.Name}>");
            groups.Add(
                $"""
                   <PropertyGroup>
                     <!-- Runs as a Microsoft.Testing.Platform app; dotnet test uses it through the runner set in global.json. -->
                 {Lines(properties)}
                   </PropertyGroup>
                 """);
        }

        groups.Add(
            $"""
               <ItemGroup>
             {PackageReferences(plan.TestPackages)}
               </ItemGroup>
               <ItemGroup>
                 <ProjectReference Include="..\{plan.EndpointProject}\{plan.EndpointProject}.csproj" />
               </ItemGroup>
             """);

        return
            $"""
             <Project Sdk="Microsoft.NET.Sdk">
             {Lines(groups)}
             </Project>

             """;
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

        var logLevels = string.Join(",\n", levels.Select(_ => $"      \"{_.Item1}\": \"{_.Item2}\""));
        var logging =
            $$"""
                "Logging": {
                  "LogLevel": {
              {{logLevels}}
                  }
                }
              """;
        if (plan.Connections.Count == 0)
        {
            return $"{{\n{logging}\n}}\n";
        }

        var connections = string.Join(",\n", plan.Connections.Select(_ => $"    \"{_.Name}\": \"{Json(DevelopmentValue(_, plan))}\""));
        return
            $$"""
              {
              {{logging}},
                "ConnectionStrings": {
              {{connections}}
                }
              }

              """;
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
        builder.Append(
            $"""
             # Generated by the NServiceBus wizard: {plan.WizardUrl}
             # What the endpoint needs in order to run on a developer's machine. The passwords are for these
             # local containers only; never reuse them.
             services:

             """);
        builder.Append(string.Join("\n\n", plan.Infrastructure.Select(_ => Template.Render(_.Compose, plan))));
        builder.Append('\n');
        return builder.ToString();
    }
}
