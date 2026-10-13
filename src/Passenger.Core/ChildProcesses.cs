using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Passenger.Core;

/// <summary>Every program Passenger starts is started here, one way: no shell and no window, an input of its own (never
/// the IDE's terminal, nor an agent's pipe), UTF-8 streams, none of a git hook's repository variables, and a line in the
/// log when it fails or takes a while, or for every one with the debug log on (<see cref="Started"/>, <see cref="Ended"/>).
/// None outlives the IDE, however the IDE ends (<see cref="Lifeline"/>). Not started here: the terminal's shell (Porta.Pty's,
/// ended by its pseudo-terminal's hangup), a program run in the terminal (Porta.Pty's, then taken on: <see cref="Adopt"/>),
/// vstest.console (VSTest's, which ends with the process that started it) and on Windows Velopack's Update.exe, which
/// installs an update once Passenger has ended and so must outlive it.</summary>
public static class ChildProcesses
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>What a git hook exports to point git at its own repository: a child (git, or tests run from a hook) would
    /// otherwise take it for its folder's.</summary>
    private static readonly string[] HookVariables =
    [
        "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES",
        "GIT_COMMON_DIR", "GIT_PREFIX", "GIT_NAMESPACE", "GIT_QUARANTINE_PATH",
    ];

    /// <summary>A child started; raised on the thread that started it, for the IDE's log.</summary>
    public static event EventHandler<ChildStart>? Started;

    /// <summary>A child ended; raised on any thread, for the IDE's log.</summary>
    public static event EventHandler<ChildEnd>? Ended;

    /// <summary>A program and its arguments, to run in a folder (the IDE's own when none is given).</summary>
    public static ProcessStartInfo Command(string program, IEnumerable<string> arguments, string? folder = null)
    {
        ProcessStartInfo start = new(program, arguments);
        if (folder is not null)
        {
            start.WorkingDirectory = folder;
        }
        foreach (string variable in HookVariables)
        {
            start.Environment.Remove(variable);
        }
        return start;
    }

    /// <summary>The .NET command line (<see cref="Dotnet.Host"/>) with these arguments, in English (the IDE reads what it
    /// says) and with no first-run banner. It builds in its own process and MSBuild's worker nodes, never in MSBuild's server,
    /// which outlives it and goes on with what it runs when it is ended. A node ends what it runs when its build goes; one
    /// left for the next build is the IDE's alone, in the group (the job on Windows) of the build that started it, so it
    /// ends with the IDE (<see cref="Lifeline"/>). A <see cref="FileNotFoundException"/> when there is none.</summary>
    public static ProcessStartInfo DotnetCommand(IEnumerable<string> arguments, string? folder = null)
    {
        ProcessStartInfo start = Command(Dotnet.Host(), arguments, folder);
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        start.Environment["DOTNET_NOLOGO"] = "1";
        // MSBuild writes UTF-8, which is how its output is read, on every Windows: by itself it does only from 10 1909 on.
        start.Environment["DOTNET_CLI_FORCE_UTF8_ENCODING"] = "true";
        // The SDK's switch for MSBuild's server, and MSBuild's own, which a person's environment may set over the SDK's.
        start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        start.Environment["MSBUILDUSESERVER"] = "0";
        // No other build takes up a node of the IDE's, to have it ended under it when the IDE ends.
        start.Environment.TryGetValue("MSBUILDNODEHANDSHAKESALT", out string? salt);
        start.Environment["MSBUILDNODEHANDSHAKESALT"] = $"{salt}passenger-{Environment.ProcessId}";
        // dotnet test's, for what it tests (an IDE run under it): each node would hold the command's output open as its own.
        start.Environment.Remove("MSBUILDENSURESTDOUTFORTASKPROCESSES");
        return start;
    }

    /// <summary>Starts a child that ends with the IDE. Its input is the caller's to write when it asked for it, and is
    /// otherwise closed at once, so the child reads nothing of the IDE's. The start is the child's from then on (another
    /// run needs another); what <see cref="Process.Start()"/> throws when it cannot start, this throws.</summary>
    public static Process Start(ProcessStartInfo start)
    {
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        bool closeInput = !start.RedirectStandardInput;
        start.RedirectStandardInput = true;
        start.StandardInputEncoding ??= Utf8;
        if (start.RedirectStandardOutput)
        {
            start.StandardOutputEncoding ??= Utf8;
        }
        if (start.RedirectStandardError)
        {
            start.StandardErrorEncoding ??= Utf8;
        }
        Lifeline.Prepare(start);
        string command = Display(start);
        long began = Stopwatch.GetTimestamp();
        Process process = new() { StartInfo = start, EnableRaisingEvents = true };
        process.Exited += (_, _) => OnExited(process, command, began);
        try
        {
            process.Start();
        }
        catch
        {
            process.Dispose();
            throw;
        }
        Lifeline.Adopt(process);
        Started?.Invoke(null, new ChildStart(process.Id, command, start.WorkingDirectory));
        if (closeInput)
        {
            process.StandardInput.Close();
        }
        return process;
    }

    /// <summary>Takes on a program started in the terminal's pseudo-terminal from <paramref name="start"/> (a run in the
    /// Terminal window), which ends with the IDE and is logged as a child is: on macOS and Linux it leads a session and
    /// process group of its own, which goes on the watchdog's list (<see cref="Lifeline"/>); on Windows it is in Porta.Pty's
    /// job already, which the system ends once the IDE's handle to it goes. <see cref="Kill"/> then ends it with all it
    /// started; <see cref="Exited"/> logs its end.</summary>
    public static void Adopt(int processId, ProcessStartInfo start)
    {
        Lifeline.AdoptGroup(processId);
        Started?.Invoke(null, new ChildStart(processId, Display(start), start.WorkingDirectory));
    }

    /// <summary>An adopted program's end, for the IDE's log as a child's is told.</summary>
    public static void Exited(int processId, ProcessStartInfo start, int exitCode, TimeSpan time) =>
        Ended?.Invoke(null, new ChildEnd(processId, Display(start), exitCode, time));

    /// <summary>Runs a child to its end, its output and errors read whole, <paramref name="input"/> written to it first.
    /// Canceled, or still running after <paramref name="limit"/>, it is killed with all it started, and the task is
    /// canceled or fails with a <see cref="TimeoutException"/>.</summary>
    public static async Task<ChildResult> RunAsync(ProcessStartInfo start, string? input = null, TimeSpan? limit = null, CancellationToken cancel = default)
    {
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using Process process = Start(start);
        Task<string> output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using CancellationTokenSource ending = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        if (limit is { } most)
        {
            ending.CancelAfter(most);
        }
        try
        {
            try
            {
                if (input is not null)
                {
                    await process.StandardInput.WriteAsync(input.AsMemory(), ending.Token);
                }
                process.StandardInput.Close();
            }
            catch (IOException)
            {
                // It ended, or closed its input, before reading all it was given: its exit says how it went.
            }
            await process.WaitForExitAsync(ending.Token);
        }
        catch (OperationCanceledException) when (ending.IsCancellationRequested)
        {
            Kill(process);
            // Disposing the process ends the reads; their ends are nobody's to report.
            Forget(output);
            Forget(errors);
            cancel.ThrowIfCancellationRequested();
            throw new TimeoutException($"{Display(start)} had not ended after {limit.GetValueOrDefault().TotalSeconds:0} s");
        }
        return new ChildResult(process.ExitCode, await output, await errors);
    }

    /// <summary>Ends a child and everything it started, now, passing over what has ended: its tree on Windows, but for a
    /// process of it Windows will not end (one ending, or not ours where a parent's id was reused); on macOS and Linux its
    /// process group, which holds what it started and what it left behind, whether or not it has ended itself. Never the
    /// runtime's tree kill there: it stops each process before killing it, and .NET 11 on macOS handles a child's end in a
    /// loop that never ends while another child is stopped, holding the lock that the tree kill then waits on.</summary>
    public static void Kill(Process process)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (AggregateException partly) when (partly.InnerExceptions.All(e => e is Win32Exception or InvalidOperationException))
                {
                    // Windows refuses a process of the tree that has ended or is ending, or is not ours: the child must still go.
                    process.Kill();
                }
                return;
            }
            Lifeline.KillGroup(process);
            process.Kill();
        }
        catch (Exception error) when (error is InvalidOperationException or Win32Exception)
        {
            // It had ended, or was ending.
        }
    }

    /// <summary>Shows a file or folder in the Finder (in Explorer on Windows, its folder in the file manager elsewhere).
    /// The file manager is the person's, not a child: it is never ended with the IDE.</summary>
    public static void Reveal(string path)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", ["-R", path])?.Dispose();
            }
            else if (OperatingSystem.IsWindows())
            {
                // Explorer reads its command line its own way: the comma and the quotes are its.
                Process.Start("explorer.exe", $"/select,\"{path}\"")?.Dispose();
            }
            else
            {
                Process.Start("xdg-open", [Path.GetDirectoryName(path) ?? path])?.Dispose();
            }
        }
        catch (Win32Exception)
        {
            // No file manager to ask.
        }
    }

    /// <summary>A command as a person would type it, each word quoted that needs to be: for the Run window and the log.</summary>
    public static string Display(ProcessStartInfo start) =>
        string.Join(' ', start.ArgumentList.Count > 0 || start.Arguments.Length == 0
            ? [Quote(start.FileName), .. start.ArgumentList.Select(Quote)]
            : [Quote(start.FileName), start.Arguments]);

    private static string Quote(string word) =>
        word.Length > 0 && !word.Any(c => char.IsWhiteSpace(c) || c is '"' or '\'') ? word : $"\"{word.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static void OnExited(Process process, string command, long began)
    {
        try
        {
            Ended?.Invoke(null, new ChildEnd(process.Id, command, process.ExitCode, Stopwatch.GetElapsedTime(began)));
        }
        catch (InvalidOperationException)
        {
            // Disposed by its owner as it ended: there is no exit code to tell.
        }
    }

    private static void Forget(Task task) =>
        _ = task.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
}

/// <summary>How a child run to its end went: its exit code, and what it wrote to its output and errors.</summary>
public sealed record ChildResult(int ExitCode, string Output, string Error)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>A child's start: its process, the command, and the folder it runs in (the IDE's own when empty).</summary>
public sealed record ChildStart(int ProcessId, string Command, string Folder)
{
    public override string ToString() => $"Process {ProcessId} started{(Folder.Length > 0 ? $" in {Folder}" : string.Empty)}: {Command}";
}

/// <summary>A child's end: its process, the command, its exit code and how long it ran.</summary>
public sealed record ChildEnd(int ProcessId, string Command, int ExitCode, TimeSpan Time)
{
    /// <summary>Worth a line in the IDE's log: it failed, or ran long enough for a person to wait on it. Git's quick
    /// reads run on every save, and would bury the rest.</summary>
    public bool Notable => ExitCode != 0 || Time >= TimeSpan.FromSeconds(1);

    public override string ToString() => $"Process {ProcessId} exited with code {ExitCode} after {Time.TotalSeconds:0.00} s: {Command}";
}
