using System.Reflection;
using Passenger.Core;

namespace Passenger.Tests;

/// <summary>An Avalonia app as <c>dotnet new avalonia.app</c> makes it, from Avalonia's own templates at the Avalonia
/// version Passenger uses (so its packages are the ones already restored), installed once per run into the run's own
/// template hive, never the person's. The template's reference to Avalonia's paid developer tools is taken out, as
/// nothing of Avalonia Accelerate may be used.</summary>
internal static class AvaloniaApp
{
    private static readonly Lazy<string> Hive = new(Install, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The Avalonia version the tests run on, which the template is asked for.</summary>
    public static string Version { get; } =
        typeof(Avalonia.Controls.Button).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    private static string Install()
    {
        string hive = Directory.CreateDirectory(Path.Combine(TestRun.Root, "templates")).FullName;
        ChildResult installed = Run(["new", "install", $"Avalonia.Templates::{Version}", "--debug:custom-hive", hive]);
        Assert.True(installed.Succeeded, $"Avalonia's templates did not install into the run's own hive (exit code {installed.ExitCode}):\n{installed.Output}{installed.Error}");
        return hive;
    }

    /// <summary>Makes the app in a folder, its project and namespace named <paramref name="name"/>, without restoring it.</summary>
    public static void Create(string folder, string name)
    {
        ChildResult made = Run(["new", "avalonia.app", "--output", folder, "--name", name, "--framework", "net10.0", "--avalonia-version", Version,
            "--no-restore", "--no-update-check", "--debug:custom-hive", Hive.Value]);
        Assert.True(made.Succeeded, $"dotnet new avalonia.app did not make the test's app (exit code {made.ExitCode}):\n{made.Output}{made.Error}");
        string project = Path.Combine(folder, name + ".csproj");
        string text = File.ReadAllText(project);
        int diagnostics = text.IndexOf("<PackageReference Include=\"AvaloniaUI.DiagnosticsSupport\"", StringComparison.Ordinal);
        if (diagnostics >= 0)
        {
            int end = text.IndexOf("</PackageReference>", diagnostics, StringComparison.Ordinal);
            end = end < 0 ? text.IndexOf("/>", diagnostics, StringComparison.Ordinal) + 2 : end + "</PackageReference>".Length;
            text = text[..diagnostics] + text[end..];
        }
        File.WriteAllText(project, text.Replace("</Project>", "  <ItemGroup>\n    <AvaloniaResource Include=\"Assets\\**\" />\n  </ItemGroup>\n</Project>", StringComparison.Ordinal));
        string program = Path.Combine(folder, "Program.cs");
        string code = File.ReadAllText(program);
        File.WriteAllText(program, string.Join('\n', code.Split('\n').Where(line => !line.Contains("WithDeveloperTools", StringComparison.Ordinal)
            && line.Trim() is not ("#if DEBUG" or "#endif"))));
    }

    private static ChildResult Run(IReadOnlyList<string> arguments) =>
        Task.Run(() => ChildProcesses.RunAsync(ChildProcesses.DotnetCommand(arguments), limit: TimeSpan.FromMinutes(3))).GetAwaiter().GetResult();
}
