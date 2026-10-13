using System.Diagnostics;
using System.Text;
using Avalonia.Threading;
using Passenger.Editor;

namespace Passenger.App.ToolWindows;

/// <summary>Where text written to a console came from: a program's output or errors, or the debugger's own words. Each
/// has its colour-code state of its own, so one stream's unfinished sequence never swallows another's text.</summary>
public enum ConsoleStream
{
    Output,
    Error,
    System,
}

/// <summary>A stretch of a batch of console text and the stream it came from.</summary>
public readonly record struct ConsoleRun(int Start, int Length, ConsoleStream Stream);

/// <summary>What a person types into a console goes to the program through these: a line at a time with its line break,
/// then the end of its input (Ctrl+D). Called on the UI thread; neither may wait on the program.</summary>
public sealed record ConsoleInput(Action<string> Send, Action Close);

/// <summary>The one way output reaches a console (Run, Build and Debug alike), from any thread: terminal control codes are
/// taken out as it comes, each stream's state kept from one write to the next, so a sequence split between two reads
/// never shows as text, nor a "\r\n" split so as an extra line. The console takes what waits a batch at a time, at most
/// once a frame and below the window's input and drawing, so a program that writes without pause never floods the
/// window; what waits is never more than the console keeps, the oldest dropped first.</summary>
public sealed class ConsoleSink
{
    /// <summary>A frame's time: batches come no closer together than this.</summary>
    private static readonly TimeSpan Frame = TimeSpan.FromMilliseconds(16);

    private readonly object gate = new();
    private readonly Action take;
    private readonly ControlCodes[] streams = [new(), new(), new()];
    private StringBuilder waiting = new();
    private List<ConsoleRun> runs = [];
    private int limit;
    private bool queued;
    private long lastTaken;

    /// <param name="take">The console's taking of a batch (<see cref="Take"/>), posted to the UI thread.</param>
    /// <param name="limit">How much text the console keeps, in characters.</param>
    internal ConsoleSink(Action take, int limit)
    {
        this.take = take;
        this.limit = limit;
    }

    /// <summary>How much text the console keeps, in characters: what waits beyond it could never be shown.</summary>
    internal int Limit
    {
        set
        {
            lock (gate)
            {
                limit = value;
            }
        }
    }

    /// <summary>Adds what a stream wrote; from any thread.</summary>
    public void Write(ReadOnlySpan<char> text, ConsoleStream stream = ConsoleStream.Output)
    {
        bool post;
        lock (gate)
        {
            int before = waiting.Length;
            streams[(int)stream].Strip(text, waiting);
            post = Added(before, stream);
        }
        if (post)
        {
            Post();
        }
    }

    /// <inheritdoc cref="Write(ReadOnlySpan{char}, ConsoleStream)"/>
    public void Write(string text, ConsoleStream stream = ConsoleStream.Output) => Write(text.AsSpan(), stream);

    /// <summary>A stream has ended (its program has): a '\r' it held back, in case a '\n' came next, is let go, and an
    /// unfinished control sequence is dropped, so the next program's text starts clean.</summary>
    public void End(ConsoleStream stream)
    {
        bool post;
        lock (gate)
        {
            int before = waiting.Length;
            streams[(int)stream].End(waiting);
            post = Added(before, stream);
        }
        if (post)
        {
            Post();
        }
    }

    /// <summary>The waiting text and the streams it came from, taken for the console; null when nothing waits. UI thread.</summary>
    internal (string Text, List<ConsoleRun> Runs)? Take()
    {
        lock (gate)
        {
            queued = false;
            lastTaken = Stopwatch.GetTimestamp();
            if (waiting.Length == 0)
            {
                return null;
            }
            string text = waiting.ToString();
            List<ConsoleRun> taken = runs;
            waiting = new StringBuilder();
            runs = [];
            return (text, taken);
        }
    }

    /// <summary>Drops what waits, for a console cleared.</summary>
    internal void Drop()
    {
        lock (gate)
        {
            waiting.Clear();
            runs.Clear();
        }
    }

    /// <summary>Notes the text a write added, keeps what waits within the console's limit, and says whether the console
    /// must be told: once for all that comes before it takes the batch.</summary>
    private bool Added(int before, ConsoleStream stream)
    {
        int added = waiting.Length - before;
        if (added == 0)
        {
            return false;
        }
        if (runs.Count > 0 && runs[^1].Stream == stream && runs[^1].Start + runs[^1].Length == before)
        {
            runs[^1] = runs[^1] with { Length = runs[^1].Length + added };
        }
        else
        {
            runs.Add(new ConsoleRun(before, added, stream));
        }
        // Twice the limit before cutting, so a flood moves the waiting text along now and then rather than at every write.
        if (waiting.Length > 2 * limit)
        {
            Cut(waiting.Length - limit);
        }
        if (queued)
        {
            return false;
        }
        queued = true;
        return true;
    }

    private void Cut(int length)
    {
        waiting.Remove(0, length);
        List<ConsoleRun> kept = [];
        foreach (ConsoleRun run in runs)
        {
            int start = Math.Max(run.Start, length);
            int end = run.Start + run.Length;
            if (end > start)
            {
                kept.Add(run with { Start = start - length, Length = end - start });
            }
        }
        runs = kept;
    }

    /// <summary>Has the console take the batch on the UI thread a frame after it took the last, below input and drawing.
    /// The wait is background work, so a screenshot or a test settles on the output it brings.</summary>
    private void Post()
    {
        TimeSpan wait;
        lock (gate)
        {
            wait = Frame - Stopwatch.GetElapsedTime(lastTaken);
        }
        if (wait <= TimeSpan.Zero)
        {
            UiThread.Post(take, DispatcherPriority.Background);
            return;
        }
        IDisposable held = BackgroundWork.Scope();
        _ = Task.Delay(wait).ContinueWith(_ =>
        {
            UiThread.Post(take, DispatcherPriority.Background);
            held.Dispose();
        }, TaskScheduler.Default);
    }

    /// <summary>One stream's terminal control sequences taken out of its text, in the shapes ECMA-48 gives them and xterm
    /// reads them: CSI (colours, cursor moves) to its final byte, OSC (titles, links) to BEL or ST, DCS, SOS, PM and APC
    /// strings to ST, and the other escapes. A sequence broken by a line break, or longer than any real one, is given up
    /// and the text after it kept.</summary>
    private sealed class ControlCodes
    {
        private const char Escape = '\u001b';
        private const char Bell = '\u0007';
        private const int Longest = 4096;

        private State state;
        private int length;
        private bool carriageReturn;

        private enum State
        {
            Text,
            Escape,
            EscapeIntermediate,
            Csi,
            String,
            StringEscape,
        }

        public void Strip(ReadOnlySpan<char> text, StringBuilder into)
        {
            int i = 0;
            while (i < text.Length)
            {
                if (state == State.Text)
                {
                    if (carriageReturn)
                    {
                        carriageReturn = false;
                        if (text[i] != '\n')
                        {
                            into.Append('\r');
                        }
                    }
                    int found = text[i..].IndexOfAny(Escape, '\r', Bell);
                    int end = found < 0 ? text.Length : i + found;
                    into.Append(text[i..end]);
                    if (found < 0)
                    {
                        return;
                    }
                    i = end + 1;
                    if (text[end] == '\r')
                    {
                        // Held back until the next character says whether it begins a line break.
                        carriageReturn = true;
                    }
                    else if (text[end] == Escape)
                    {
                        state = State.Escape;
                        length = 0;
                    }
                    continue;
                }
                if (Take(text[i]))
                {
                    i++;
                }
                else
                {
                    // Not part of a sequence: the sequence is given up, and the character is text again.
                    state = State.Text;
                }
            }
        }

        public void End(StringBuilder into)
        {
            if (carriageReturn)
            {
                into.Append('\r');
            }
            carriageReturn = false;
            state = State.Text;
        }

        /// <summary>Takes one character of a sequence under way; false when it cannot belong to it.</summary>
        private bool Take(char c)
        {
            if (++length > Longest || c is '\n' or '\r')
            {
                return false;
            }
            switch (state)
            {
                case State.Escape:
                    state = c switch
                    {
                        '[' => State.Csi,
                        ']' or 'P' or 'X' or '^' or '_' => State.String,
                        >= ' ' and <= '/' => State.EscapeIntermediate,
                        >= '0' and <= '~' => State.Text,
                        Escape => State.Escape,
                        _ => State.Text,
                    };
                    return c is >= ' ' and <= '~' or Escape;
                case State.EscapeIntermediate:
                    if (c is >= ' ' and <= '/')
                    {
                        return true;
                    }
                    return Final(c, '0');
                case State.Csi:
                    if (c is >= '0' and <= '?' or >= ' ' and <= '/')
                    {
                        return true;
                    }
                    return Final(c, '@');
                case State.String:
                    if (c == Bell)
                    {
                        state = State.Text;
                    }
                    else if (c == Escape)
                    {
                        state = State.StringEscape;
                    }
                    return true;
                case State.StringEscape:
                    if (c == '\\')
                    {
                        state = State.Text;
                        return true;
                    }
                    // Another escape begins: the string ended with this one.
                    state = State.Escape;
                    return Take(c);
                default:
                    return false;
            }
        }

        /// <summary>A sequence's last character, from <paramref name="first"/> to '~'; ESC begins another instead.</summary>
        private bool Final(char c, char first)
        {
            if (c >= first && c <= '~')
            {
                state = State.Text;
                return true;
            }
            if (c == Escape)
            {
                state = State.Escape;
                length = 0;
                return true;
            }
            return false;
        }
    }
}
