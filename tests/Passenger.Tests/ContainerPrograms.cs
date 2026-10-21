using System.Diagnostics;
using System.Globalization;
using System.Text;
using Passenger.Core;

namespace Passenger.Tests;

/// <summary>What the container tests run in a container: a .NET 10 console program built here in Debug and copied into an
/// image of .NET's runtime, labelled as the run's, and Passenger's container tools published where a dev build finds them
/// (artifacts/container-tools), as <c>just container-tools</c> makes them: some seconds when they are up to date.</summary>
internal static class ContainerPrograms
{
    /// <summary>The engine's processor, as .NET names it (x64, arm64).</summary>
    public static string Processor() => Docker.Ask(["version", "--format", "{{.Server.Arch}}"]).Trim() == "arm64" ? "arm64" : "x64";

    /// <summary>Publishes the tools for a system into artifacts/container-tools, as tools/container-tools.sh does.</summary>
    public static void Publish(string rid)
    {
        string output = Path.Combine(Debugging.Helpers.Debuggees.Root, "artifacts", "container-tools", rid);
        ChildResult published = Dotnet(["publish", Path.Combine(Debugging.Helpers.Debuggees.Root, "src", "Passenger.ContainerTools"), "-c", "Release", "-r", rid,
            "--self-contained", "-o", output, "-nologo", "-v:q"], Debugging.Helpers.Debuggees.Root);
        Assert.True(published.Succeeded, $"Passenger's container tools for {rid} did not publish: {published.Output}{published.Error}");
    }

    /// <summary>A program's project in <paramref name="folder"/>, built in Debug into <paramref name="output"/>, its paths
    /// mapped to <paramref name="mapped"/> (the PDB naming them so) when given.</summary>
    public static void Build(string folder, string project, string program, string output, string? mapped = null)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, Path.GetFileName(folder) + ".csproj"), project);
        File.WriteAllText(Path.Combine(folder, "Program.cs"), program);
        List<string> words = ["build", Path.Combine(folder, Path.GetFileName(folder) + ".csproj"), "-c", "Debug", "-o", output, "-nologo", "-v:q"];
        if (mapped is not null)
        {
            words.Add($"-p:PathMap={PathNames.Canonical(folder)}{Path.DirectorySeparatorChar}={mapped}");
        }
        ChildResult built = Dotnet(words, folder);
        Assert.True(built.Succeeded, $"{Path.GetFileName(folder)} did not build: {built.Output}{built.Error}");
    }

    /// <summary>An image of a build on a runtime image, running <paramref name="assembly"/> with dotnet, with sources at
    /// /src/NAME when given; its tag, labelled as the run's.</summary>
    public static string Image(string context, string variant, string from, string build, string assembly, string? sources = null, string? sourcesAt = null)
    {
        Directory.CreateDirectory(context);
        CopyFolder(build, Path.Combine(context, "app"));
        StringBuilder dockerfile = new($"FROM {from}\nCOPY app/ /app/\n");
        if (sources is not null && sourcesAt is not null)
        {
            Directory.CreateDirectory(Path.Combine(context, "src"));
            File.Copy(Path.Combine(sources, "Program.cs"), Path.Combine(context, "src", "Program.cs"));
            dockerfile.Append($"COPY src/ {sourcesAt}\n");
        }
        dockerfile.Append($"ENTRYPOINT [\"dotnet\", \"/app/{assembly}.dll\"]\n");
        File.WriteAllText(Path.Combine(context, "Dockerfile"), dockerfile.ToString());
        string tag = $"ptest{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}-{assembly.ToLowerInvariant()}:{variant}";
        Docker.Run("build", "-q", "--label", Docker.RunLabel, "-t", tag, context);
        return tag;
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            string into = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(into)!);
            File.Copy(file, into, overwrite: true);
        }
    }

    private static ChildResult Dotnet(IReadOnlyList<string> words, string folder)
    {
        ProcessStartInfo start = ChildProcesses.DotnetCommand(words);
        start.WorkingDirectory = folder;
        return Task.Run(() => ChildProcesses.RunAsync(start, null, TimeSpan.FromMinutes(10).Scaled(), CancellationToken.None)).GetAwaiter().GetResult();
    }
}
