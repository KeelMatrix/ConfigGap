using System.Globalization;
using System.Text;

namespace KeelMatrix.ConfigGap.Probe;

internal static class SyntheticSolutionGenerator
{
    public static void Generate(string repositoryRoot, string outputDirectory, int projectCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(projectCount, 1);

        var fixtureRoot = Path.Combine(repositoryRoot, "fixtures", "FixtureConsumer");
        var supportRoot = Path.Combine(repositoryRoot, "fixtures", "FixtureSupport");
        if (!Directory.Exists(fixtureRoot) || !Directory.Exists(supportRoot))
        {
            throw new InvalidOperationException("The fixture corpus is required for synthetic generation.");
        }

        if (Directory.Exists(outputDirectory) && Directory.EnumerateFileSystemEntries(outputDirectory).Any())
        {
            throw new InvalidOperationException($"Synthetic output directory is not empty: {outputDirectory}");
        }

        Directory.CreateDirectory(outputDirectory);
        File.WriteAllText(Path.Combine(outputDirectory, "Directory.Build.props"), "<Project><PropertyGroup><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><ImplicitUsings>enable</ImplicitUsings><Deterministic>true</Deterministic><DeterministicSourcePaths>false</DeterministicSourcePaths><IsPackable>false</IsPackable></PropertyGroup></Project>" + Environment.NewLine);
        File.WriteAllText(Path.Combine(outputDirectory, "Directory.Packages.props"), "<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally><CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled></PropertyGroup><ItemGroup><PackageVersion Include=\"Microsoft.Extensions.Configuration.Abstractions\" Version=\"8.0.0\" /><PackageVersion Include=\"Microsoft.Extensions.Configuration.Binder\" Version=\"8.0.2\" /><PackageVersion Include=\"Microsoft.Extensions.Options.ConfigurationExtensions\" Version=\"8.0.0\" /></ItemGroup></Project>" + Environment.NewLine);
        File.Copy(Path.Combine(repositoryRoot, "global.json"), Path.Combine(outputDirectory, "global.json"), overwrite: true);
        CopyFixtureSupport(supportRoot, Path.Combine(outputDirectory, "FixtureSupport"));
        for (var index = 1; index <= projectCount; index++)
        {
            var projectName = $"FixtureConsumer{index:000}";
            var projectRoot = Path.Combine(outputDirectory, projectName);
            Directory.CreateDirectory(Path.Combine(projectRoot, "Patterns"));
            foreach (var source in Directory.EnumerateFiles(Path.Combine(fixtureRoot, "Patterns"), "*.cs", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.Ordinal))
            {
                File.Copy(source, Path.Combine(projectRoot, "Patterns", Path.GetFileName(source)));
            }

            foreach (var fileName in new[] { "appsettings.json", "appsettings.Production.json", ".env.example" })
            {
                var source = Path.Combine(fixtureRoot, fileName);
                if (File.Exists(source))
                {
                    File.Copy(source, Path.Combine(projectRoot, fileName));
                }
            }

            File.WriteAllText(Path.Combine(projectRoot, projectName + ".csproj"), CreateProjectFile(projectName));
        }

        File.WriteAllText(Path.Combine(outputDirectory, "ConfigGap.Synthetic.sln"), CreateSolution(projectCount));
    }

    private static void CopyFixtureSupport(string sourceRoot, string destinationRoot)
    {
        Directory.CreateDirectory(destinationRoot);
        foreach (var source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories).Where(path => !IsBuildOutput(path)).OrderBy(path => path, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(sourceRoot, source);
            var destination = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination);
        }
    }

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj");

    private static string CreateProjectFile(string projectName) => $@"<Project Sdk=""Microsoft.NET.Sdk"">{CRLF}  <PropertyGroup>{CRLF}    <AssemblyName>ConfigGap.Synthetic.{projectName}</AssemblyName>{CRLF}    <RootNamespace>ConfigGap.Synthetic.{projectName}</RootNamespace>{CRLF}  </PropertyGroup>{CRLF}{CRLF}  <ItemGroup>{CRLF}    <PackageReference Include=""Microsoft.Extensions.Configuration.Abstractions"" />{CRLF}    <PackageReference Include=""Microsoft.Extensions.Configuration.Binder"" />{CRLF}    <PackageReference Include=""Microsoft.Extensions.Options.ConfigurationExtensions"" />{CRLF}  </ItemGroup>{CRLF}{CRLF}  <ItemGroup>{CRLF}    <ProjectReference Include=""..\FixtureSupport\FixtureSupport.csproj"" />{CRLF}  </ItemGroup>{CRLF}</Project>{CRLF}";

    private const string CRLF = "\r\n";

    private static string CreateSolution(int projectCount)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
        builder.AppendLine("# Visual Studio Version 17");
        builder.AppendLine("VisualStudioVersion = 17.0.31903.59");
        builder.AppendLine("MinimumVisualStudioVersion = 10.0.40219.1");
        builder.AppendLine("Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"FixtureSupport\", \"FixtureSupport\\FixtureSupport.csproj\", \"{10000000-0000-0000-0000-000000000001}\"");
        builder.AppendLine("EndProject");
        for (var index = 1; index <= projectCount; index++)
        {
            builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"FixtureConsumer{0:000}\", \"FixtureConsumer{0:000}\\FixtureConsumer{0:000}.csproj\", \"{{{1}}}\"", index, ConsumerProjectId(index)));
            builder.AppendLine("EndProject");
        }

        builder.AppendLine("Global");
        builder.AppendLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");
        builder.AppendLine("\t\tDebug|Any CPU = Debug|Any CPU");
        builder.AppendLine("\t\tRelease|Any CPU = Release|Any CPU");
        builder.AppendLine("\tEndGlobalSection");
        builder.AppendLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");
        builder.AppendLine("\t\t{10000000-0000-0000-0000-000000000001}.Debug|Any CPU.ActiveCfg = Debug|Any CPU");
        builder.AppendLine("\t\t{10000000-0000-0000-0000-000000000001}.Debug|Any CPU.Build.0 = Debug|Any CPU");
        builder.AppendLine("\t\t{10000000-0000-0000-0000-000000000001}.Release|Any CPU.ActiveCfg = Release|Any CPU");
        builder.AppendLine("\t\t{10000000-0000-0000-0000-000000000001}.Release|Any CPU.Build.0 = Release|Any CPU");
        for (var index = 1; index <= projectCount; index++)
        {
            var projectId = ConsumerProjectId(index);
            builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "\t\t{{{0}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU", projectId));
            builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "\t\t{{{0}}}.Debug|Any CPU.Build.0 = Debug|Any CPU", projectId));
            builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "\t\t{{{0}}}.Release|Any CPU.ActiveCfg = Release|Any CPU", projectId));
            builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "\t\t{{{0}}}.Release|Any CPU.Build.0 = Release|Any CPU", projectId));
        }

        builder.AppendLine("\tEndGlobalSection");
        builder.AppendLine("\tGlobalSection(SolutionProperties) = preSolution");
        builder.AppendLine("\t\tHideSolutionNode = FALSE");
        builder.AppendLine("\tEndGlobalSection");
        builder.AppendLine("EndGlobal");
        return builder.ToString();
    }

    private static string ConsumerProjectId(int index) => $"10000000-0000-0000-0001-{index:000000000000}";
}
