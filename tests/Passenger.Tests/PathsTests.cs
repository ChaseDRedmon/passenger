using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Debugging;
using Passenger.App.Explorer;
using Passenger.App.ToolWindows;
using Passenger.App.Vcs;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>A file is one file however its path is spelled. A repository opened by a lower-case path (on Windows also by
/// its 8.3 short names, elsewhere through a link) shows its changed file coloured, diffs the open file itself, commits it
/// and binds a breakpoint in it, as Git, MSBuild and the debugger name it by the disk's own names. Debugged, so run with
/// the debugger's tests.</summary>
[Collection(Debugging.Helpers.DebugSession.Collection)]
public sealed class PathsTests : IDisposable
{
    private readonly string root = PathNames.Canonical(Directory.CreateTempSubdirectory("passenger-paths-").FullName);

    public enum Spelling
    {
        LowerCase,
        ShortNames,
    }

    public void Dispose() => TestFolders.Delete(root);

    [AvaloniaTheory]
    [InlineData(Spelling.LowerCase)]
    [InlineData(Spelling.ShortNames)]
    public void A_repository_opened_by_another_spelling_of_its_path_colours_diffs_debugs_and_commits_its_changed_file(Spelling spelling)
    {
        // A name with a space, which Windows gives an 8.3 name of its own.
        string repository = Path.Combine(root, "Shop Repository");
        Write(repository, ".gitignore", "bin/\nobj/\n");
        Write(repository, "Shop.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n</Project>\n");
        string program = Write(repository, "Program.cs", "int total = 0;\nfor (int i = 1; i <= 3; i++)\n{\n    total += i;\n}\nConsole.WriteLine(total);\n");
        Git(repository, "init", "-q", "-b", "main");
        Git(repository, "config", "user.email", "tests@passenger.invalid");
        Git(repository, "config", "user.name", "Passenger Tests");
        Git(repository, "config", "core.autocrlf", "false");
        Git(repository, "add", "-A");
        Git(repository, "commit", "-q", "-m", "First");
        File.WriteAllText(program, "int total = 10;\nfor (int i = 1; i <= 3; i++)\n{\n    total += i;\n}\nConsole.WriteLine(total);\n");
        Restore.Run(repository);

        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        string spelled = Spelled(repository, spelling);
        window.OpenFolder(spelled);
        Ui.Settle();

        // Coloured in the Explorer as Git lists it.
        Ui.WaitFor(() => window.Vcs.Changes.Any(c => c.Name == "Program.cs" && c.Kind == ChangeKind.Modified), "Git to list Program.cs as changed");
        ExplorerNode row = Assert.Single(window.ExplorerView.Tree.Rows, r => r.Name == "Program.cs");
        Ui.WaitFor(() => row.Change == ChangeKind.Modified, $"Program.cs to be coloured as changed in the Explorer, which has it at {row.Path} where Git has it at {window.Vcs.Changes[0].Path}");

        // Opened from there, its diff is of the open file itself, not a copy of it.
        window.OpenFile(row.Path!);
        Ui.Settle();
        Assert.Equal(program, window.EditorArea.Active?.Path);
        Task<DiffWindow?> opening = window.ShowDiffAsync(Assert.Single(window.Vcs.Changes).Path);
        Ui.WaitFor(() => opening.IsCompleted, "the diff to open");
        DiffWindow diff = opening.Result!;
        Assert.True(ReferenceEquals(window.EditorArea.Active!.Text, diff.View.Right.Document), "Show Diff opened a copy of the open file rather than the file itself");
        diff.Close();
        Ui.Settle();

        // A breakpoint set in it binds, and the program stops there.
        CodeEditor editor = window.EditorArea.CodeEditor;
        editor.GoTo(new TextPosition(3, 4));
        Ui.Run(window, AppActions.ToggleBreakpoint);
        Ui.Run(window, AppActions.Debug);
        Ui.WaitFor(() => window.Debugger.Session?.State == DebugState.Paused, "the program to stop at the breakpoint");
        Ui.WaitFor(() => editor.LineMarks.Contains(new LineMark(3, LineMarkKind.BreakpointBound)), "the breakpoint to bind");
        Ui.WaitFor(() => editor.LineMarks.Contains(new LineMark(3, LineMarkKind.ExecutionPoint)), "the stop to be shown at the breakpoint in the open file");
        Ui.Run(window, AppActions.Stop);
        Ui.WaitFor(() => !window.Debugger.IsDebugging, "the session to end");

        // Committed from the Commit window, it leaves nothing changed.
        CommitView commit = window.CommitView;
        window.Perform(AppActions.Commit);
        Ui.WaitFor(() => commit.Chosen.Any(c => c.Name == "Program.cs"), "Program.cs in the Commit window");
        commit.Message = "Start at ten";
        Task<bool> committed = commit.CommitAsync(push: false);
        Ui.WaitFor(() => committed.IsCompleted, "the commit");
        Assert.True(committed.Result, window.Status);
        Assert.Equal("Start at ten", Git(repository, "log", "-1", "--format=%s").Trim());
        Ui.WaitFor(() => window.Vcs.Changes.Count == 0, "nothing left changed");
        window.Close();
    }

    /// <summary>Another spelling of an existing folder's path: all in lower case, or with Windows' 8.3 short names (through
    /// a symbolic link elsewhere, the other way another name reaches the same folder).</summary>
    private string Spelled(string path, Spelling spelling)
    {
        if (spelling == Spelling.LowerCase)
        {
            return path.ToLowerInvariant();
        }
        if (!OperatingSystem.IsWindows())
        {
            string link = Path.Combine(root, "Linked Repository");
            Directory.CreateSymbolicLink(link, path);
            return link;
        }
        char[] buffer = new char[1024];
        uint length = GetShortPathName(path, buffer, (uint)buffer.Length);
        Assert.True(length > 0 && length < buffer.Length, $"Windows gave no short name for {path} (error {Marshal.GetLastPInvokeError()})");
        string spelled = new(buffer, 0, (int)length);
        Assert.True(spelled.Contains('~', StringComparison.Ordinal), $"The volume of {path} makes no 8.3 names (fsutil 8dot3name query), so {spelled} is the long path again");
        return spelled;
    }

    private static string Write(string folder, string relative, string text)
    {
        // The full path's separators are the system's, as the IDE's own paths are.
        string path = Path.GetFullPath(Path.Combine(folder, relative));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private static string Git(string folder, params string[] arguments)
    {
        ProcessStartInfo start = new("git", arguments)
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)}: {error}");
        return output;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetShortPathNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathName(string path, [Out] char[] shortPath, uint length);
}
