using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Passenger.Debugger.Interop;

namespace Passenger.Debugger;

/// <summary>A program started suspended by dbgshim (<c>CreateProcessForLaunch</c>), as netcoredbg starts one on
/// macOS and Linux: held before its first instruction until <see cref="Resume"/>, with no environment variable
/// to hold it, so a .NET program it starts in turn (a build, a language server) runs as it would undebugged.
/// dbgshim gives the program the adapter's own standard streams, so for the moment of the launch they are its
/// pipes, which is why the protocol never runs over descriptors 0 and 1 (<see cref="DetachProtocol"/>).</summary>
public sealed partial class SuspendedLaunch : IDisposable
{
    private static readonly Lock Swap = new();
    private nint resumeHandle;

    private SuspendedLaunch(int processId, nint resumeHandle, Stream input, Stream output, Stream error)
    {
        ProcessId = processId;
        this.resumeHandle = resumeHandle;
        Input = input;
        Output = output;
        Error = error;
    }

    public int ProcessId { get; }

    /// <summary>The program's standard input, written by the client's console.</summary>
    public Stream Input { get; }

    public Stream Output { get; }
    public Stream Error { get; }

    /// <summary>Moves the adapter's protocol off its standard input and output onto descriptors of its own,
    /// closed on exec so no program ever inherits them, and points 0 at /dev/null and 1 at standard error,
    /// so a stray write to the console can never corrupt the protocol either.</summary>
    public static (Stream Input, Stream Output) DetachProtocol()
    {
        Unix();
        SafeFileHandle input = new(Check(Dup(0), "dup"), ownsHandle: true);
        SafeFileHandle output = new(Check(Dup(1), "dup"), ownsHandle: true);
        using SafeFileHandle nothing = File.OpenHandle("/dev/null", FileMode.Open, FileAccess.Read);
        Check(Dup2((int)nothing.DangerousGetHandle(), 0), "dup2");
        Check(Dup2(2, 1), "dup2");
        return (new FileStream(input, FileAccess.Read, 1), new FileStream(output, FileAccess.Write, 1));
    }

    /// <summary>Starts a program suspended, in a folder, with an environment (the adapter's, with these
    /// variables over it) and its standard streams on pipes.</summary>
    public static SuspendedLaunch Start(string program, IReadOnlyList<string> arguments, string? folder, IReadOnlyDictionary<string, string> variables)
    {
        Unix();
        Dictionary<string, string> environment = [];
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            environment[(string)entry.Key] = (string?)entry.Value ?? string.Empty;
        }
        foreach ((string key, string value) in variables)
        {
            environment[key] = value;
        }
        // dbgshim hands the block to the platform layer as narrow characters, whatever the command line is.
        StringBuilder block = new();
        foreach ((string key, string value) in environment)
        {
            block.Append(key).Append('=').Append(value).Append('\0');
        }
        block.Append('\0');
        byte[] encoded = Encoding.UTF8.GetBytes(block.ToString());

        (int childIn, int parentIn) = Pipe();
        (int parentOut, int childOut) = Pipe();
        (int parentErr, int childErr) = Pipe();
        nint environmentBlock = Marshal.AllocHGlobal(encoded.Length);
        Marshal.Copy(encoded, 0, environmentBlock, encoded.Length);
        try
        {
            int processId;
            nint resume;
            lock (Swap)
            {
                // The program takes descriptors 0, 1 and 2 as they are when dbgshim forks it.
                int savedIn = Check(Dup(0), "dup");
                int savedOut = Check(Dup(1), "dup");
                int savedErr = Check(Dup(2), "dup");
                try
                {
                    Check(Dup2(childIn, 0), "dup2");
                    Check(Dup2(childOut, 1), "dup2");
                    Check(Dup2(childErr, 2), "dup2");
                    int hr = DbgShim.CreateProcessForLaunch(CommandLine(program, arguments), true, environmentBlock, folder, out uint pid, out resume);
                    Marshal.ThrowExceptionForHR(hr);
                    processId = checked((int)pid);
                }
                finally
                {
                    Dup2(savedIn, 0);
                    Dup2(savedOut, 1);
                    Dup2(savedErr, 2);
                    Close(savedIn);
                    Close(savedOut);
                    Close(savedErr);
                }
            }
            return new SuspendedLaunch(processId, resume, Stream(parentIn, FileAccess.Write), Stream(parentOut, FileAccess.Read), Stream(parentErr, FileAccess.Read));
        }
        catch
        {
            Close(parentIn);
            Close(parentOut);
            Close(parentErr);
            throw;
        }
        finally
        {
            Marshal.FreeHGlobal(environmentBlock);
            Close(childIn);
            Close(childOut);
            Close(childErr);
        }
    }

    /// <summary>Lets the program run its first instruction.</summary>
    public void Resume()
    {
        if (resumeHandle != 0)
        {
            Marshal.ThrowExceptionForHR(DbgShim.ResumeProcess(resumeHandle));
            DbgShim.CloseResumeHandle(resumeHandle);
            resumeHandle = 0;
        }
    }

    /// <summary>Waits for the program to end and says how: its exit code, or 128 and the signal that ended
    /// it, as a shell does. Null when it was not this process's child to wait for.</summary>
    public int? WaitForExit()
    {
        int status;
        int result;
        do
        {
            result = WaitPid(ProcessId, out status, 0);
        }
        while (result == -1 && Marshal.GetLastPInvokeError() == 4); // EINTR
        if (result != ProcessId)
        {
            return null;
        }
        int signal = status & 0x7f;
        return signal == 0 ? (status >> 8) & 0xff : 128 + signal;
    }

    public void Kill() => _ = SignalProcess(ProcessId, SigKill);

    public void Dispose()
    {
        if (resumeHandle != 0)
        {
            DbgShim.CloseResumeHandle(resumeHandle);
            resumeHandle = 0;
        }
        Input.Dispose();
        Output.Dispose();
        Error.Dispose();
    }

    /// <summary>The program and its arguments as one command line, quoted as dbgshim's own parser (the
    /// runtime's platform layer, which follows Windows' rules) splits it back.</summary>
    internal static string CommandLine(string program, IReadOnlyList<string> arguments)
    {
        StringBuilder line = new();
        foreach (string argument in arguments.Prepend(program))
        {
            if (line.Length > 0)
            {
                line.Append(' ');
            }
            if (argument.Length > 0 && !argument.Any(c => char.IsWhiteSpace(c) || c == '"'))
            {
                line.Append(argument);
                continue;
            }
            line.Append('"');
            int backslashes = 0;
            foreach (char c in argument)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                // Backslashes before a quote are doubled, and the quote escaped; elsewhere they stand as they are.
                line.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes);
                backslashes = 0;
                line.Append(c);
            }
            line.Append('\\', backslashes * 2).Append('"');
        }
        return line.ToString();
    }

    private static void Unix()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows launches through the diagnostic port until it has its own suspended start");
        }
    }

    private static (int Read, int Write) Pipe()
    {
        Span<int> ends = stackalloc int[2];
        Check(SystemNativePipe(ref MemoryMarshal.GetReference(ends), CloseOnExec), "pipe");
        return (ends[0], ends[1]);
    }

    private static FileStream Stream(int descriptor, FileAccess access) => new(new SafeFileHandle(descriptor, ownsHandle: true), access, 1);

    private static int Check(int result, string call) =>
        result >= 0 ? result : throw new IOException($"{call} failed: errno {Marshal.GetLastPInvokeError()}");

    private static int Dup(int descriptor) => checked((int)SystemNativeDup(descriptor));

    // The runtime's own shim, whose descriptors are opened close-on-exec (fcntl is variadic, and a variadic
    // call is not one P/Invoke can make on Apple silicon).
    private const int CloseOnExec = 0x0010;
    private const int SigKill = 9;

    [LibraryImport("libSystem.Native", EntryPoint = "SystemNative_Dup", SetLastError = true)]
    private static partial nint SystemNativeDup(nint descriptor);

    [LibraryImport("libSystem.Native", EntryPoint = "SystemNative_Pipe", SetLastError = true)]
    private static partial int SystemNativePipe(ref int ends, int flags);

    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int SignalProcess(int processId, int signal);

    [LibraryImport("libc", EntryPoint = "dup2", SetLastError = true)]
    private static partial int Dup2(int from, int to);

    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int Close(int descriptor);

    [LibraryImport("libc", EntryPoint = "waitpid", SetLastError = true)]
    private static partial int WaitPid(int processId, out int status, int options);
}
