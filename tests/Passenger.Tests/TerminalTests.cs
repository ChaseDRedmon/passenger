using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Passenger.App;
using Passenger.Terminal;

namespace Passenger.Tests;

/// <summary>The terminal as programs use it: the screen reads xterm's sequences as xterm does (wrapping, scroll regions,
/// the alternate screen, colours, wide characters, answers to a program's questions, resizing into the scrollback),
/// and the Terminal window runs a real shell in a pseudo-terminal that takes typed commands and keys ahead of the IDE,
/// whose floods of output and stuck pastes never hold the window, and whose pastes stay text.</summary>
public sealed class TerminalTests
{
    [Fact]
    public void Text_wraps_at_the_edge_and_scrolls_into_the_scrollback()
    {
        TerminalScreen screen = new(10, 3);
        screen.Feed("abcdefghijKLM\r\n");
        Assert.Equal("abcdefghij\nKLM\n", screen.ScreenText());
        Assert.True(screen.Lines[0].Wrapped);
        for (int i = 0; i < 5; i++)
        {
            screen.Feed($"line {i}\r\n");
        }
        Assert.Equal(["abcdefghij", "KLM", "line 0", "line 1", "line 2"], screen.Scrollback.Select(l => l.Text()));
        Assert.Equal("line 3\nline 4\n", screen.ScreenText());
        Assert.Equal((2, 0), (screen.CursorRow, screen.CursorColumn));
    }

    [Fact]
    public void Clear_buffer_leaves_only_the_prompt_at_the_top()
    {
        TerminalScreen screen = new(20, 3);
        for (int i = 0; i < 6; i++)
        {
            screen.Feed($"output {i}\r\n");
        }
        screen.Feed("$ ls");
        Assert.NotEmpty(screen.Scrollback);
        screen.ClearBuffer();
        Assert.Empty(screen.Scrollback);
        Assert.Equal("$ ls\n\n", screen.ScreenText());
        Assert.Equal((0, 4), (screen.CursorRow, screen.CursorColumn));
    }

    [Fact]
    public void Cursor_addressing_and_erasing_work_as_a_full_screen_program_uses_them()
    {
        TerminalScreen screen = new(10, 4);
        screen.Feed("1111111111\r\n2222222222\r\n3333333333\r\n4444444444");
        screen.Feed("\x1b[2;3H\x1b[K");
        screen.Feed("\x1b[3;5H\x1b[1K");
        screen.Feed("\x1b[4;1H\x1b[2K!");
        Assert.Equal("1111111111\n22\n     33333\n!", screen.ScreenText());
        screen.Feed("\x1b[1;1H\x1b[J");
        Assert.Equal(string.Empty, screen.ScreenText().Trim('\n'));
    }

    [Fact]
    public void A_scroll_region_scrolls_only_its_own_rows()
    {
        TerminalScreen screen = new(8, 5);
        screen.Feed("top\r\na\r\nb\r\nc\r\nbottom");
        // Rows 2 to 4 scroll; the top and bottom rows stay where they are, and nothing reaches the scrollback.
        screen.Feed("\x1b[2;4r\x1b[4;1H\nd");
        Assert.Equal("top\nb\nc\nd\nbottom", screen.ScreenText());
        Assert.Empty(screen.Scrollback);
    }

    [Fact]
    public void The_alternate_screen_leaves_the_shells_screen_and_cursor_as_they_were()
    {
        TerminalScreen screen = new(20, 4);
        screen.Feed("$ vim\r\n");
        screen.Feed("\x1b[?1049h\x1b[H\x1b[2Jfull screen\x1b[3;3H");
        Assert.True(screen.IsAlternate);
        Assert.Equal("full screen\n\n\n", screen.ScreenText());
        screen.Feed("\x1b[?1049l");
        Assert.False(screen.IsAlternate);
        Assert.Equal("$ vim\n\n\n", screen.ScreenText());
        Assert.Equal((1, 0), (screen.CursorRow, screen.CursorColumn));
    }

    [Fact]
    public void Colours_come_from_the_palette_the_256_and_24_bit_forms_alike()
    {
        TerminalScreen screen = new(20, 2);
        screen.Feed("\x1b[1;31mA\x1b[38;5;208mB\x1b[38;2;10;20;30mC\x1b[38:2::40:50:60mD\x1b[0;44mE\x1b[0mF");
        Cell[] cells = screen.Lines[0].Cells;
        Assert.Equal((1, CellFlags.Bold), (cells[0].Style.Fore, cells[0].Style.Flags));
        Assert.Equal(208, cells[1].Style.Fore);
        Assert.Equal(CellStyle.FromRgb(10, 20, 30), cells[2].Style.Fore);
        Assert.Equal(CellStyle.FromRgb(40, 50, 60), cells[3].Style.Fore);
        Assert.Equal((CellStyle.Default, 4), (cells[4].Style.Fore, cells[4].Style.Back));
        Assert.Equal(CellStyle.Plain, cells[5].Style);
    }

    [Fact]
    public void A_wide_character_takes_two_columns_and_goes_to_the_next_row_rather_than_split()
    {
        TerminalScreen screen = new(5, 2);
        screen.Feed("abcd漢");
        Assert.Equal("abcd\n漢", screen.ScreenText());
        Assert.Equal((2, 0), (screen.Lines[1].Cells[0].Width, screen.Lines[1].Cells[1].Width));
        Assert.Equal(2, screen.CursorColumn);
    }

    [Fact]
    public void A_programs_questions_are_answered_through_the_reply()
    {
        TerminalScreen screen = new(80, 24);
        List<string> replies = [];
        screen.Reply += replies.Add;
        screen.Feed("\x1b[5;10H\x1b[6n\x1b[c");
        Assert.Equal(["\x1b[5;10R", "\x1b[?62;22c"], replies);
    }

    [Fact]
    public void Shrinking_keeps_the_cursors_line_on_screen_and_growing_brings_lines_back()
    {
        TerminalScreen screen = new(10, 5);
        screen.Feed("one\r\ntwo\r\nthree\r\nfour\r\n$ ");
        screen.Resize(10, 3);
        Assert.Equal("three\nfour\n$", screen.ScreenText());
        Assert.Equal(["one", "two"], screen.Scrollback.Select(l => l.Text()));
        Assert.Equal((2, 2), (screen.CursorRow, screen.CursorColumn));
        screen.Resize(10, 5);
        Assert.Equal("one\ntwo\nthree\nfour\n$", screen.ScreenText());
        Assert.Equal(4, screen.CursorRow);
    }

    [Fact]
    public void Keys_are_sent_as_xterm_sends_them()
    {
        Assert.Equal("\x1b[A", TerminalView.Encode(Key.Up, KeyModifiers.None, applicationCursor: false));
        Assert.Equal("\x1bOA", TerminalView.Encode(Key.Up, KeyModifiers.None, applicationCursor: true));
        Assert.Equal("\x1b[1;5C", TerminalView.Encode(Key.Right, KeyModifiers.Control, applicationCursor: false));
        Assert.Equal("\x03", TerminalView.Encode(Key.C, KeyModifiers.Control, applicationCursor: false));
        Assert.Equal("\x1b[3~", TerminalView.Encode(Key.Delete, KeyModifiers.None, applicationCursor: false));
        Assert.Equal("\x1b[Z", TerminalView.Encode(Key.Tab, KeyModifiers.Shift, applicationCursor: false));
        Assert.Null(TerminalView.Encode(Key.A, KeyModifiers.None, applicationCursor: false));
    }

    [AvaloniaFact]
    public void The_terminal_window_runs_a_shell_that_takes_typed_commands_and_hides_when_it_exits()
    {
        string folder = Directory.CreateTempSubdirectory("passenger-terminal-").FullName;
        try
        {
            MainWindow window = new() { Width = 1200, Height = 800 };
            window.Show();
            window.OpenFolder(folder);
            window.TerminalPanel.Shell = OperatingSystem.IsWindows() ? ("cmd.exe", []) : ("/bin/sh", []);
            Ui.Run(window, AppActions.ShowTerminal);
            Ui.WaitFor(() => window.TerminalPanel.Active is { IsRunning: true }, "the shell to start");
            TerminalSession shell = window.TerminalPanel.Active!;
            Ui.WaitFor(() => window.TerminalPanel.View.IsFocused, "the keys to go to the terminal");

            // Typed into the view, the command runs in the folder, its answer on the screen.
            window.KeyTextInput(OperatingSystem.IsWindows() ? "echo %CD%" : "pwd");
            Ui.Press(window, "ENTER");
            string where = Passenger.App.Workspace.PathNames.Canonical(folder);
            Ui.WaitFor(() => Screen(shell).Contains(where, StringComparison.Ordinal), $"the folder on the screen:\n{Screen(shell)}");

            // The view's size is the shell's: it fills the window, in whole cells.
            Assert.True(shell.Screen.Columns > 80, $"{shell.Screen.Columns} columns");

            // Shift+Escape is still the IDE's: it hides the window. The terminal's shortcut brings it back.
            Ui.Press(window, "shift ESCAPE");
            Assert.False(window.DockHost.IsShown("terminal"));
            Ui.Run(window, AppActions.ShowTerminal);
            Assert.True(window.DockHost.IsShown("terminal"));

            // The shell's own exit closes its tab, and with the last one the window.
            Ui.WaitFor(() => window.TerminalPanel.View.IsFocused, "the terminal focused again");
            window.KeyTextInput("exit");
            Ui.Press(window, "ENTER");
            Ui.WaitFor(() => window.TerminalPanel.Tabs.Count == 0, "the tab to close");
            Assert.False(window.DockHost.IsShown("terminal"));
            window.Close();
        }
        finally
        {
            TestFolders.Delete(folder);
        }
    }

    [AvaloniaFact]
    public void A_flood_of_output_leaves_the_window_taking_keys_and_Ctrl_C_stops_it()
    {
        if (OperatingSystem.IsWindows())
        {
            // sh, cat and a terminal's own Ctrl+C are Unix's.
            return;
        }
        string folder = Directory.CreateTempSubdirectory("passenger-terminal-").FullName;
        try
        {
            // 50 MB of lines, the last one saying so.
            using (StreamWriter big = new(Path.Combine(folder, "big.txt")))
            {
                string filler = new('x', 86);
                for (int i = 0; i < 500_000; i++)
                {
                    big.Write($"line {i.ToString("D7", CultureInfo.InvariantCulture)} {filler}\n");
                }
                big.Write("the end of big.txt\n");
            }
            (MainWindow window, TerminalSession shell) = Open(folder, "/bin/sh");
            using (InputWait waits = new())
            {
                window.KeyTextInput("cat big.txt");
                Ui.Press(window, "ENTER");
                Ui.WaitFor(() => shell.Screen.ScreenText().Contains("xxxxxxxx", StringComparison.Ordinal), "the file pouring onto the screen");
                // A second more of the flood, the window running as it does on a desktop.
                Screenshot.Pump(TimeSpan.FromSeconds(1));
                waits.Stop();
                Assert.True(waits.Answered >= 20, $"only {waits.Answered} looks at the window's thread");
                Assert.True(waits.Longest < TimeSpan.FromMilliseconds(250),
                    $"a key waited {waits.Longest.TotalMilliseconds:F0} ms for the window while cat's output poured in");
            }

            // The window falls behind for a moment, as it would drawing a heavy frame: cat waits for it, its output not
            // read into memory meanwhile, so Ctrl+C still finds it running.
            Thread.Sleep(2000);
            Ui.Press(window, "control C");
            window.KeyTextInput("echo after-$((6*7))");
            Ui.Press(window, "ENTER");
            Ui.WaitFor(() => shell.Screen.ScreenText().Contains("after-42", StringComparison.Ordinal), $"the shell's answer after Ctrl+C:\n{shell.Screen.ScreenText()}", TimeSpan.FromSeconds(30));
            Assert.False(Screen(shell).Contains("the end of big.txt", StringComparison.Ordinal),
                "cat ran to the end of its 50 MB: Ctrl+C never reached it while the window took the output");
            window.Close();
        }
        finally
        {
            TestFolders.Delete(folder);
        }
    }

    [AvaloniaFact]
    public void A_paste_into_a_program_that_reads_nothing_leaves_the_window_taking_keys()
    {
        if (OperatingSystem.IsWindows())
        {
            // A Unix pseudo-terminal's input queue holds a kilobyte or so; past that, a write waits for the program.
            return;
        }
        string folder = Directory.CreateTempSubdirectory("passenger-terminal-").FullName;
        try
        {
            (MainWindow window, TerminalSession shell) = Open(folder, "/bin/sh");
            window.KeyTextInput("sleep 30");
            Ui.Press(window, "ENTER");
            Ui.WaitFor(() => shell.Screen.CursorRow > 0 && shell.Screen.Lines[shell.Screen.CursorRow - 1].Text().Contains("sleep 30", StringComparison.Ordinal),
                "sleep to start");

            // Far more than the pseudo-terminal holds, while sleep reads none of it.
            Copy(window, string.Concat(Enumerable.Range(0, 2000).Select(i => $"echo pasted {i}\n")));
            Stopwatch pasting = Stopwatch.StartNew();
            Ui.Press(window, PasteKey);
            Assert.True(pasting.Elapsed < TimeSpan.FromSeconds(1), $"the paste held the window for {pasting.ElapsedMilliseconds} ms");
            // What the pseudo-terminal took it echoes, so the paste did go.
            Ui.WaitFor(() => Screen(shell).Contains("echo pasted 0", StringComparison.Ordinal), "the start of the paste echoed", TimeSpan.FromSeconds(10));

            // The window still takes the IDE's keys: Shift+Escape hides the terminal.
            Ui.Press(window, "shift ESCAPE");
            Assert.False(window.DockHost.IsShown("terminal"), "the terminal is still shown after Shift+Escape");
            window.Close();
        }
        finally
        {
            TestFolders.Delete(folder);
        }
    }

    [AvaloniaFact]
    public void A_paste_cannot_end_its_own_bracket_and_run_the_line_after_it()
    {
        if (!File.Exists("/bin/zsh"))
        {
            // zsh's line editor brackets pastes (as the Mac's own shell, it is always there on a Mac).
            return;
        }
        string folder = Directory.CreateTempSubdirectory("passenger-terminal-").FullName;
        try
        {
            (MainWindow window, TerminalSession shell) = Open(folder, "/bin/zsh", "-f");
            Ui.WaitFor(() => shell.Screen.BracketedPaste, "zsh to ask for bracketed pastes");

            // A clipboard that ends the bracket itself, then a line to run, as a web page can put there.
            Copy(window, "echo a\x1b[201~\rtouch hacked\n");
            Ui.Press(window, PasteKey);
            Ui.WaitFor(() => shell.Screen.ScreenText().Contains("touch hacked", StringComparison.Ordinal), $"the paste in zsh's line:\n{shell.Screen.ScreenText()}");

            // The paste is text in the line being edited, which Ctrl+C throws away; anything run would have run by the answer.
            Ui.Press(window, "control C");
            window.KeyTextInput("echo checked-$((6*7))");
            Ui.Press(window, "ENTER");
            Ui.WaitFor(() => shell.Screen.ScreenText().Contains("checked-42", StringComparison.Ordinal), $"zsh's answer:\n{shell.Screen.ScreenText()}");
            Assert.False(File.Exists(Path.Combine(folder, "hacked")), "the paste ended its own bracket and ran the line after it");
            window.Close();
        }
        finally
        {
            TestFolders.Delete(folder);
        }
    }

    /// <summary>The window over a folder, its Terminal open on a shell of the test's and the keys going to it.</summary>
    private static (MainWindow Window, TerminalSession Shell) Open(string folder, string shell, params string[] arguments)
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        window.TerminalPanel.Shell = (shell, arguments);
        Ui.Run(window, AppActions.ShowTerminal);
        Ui.WaitFor(() => window.TerminalPanel.Active is { IsRunning: true }, "the shell to start");
        Ui.WaitFor(() => window.TerminalPanel.View.IsFocused, "the keys to go to the terminal");
        Ui.WaitFor(() => window.TerminalPanel.Active!.Screen.CursorColumn > 0, "the shell's prompt");
        return (window, window.TerminalPanel.Active!);
    }

    /// <summary>The terminal's Paste: Cmd+V on the Mac, Ctrl+Shift+V elsewhere.</summary>
    private static string PasteKey => OperatingSystem.IsMacOS() ? "meta V" : "control shift V";

    private static void Copy(TopLevel window, string text)
    {
        Task copied = window.Clipboard!.SetTextAsync(text);
        while (!copied.IsCompleted)
        {
            Ui.Settle();
        }
    }

    /// <summary>How long a key waits for the window's thread: work posted at input's priority every 20 ms, as the
    /// platform's keys wait for the thread between jobs, the longest wait kept until <see cref="Stop"/>.</summary>
    private sealed class InputWait : IDisposable
    {
        private volatile bool stopped;

        public InputWait()
        {
            _ = Task.Run(async () =>
            {
                while (!stopped)
                {
                    long posted = Stopwatch.GetTimestamp();
                    Dispatcher.UIThread.Post(() => Answer(Stopwatch.GetElapsedTime(posted)), DispatcherPriority.Input);
                    await Task.Delay(20);
                }
            });
        }

        public TimeSpan Longest { get; private set; }

        public int Answered { get; private set; }

        /// <summary>Stops measuring, on the window's thread, so a wait while it is not running never counts.</summary>
        public void Stop() => stopped = true;

        public void Dispose() => Stop();

        private void Answer(TimeSpan waited)
        {
            if (stopped)
            {
                return;
            }
            Answered++;
            Longest = waited > Longest ? waited : Longest;
        }
    }

    private static string Screen(TerminalSession session) =>
        string.Join('\n', session.Screen.Scrollback.Select(l => l.Text()).Concat(session.Screen.Lines.Select(l => l.Text())));
}
