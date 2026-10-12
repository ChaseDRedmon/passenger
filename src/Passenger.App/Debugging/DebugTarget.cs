using System.Diagnostics;
using Passenger.App.Run;
using Passenger.App.Workspace;

namespace Passenger.App.Debugging;

/// <summary>What a run configuration debugs: the assembly its project builds, the folder it runs in (a
/// launch profile's, or else the output folder, as Rider's default), its arguments and environment.
/// MSBuild says where the assembly is, since a project's own properties (an artifacts layout, an output
/// path) decide it.</summary>
public sealed record DebugTarget(string Program, string WorkingDirectory, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> Environment)
{
    /// <summary>The target for a configuration, with these arguments in place of its profile's if given.</summary>
    public static async Task<DebugTarget> ResolveAsync(RunConfiguration configuration, string? arguments = null, CancellationToken cancellation = default)
    {
        DebugTarget built = await ResolveAsync(configuration.Project, cancellation);
        return built with
        {
            WorkingDirectory = configuration.Profile?.Folder(configuration.Project) ?? built.WorkingDirectory,
            Arguments = configuration.Arguments(arguments),
            Environment = configuration.Profile?.Environment() ?? new Dictionary<string, string>(),
        };
    }

    public static async Task<DebugTarget> ResolveAsync(ProjectModel project, CancellationToken cancellation = default)
    {
        List<string> args = ["msbuild", project.Path, "-nologo", "-getProperty:TargetPath"];
        if (project.TargetFrameworks.Count > 1)
        {
            // A project built for several frameworks runs the first, as dotnet run asks of one.
            args.Add($"-property:TargetFramework={project.TargetFrameworks[0]}");
        }
        ProcessStartInfo start = new(Lsp.Dotnet.Host(), args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = project.Folder,
        };
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("dotnet did not start");
        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellation);
        Task<string> errors = process.StandardError.ReadToEndAsync(cancellation);
        await process.WaitForExitAsync(cancellation);
        string path = (await output).Trim();
        if (process.ExitCode != 0 || path.Length == 0)
        {
            throw new InvalidOperationException($"MSBuild could not say what {project.Name} builds: {(await errors).Trim()} {path}".Trim());
        }
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{project.Name} has not built {Path.GetFileName(path)}", path);
        }
        return new DebugTarget(path, Path.GetDirectoryName(path)!, [], new Dictionary<string, string>());
    }
}
