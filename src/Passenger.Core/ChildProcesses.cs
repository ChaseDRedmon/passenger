using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Passenger.Core;

/// <summary>Every program Passenger starts is started here, one way: no shell and no window, an input of its own (never
/// the IDE's terminal, nor an agent's pipe), UTF-8 streams, none of a git hook's repository variables, and a line in the
/// log when it fails or takes a while (<see cref="Ended"/>). None outlives the IDE, however the IDE ends
/// (<see cref="Lifeline"/>). Not started here: the terminal's shell (Porta.Pty's; its pseudo-terminal's hangup ends it)
/// and vstest.console (VSTest's own, which ends when the process that started it does).</summary>
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

    /// <summary>The .NET command line (<see cref="Dotnet.Host"/>) with these arguments: in English, as the IDE reads what
    /// it says, and with no first-run banner. A <see cref="FileNotFoundException"/> when there is none.</summary>
    public static ProcessStartInfo DotnetCommand(IEnumerable<string> arguments, string? folder = null)
    {
        ProcessStartInfo start = Command(Dotnet.Host(), arguments, folder);
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        start.Environment["DOTNET_NOLOGO"] = "1";
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
        if (closeInput)
        {
            process.StandardInput.Close();
        }
        return process;
    }

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

/// <summary>A child's end: its process, the command, its exit code and how long it ran.</summary>
public sealed record ChildEnd(int ProcessId, string Command, int ExitCode, TimeSpan Time)
{
    /// <summary>Worth a line in the IDE's log: it failed, or ran long enough for a person to wait on it. Git's quick
    /// reads run on every save, and would bury the rest.</summary>
    public bool Notable => ExitCode != 0 || Time >= TimeSpan.FromSeconds(1);

    public override string ToString() => $"Process {ProcessId} exited with code {ExitCode} after {Time.TotalSeconds:0.00} s: {Command}";
}
