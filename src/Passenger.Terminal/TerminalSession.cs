using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Avalonia.Threading;
using Porta.Pty;

namespace Passenger.Terminal;

/// <summary>A shell in a pseudo-terminal, as Rider's terminal runs one: the user's login shell in the folder, its
/// output read off the window's thread and fed to the <see cref="TerminalScreen"/> on it a slice at a time, keys and
/// pastes written to it off the window's thread, its size kept to the view's. Porta.Pty makes the pseudo-terminal
/// (forkpty in a native shim on macOS and Linux, ConPTY on Windows).</summary>
public sealed class TerminalSession : IAsyncDisposable
{
    /// <summary>Output read but not yet on the screen past which reading waits, so a program writing faster than the
    /// window draws is held in its own write, as on a slow terminal, instead of its output piling up in memory.</summary>
    private const int MostHeld = 256 * 1024;

    /// <summary>How much the screen takes between looks at the clock.</summary>
    private const int Piece = 4096;

    /// <summary>The longest the window's thread parses output in one go before it takes keys and draws again.</summary>
    private static readonly TimeSpan Slice = TimeSpan.FromMilliseconds(10);

    private readonly ConcurrentQueue<byte[]> arrived = new();
    private readonly SemaphoreSlim room = new(0);
    private readonly Channel<byte[]> outgoing = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });
    private IPtyConnection? pty;
    private byte[]? feeding;
    private int fed;
    private int held;
    private int posted;
    private volatile bool closed;

    public TerminalSession(int columns = 80, int rows = 24)
    {
        Screen = new TerminalScreen(columns, rows);
        Screen.Reply += Write;
    }

    public TerminalScreen Screen { get; }

    /// <summary>Raised on the window's thread when output has been taken onto the screen.</summary>
    public event EventHandler? Output;

    /// <summary>Raised on the window's thread when the shell has ended, with its exit code.</summary>
    public event EventHandler<int>? Exited;

    public bool IsRunning { get; private set; }

    /// <summary>The shell's process id, once it runs.</summary>
    public int? ProcessId => pty?.Pid;

    /// <summary>Starts the shell in a folder. <paramref name="shell"/> names one other than the user's own.</summary>
    public async Task StartAsync(string folder, string? shell = null, IReadOnlyList<string>? arguments = null)
    {
        (string app, string[] args) = shell is null ? DefaultShell() : (shell, [.. arguments ?? []]);
        Dictionary<string, string> environment = new()
        {
            ["TERM"] = "xterm-256color",
            ["COLORTERM"] = "truecolor",
            ["TERM_PROGRAM"] = "Passenger",
        };
        // An app opened from the Finder has no LANG, and a shell without one misreads every UTF-8 character.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LANG")) && !OperatingSystem.IsWindows())
        {
            environment["LANG"] = "en_US.UTF-8";
        }
        PtyOptions options = new()
        {
            Name = "Passenger",
            Cols = Screen.Columns,
            Rows = Screen.Rows,
            Cwd = Directory.Exists(folder) ? folder : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            App = app,
            CommandLine = args,
            Environment = environment,
        };
        pty = await PtyProvider.SpawnAsync(options, CancellationToken.None);
        IsRunning = true;
        pty.ProcessExited += (_, e) => Dispatcher.UIThread.Post(() =>
        {
            IsRunning = false;
            Exited?.Invoke(this, e.ExitCode);
        });
        Stream reader = pty.ReaderStream;
        Stream writer = pty.WriterStream;
        _ = Task.Run(() => ReadAsync(reader));
        _ = Task.Run(() => SendAsync(writer));
    }

    /// <summary>Text for the shell, as typed.</summary>
    public void Write(string text) => Write(Encoding.UTF8.GetBytes(text));

    /// <summary>Bytes for the shell, in order with everything written before. They go from another thread: a program
    /// that reads nothing (a paste during sleep 30) leaves that write waiting in the kernel, never the window.</summary>
    public void Write(byte[] bytes)
    {
        if (pty is null || !IsRunning || bytes.Length == 0)
        {
            return;
        }
        outgoing.Writer.TryWrite(bytes);
    }

    /// <summary>A paste: line breaks as Enter sends them, every other control character left out, as xterm and Windows
    /// Terminal filter one (so no ESC in it can end its own bracket, nor a Ctrl+C or an escape sequence act as keys),
    /// and bracketed when the program asked (so a shell takes it as text and runs none of its lines).</summary>
    public void Paste(string text)
    {
        StringBuilder kept = new(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
            {
                continue;
            }
            if (c is '\r' or '\n')
            {
                kept.Append('\r');
            }
            else if (c == '\t' || !char.IsControl(c))
            {
                kept.Append(c);
            }
        }
        if (kept.Length == 0)
        {
            return;
        }
        Write(Screen.BracketedPaste ? $"\x1b[200~{kept}\x1b[201~" : kept.ToString());
    }

    public void Resize(int columns, int rows)
    {
        if (columns == Screen.Columns && rows == Screen.Rows)
        {
            return;
        }
        Screen.Resize(columns, rows);
        try
        {
            pty?.Resize(columns, rows);
        }
        catch (Exception failed) when (failed is IOException or InvalidOperationException)
        {
            // A shell that has just ended has no size to set.
        }
    }

    public void Kill()
    {
        try
        {
            pty?.Kill();
        }
        catch (Exception failed) when (failed is IOException or InvalidOperationException)
        {
            // Already gone.
        }
    }

    public ValueTask DisposeAsync()
    {
        closed = true;
        outgoing.Writer.TryComplete();
        room.Release();
        if (IsRunning)
        {
            Kill();
        }
        pty?.Dispose();
        pty = null;
        return ValueTask.CompletedTask;
    }

    /// <summary>The user's shell as a login shell, as Rider starts it: $SHELL (zsh where unset on macOS, bash on
    /// Linux); PowerShell on Windows, the newer one when it is installed.</summary>
    public static (string App, string[] Arguments) DefaultShell()
    {
        if (OperatingSystem.IsWindows())
        {
            string? pwsh = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(folder => Path.Combine(folder, "pwsh.exe"))
                .FirstOrDefault(File.Exists);
            return (pwsh ?? Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"), ["-NoLogo"]);
        }
        string shell = Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } own && File.Exists(own)
            ? own
            : OperatingSystem.IsMacOS() ? "/bin/zsh" : "/bin/bash";
        return (shell, ["-l"]);
    }

    /// <summary>Reads the shell's output as it comes and hands it to the window's thread, waiting while the screen has
    /// <see cref="MostHeld"/> still to take.</summary>
    private async Task ReadAsync(Stream reader)
    {
        byte[] buffer = new byte[64 * 1024];
        try
        {
            while (!closed)
            {
                int read = await reader.ReadAsync(buffer);
                if (read <= 0)
                {
                    break;
                }
                arrived.Enqueue(buffer[..read]);
                Interlocked.Add(ref held, read);
                if (Interlocked.Exchange(ref posted, 1) == 0)
                {
                    Dispatcher.UIThread.Post(TakeOutput, DispatcherPriority.Input);
                }
                while (Volatile.Read(ref held) >= MostHeld && !closed)
                {
                    await room.WaitAsync();
                }
            }
        }
        catch (Exception failed) when (failed is IOException or ObjectDisposedException)
        {
            // The pseudo-terminal closed with the shell.
        }
    }

    /// <summary>Writes what was typed and pasted, in order, for as long as the shell runs.</summary>
    private async Task SendAsync(Stream writer)
    {
        try
        {
            await foreach (byte[] bytes in outgoing.Reader.ReadAllAsync())
            {
                await writer.WriteAsync(bytes);
                await writer.FlushAsync();
            }
        }
        catch (Exception failed) when (failed is IOException or ObjectDisposedException)
        {
            // The shell has gone; its exit says so.
        }
    }

    /// <summary>Puts output on the screen for at most a <see cref="Slice"/>, then, while more comes, lets the window take
    /// its keys and draw before the next, as xterm.js parses: a flood of output never holds the window nor keeps Ctrl+C
    /// from the program. The next waits on a timer, since one posted would run ahead of the keys.</summary>
    private void TakeOutput()
    {
        if (closed)
        {
            return;
        }
        long started = Stopwatch.GetTimestamp();
        int taken = 0;
        while (Stopwatch.GetElapsedTime(started) < Slice)
        {
            if (feeding is null || fed == feeding.Length)
            {
                if (!arrived.TryDequeue(out feeding))
                {
                    break;
                }
                fed = 0;
            }
            int piece = Math.Min(Piece, feeding.Length - fed);
            Screen.Feed(feeding.AsSpan(fed, piece));
            fed += piece;
            taken += piece;
        }
        if (taken > 0)
        {
            // Only this thread releases, so the reader is never owed more than one wake.
            if (Interlocked.Add(ref held, -taken) < MostHeld && room.CurrentCount == 0)
            {
                room.Release();
            }
            Output?.Invoke(this, EventArgs.Empty);
            // Even when this slice emptied the queue, the reader's next post would run straight after it.
            DispatcherTimer.RunOnce(TakeOutput, TimeSpan.FromMilliseconds(1), DispatcherPriority.Input);
            return;
        }
        Interlocked.Exchange(ref posted, 0);
        // Output that came after the queue was found empty, whose reader saw this still posted.
        if (!arrived.IsEmpty && Interlocked.Exchange(ref posted, 1) == 0)
        {
            Dispatcher.UIThread.Post(TakeOutput, DispatcherPriority.Input);
        }
    }
}
