using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Passenger.App;
using Passenger.App.Docking;
using Passenger.Core;
using Passenger.Terminal;

namespace Passenger.Tests;

/// <summary>The terminal as programs use it: the screen reads xterm's sequences as xterm does (wrapping, scroll regions,
/// the alternate screen, colours, wide characters, answers to a program's questions, resizing into the scrollback),
/// and the Terminal window runs a real shell in a pseudo-terminal that takes typed commands and keys ahead of the IDE,
/// whose floods of output and stuck pastes never hold the window, and whose pastes stay text; accents and emoji take
/// the columns the shell counts, lines reflow as it resizes, and a place or a selection holds as output streams.</summary>
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
            string where = PathNames.Canonical(folder);
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
            // sh, seq and a terminal's own Ctrl+C are Unix's.
            return;
        }
        string folder = Directory.CreateTempSubdirectory("passenger-terminal-").FullName;
        MainWindow? window = null;
        try
        {
            // Numbered lines of a hundred bytes, made as they are read: 500 MB of them, many times the 7 to 35 MB the window
            // takes before Ctrl+C, yet an end that a terminal reading without waiting comes to.
            string lines = $"seq -f 'line %07.0f {new string('x', 86)}'";
            (window, TerminalSession shell) = Open(folder, "/bin/sh");
            // The lines' glyphs drawn once first, as in a terminal already in use: the first frame of them lays them out,
            // which on a slow machine holds the window for a second whatever the terminal does.
            window.KeyTextInput($"{lines} 0 2");
            Ui.Press(window, "ENTER");
            Ui.WaitFor(() => shell.Screen.ScreenText().Contains("line 0000002", StringComparison.Ordinal), "the first lines on the screen");
            Screenshot.Pump(TimeSpan.FromMilliseconds(100));
            window.KeyTextInput("clear");
            Ui.Press(window, "ENTER");
            Ui.WaitFor(() => !shell.Screen.ScreenText().Contains("xxxxxxxx", StringComparison.Ordinal), "the screen cleared");
            window.KeyTextInput($"{lines} 0 4999999");
            using (InputWait waits = new())
            {
                // Pressed and waited for without settling, which waits for all the IDE's background work, seq pouring on.
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
                Stopwatch starting = Stopwatch.StartNew();
                while (!shell.Screen.ScreenText().Contains("xxxxxxxx", StringComparison.Ordinal))
                {
                    Assert.True(starting.Elapsed < TimeSpan.FromSeconds(90), $"seq's lines never came onto the screen:\n{shell.Screen.ScreenText()}");
                    Screenshot.Pump(TimeSpan.FromMilliseconds(20));
                }
                // A second more of the flood, the window running as it does on a desktop, and on a machine slow to draw as
                // long as it takes the window to answer enough looks to say how long a key waits.
                Stopwatch flooding = Stopwatch.StartNew();
                while (flooding.Elapsed < TimeSpan.FromSeconds(1) || (waits.Answered < 20 && flooding.Elapsed < TimeSpan.FromSeconds(20)))
                {
                    Screenshot.Pump(TimeSpan.FromMilliseconds(100));
                }
                waits.Stop();
                Assert.True(waits.Answered >= 20,
                    $"the window answered {waits.Answered} of {waits.Posted} looks at its thread in {flooding.Elapsed.TotalSeconds:F1} s of seq's output");
                Assert.True(waits.Longest < TimeSpan.FromMilliseconds(250),
                    $"a key waited {waits.Longest.TotalMilliseconds:F0} ms for the window while seq's output poured in");
            }

            // The window falls behind for a moment, as it would drawing a heavy frame: seq waits for it, its output not read
            // into memory meanwhile, so Ctrl+C finds it running and only what the terminal had already read comes after.
            long atCtrlC = -1;
            window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
            {
                if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control)
                {
                    atCtrlC = LastLine(shell);
                }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
            Thread.Sleep(2000);
            Ui.Press(window, "control C");
            window.KeyTextInput("echo after-$((6*7))");
            Ui.Press(window, "ENTER");
            Ui.WaitFor(() => shell.Screen.ScreenText().Contains("after-42", StringComparison.Ordinal),
                $"the shell's answer after Ctrl+C:\n{shell.Screen.ScreenText()}", TimeSpan.FromSeconds(30));
            long last = LastLine(shell);
            Assert.True(last < 4_999_999,
                $"seq ran to the end of its 500 MB, line {atCtrlC} on the screen when Ctrl+C was pressed: Ctrl+C never stopped it");
            Assert.True(last - atCtrlC < 40_000,
                $"{(last - atCtrlC) / 10_000.0:F1} MB of seq's output came after Ctrl+C: it was read into memory while the window was behind");
        }
        finally
        {
            // seq's lines outlast a test failing part-way, and end with the window's shells.
            window?.Close();
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
            WaitForScreen(window, shell, s => s.CursorRow > 0 && s.Lines[s.CursorRow - 1].Text().Contains("sleep 30", StringComparison.Ordinal), "sleep to start");

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
    public void The_shell_echoes_what_is_typed_while_every_thread_of_the_pool_is_busy()
    {
        if (OperatingSystem.IsWindows())
        {
            // sh is Unix's.
            return;
        }
        string folder = Directory.CreateTempSubdirectory("passenger-terminal-").FullName;
        using ManualResetEventSlim release = new();
        try
        {
            (MainWindow window, TerminalSession shell) = Open(folder, "/bin/sh");
            // Every thread of the pool held, as a build, a debugger or tests waiting on processes hold them, and more waiting.
            ThreadPool.GetMinThreads(out int threads, out _);
            for (int i = 0; i < threads * 4; i++)
            {
                ThreadPool.UnsafeQueueUserWorkItem(_ => release.Wait(TimeSpan.FromSeconds(60)), null);
            }
            Thread.Sleep(200);
            Stopwatch typing = Stopwatch.StartNew();
            window.KeyTextInput("echo typed");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            // Frames pumped by hand: settling waits for background work, which the busy pool would hold.
            while (!Screen(shell).Contains("\ntyped", StringComparison.Ordinal) && typing.Elapsed < TimeSpan.FromSeconds(30))
            {
                Screenshot.Pump(TimeSpan.FromMilliseconds(20));
            }
            TimeSpan answered = typing.Elapsed;
            release.Set();
            Assert.True(answered < TimeSpan.FromSeconds(5),
                $"with the pool's {threads} threads busy, the shell's answer to a command typed took {answered.TotalSeconds:F1} s:\n{Screen(shell)}");
            window.Close();
        }
        finally
        {
            release.Set();
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

    [AvaloniaFact]
    public void A_decomposed_accent_shows_on_its_letter_in_one_cell_and_copies_with_it()
    {
        if (OperatingSystem.IsWindows())
        {
            // sh's printf writes the bytes.
            return;
        }
        string folder = Directory.CreateTempSubdirectory("passenger-terminal-").FullName;
        try
        {
            (MainWindow window, TerminalSession shell) = Open(folder, "/bin/sh");
            // Résumé.pdf as macOS's file system spells it, each é an e and a combining acute (CC 81), as ls prints it.
            string name = "Re\u0301sume\u0301.pdf";
            Run(window, @"printf 'Re\314\201sume\314\201.pdf\n'");
            long line = WaitForLine(shell, text => text.StartsWith('R') && text.EndsWith(".pdf", StringComparison.Ordinal), "the name printed");
            TerminalLine row = shell.Screen.LineAt(line);
            Assert.True(row.Text() == name, $"Résumé.pdf printed as \"{row.Text()}\": its accents were dropped");
            Assert.True(row.TextOf(1) == "e\u0301" && row.Cells[2].Rune == 's' && row.Cells[9].Rune == 'f',
                $"Résumé.pdf took other columns than its ten letters, so text after an accent drifts from where the shell counts it: [{string.Join("|", Enumerable.Range(0, 12).Select(row.TextOf))}]");

            // Dragged over and copied, it is what was printed.
            Drag(window, line, 0, line, 9);
            Ui.Press(window, CopyKey);
            Assert.Equal(name, Ui.Clipboard(window));
            window.Close();
        }
        finally
        {
            TestFolders.Delete(folder);
        }
    }

    [AvaloniaFact]
    public void An_emoji_takes_the_two_columns_the_shell_counts_so_the_text_after_it_stays_in_place()
    {
        if (OperatingSystem.IsWindows())
        {
            // sh's printf writes the bytes.
            return;
        }
        string folder = Directory.CreateTempSubdirectory("passenger-terminal-").FullName;
        try
        {
            (MainWindow window, TerminalSession shell) = Open(folder, "/bin/sh");
            // A rocket (U+1F680), then a check mark (U+2705) and a lightning bolt (U+26A1), each two columns in zsh's count.
            Run(window, @"printf '\360\237\232\200x\n\342\234\205\342\232\241y\n'");
            long rocket = WaitForLine(shell, "\U0001F680x", "the rocket printed");
            Assert.True(shell.Screen.LineAt(rocket).Cells[2].Rune == 'x',
                $"x printed after a rocket stands in column {Array.FindIndex(shell.Screen.LineAt(rocket).Cells, c => c.Rune == 'x')}, not 2: the rocket overlaps it");
            TerminalLine marks = shell.Screen.LineAt(rocket + 1);
            Assert.True(marks.Text() == "\u2705\u26A1y" && marks.Cells[4].Rune == 'y',
                $"y printed after a check mark and a lightning bolt stands in column {Array.FindIndex(marks.Cells, c => c.Rune == 'y')}, not 4");
            window.Close();
        }
        finally
        {
            TestFolders.Delete(folder);
        }
    }

    [AvaloniaFact]
    public void A_long_line_reads_whole_after_the_terminal_narrows_and_widens_again_and_the_prompt_keeps_its_cursor()
    {
        if (OperatingSystem.IsWindows())
        {
            // sh and seq are Unix's.
            return;
        }
        string folder = Directory.CreateTempSubdirectory("passenger-terminal-").FullName;
        try
        {
            (MainWindow window, TerminalSession shell) = Open(folder, "/bin/sh");
            TerminalScreen screen = shell.Screen;
            int wide = screen.Columns;
            Assert.True(wide >= 100, $"the terminal has {wide} columns, too few for a line of 100");
            // The numbers 10 to 59 in a row: a line of 100 characters.
            string hundred = string.Concat(Enumerable.Range(10, 50));
            Run(window, "printf '%s' $(seq 10 59); echo");
            WaitForLine(shell, hundred, "the line of 100 printed");
            window.KeyTextInput("echo kept");
            Ui.WaitFor(() => screen.Lines[screen.CursorRow].Text().EndsWith("echo kept", StringComparison.Ordinal), "the command typed");
            string prompt = screen.Lines[screen.CursorRow].Text();

            // Moved to the right side, as a drag of its stripe button moves it, the terminal is a side panel's width.
            DockHost dock = window.DockHost;
            dock.Move(dock["terminal"]!, ToolAnchor.RightTop);
            Ui.WaitFor(() => screen.Columns < wide, "the terminal to narrow on the right side");
            Assert.True(screen.Columns < 100, $"on the right side the terminal has {screen.Columns} columns, room for the whole line");
            Assert.True(Lines(screen).Contains(hundred), $"at {screen.Columns} columns the line of 100 no longer reads whole, its rows joined:\n{Screen(shell)}");

            dock.Move(dock["terminal"]!, ToolAnchor.BottomLeft);
            Ui.WaitFor(() => screen.Columns == wide, "the terminal to widen back at the bottom");
            Ui.Settle();
            Assert.True(Enumerable.Range(0, screen.Rows).Any(r => screen.Lines[r].Text() == hundred),
                $"widened again to {wide} columns, the line of 100 is not one row of the screen: the narrower width cut it\n{Screen(shell)}");
            Assert.True(screen.Lines[screen.CursorRow].Text() == prompt && screen.CursorColumn == prompt.Length,
                $"the cursor is at row {screen.CursorRow}, column {screen.CursorColumn}, not after the command typed ({prompt}):\n{Screen(shell)}");
            Ui.Press(window, "ENTER");
            WaitForLine(shell, "kept", "the command typed before the resizes to run");
            window.Close();
        }
        finally
        {
            TestFolders.Delete(folder);
        }
    }

    [AvaloniaFact]
    public void Scrolled_back_the_terminal_holds_its_place_as_output_arrives()
    {
        if (OperatingSystem.IsWindows())
        {
            // sh and seq are Unix's.
            return;
        }
        string folder = Directory.CreateTempSubdirectory("passenger-terminal-").FullName;
        try
        {
            (MainWindow window, TerminalSession shell) = Open(folder, "/bin/sh");
            TerminalView view = window.TerminalPanel.View;
            Run(window, "seq 1 300");
            WaitForLine(shell, "300", "seq's lines");
            // Output that comes once the person is reading back: the loop waits for a file the test makes.
            Run(window, "while [ ! -e go ]; do sleep 0.05; done; seq 1001 1100");
            Ui.WaitFor(() => shell.Screen.Lines[shell.Screen.CursorRow].Text().Length == 0, "the loop to start waiting");

            Point middle = view.TranslatePoint(new Point(view.Bounds.Width / 2, view.Bounds.Height / 2), window)!.Value;
            for (int notch = 0; notch < 7; notch++)
            {
                window.MouseWheel(middle, new Vector(0, 1));
            }
            Ui.Settle();
            Assert.True(view.ScrolledBack == 21, $"seven notches of the wheel scrolled {view.ScrolledBack} lines back, not 21");
            string top = shell.Screen.LineAt(view.TopLine).Text();

            File.WriteAllText(Path.Combine(folder, "go"), string.Empty);
            WaitForLine(shell, "1100", "the hundred lines that came while scrolled back");
            Ui.Settle();
            Assert.True(view.ScrolledBack > 0, "the output scrolled the view back to the bottom while the person read back");
            Assert.True(shell.Screen.LineAt(view.TopLine).Text() == top,
                $"scrolled back to \"{top}\", the view moved to \"{shell.Screen.LineAt(view.TopLine).Text()}\" as 100 lines arrived");
            window.Close();
        }
        finally
        {
            TestFolders.Delete(folder);
        }
    }

    [AvaloniaFact]
    public void A_selection_stays_on_its_text_as_output_streams_past_the_scrollbacks_limit()
    {
        if (OperatingSystem.IsWindows())
        {
            // sh and seq are Unix's.
            return;
        }
        string folder = Directory.CreateTempSubdirectory("passenger-terminal-").FullName;
        try
        {
            (MainWindow window, TerminalSession shell) = Open(folder, "/bin/sh");
            TerminalScreen screen = shell.Screen;
            string text = $"{TerminalScreen.ScrollbackLimit + 100}";
            Run(window, $"seq 1 {text}");
            WaitForLine(shell, text, "seq's lines");
            Assert.True(screen.Scrollback.Count == TerminalScreen.ScrollbackLimit, $"the scrollback holds {screen.Scrollback.Count} lines, short of its limit");
            Run(window, "while [ ! -e go ]; do sleep 0.05; done; seq 20001 20100");
            Ui.WaitFor(() => screen.Lines[screen.CursorRow].Text().Length == 0, "the loop to start waiting");

            // The person drags over seq's last line, and output streams in, each line now dropping the oldest.
            long chosen = WaitForLine(shell, text, "seq's last line");
            Drag(window, chosen, 0, chosen, text.Length - 1);
            Assert.Equal(text, window.TerminalPanel.View.SelectedText);
            File.WriteAllText(Path.Combine(folder, "go"), string.Empty);
            WaitForLine(shell, "20100", "the hundred lines streamed past the limit");
            Ui.Press(window, CopyKey);
            Assert.True(Ui.Clipboard(window) == text,
                $"the selection made on \"{text}\" copied \"{Ui.Clipboard(window)}\" once 100 more lines had dropped the oldest");
            window.Close();
        }
        finally
        {
            TestFolders.Delete(folder);
        }
    }

    /// <summary>Waits for the terminal's screen to show something, failing with what it shows instead, its size, its
    /// cursor and what has the keys.</summary>
    private static void WaitForScreen(MainWindow window, TerminalSession shell, Func<TerminalScreen, bool> shows, string what)
    {
        try
        {
            Ui.WaitFor(() => shows(shell.Screen), what);
        }
        catch (TimeoutException waited)
        {
            TerminalScreen screen = shell.Screen;
            throw new TimeoutException($"{waited.Message}. The terminal, {screen.Columns} by {screen.Rows} with its cursor at row {screen.CursorRow} "
                + $"column {screen.CursorColumn}, {window.FocusManager?.GetFocusedElement()?.GetType().Name ?? "nothing"} having the keys, shows:\n{Screen(shell)}", waited);
        }
    }

    /// <summary>Types a command into the terminal and presses Enter.</summary>
    private static void Run(MainWindow window, string command)
    {
        window.KeyTextInput(command);
        Ui.Press(window, "ENTER");
    }

    /// <summary>Waits for a line of the scrollback or screen to read just so, and gives its number.</summary>
    private static long WaitForLine(TerminalSession shell, string text, string what) => WaitForLine(shell, line => line == text, what);

    /// <summary>Waits for a line of the scrollback or screen whose text passes a test, and gives the newest one's number.</summary>
    private static long WaitForLine(TerminalSession shell, Func<string, bool> test, string what)
    {
        long found = -1;
        Ui.WaitFor(() =>
        {
            TerminalScreen screen = shell.Screen;
            for (long line = screen.ScreenTop + screen.Rows - 1; line >= screen.FirstLine; line--)
            {
                if (test(screen.LineAt(line).Text()))
                {
                    found = line;
                    return true;
                }
            }
            return false;
        }, what, TimeSpan.FromSeconds(30));
        return found;
    }

    /// <summary>The scrollback's and screen's text, rows joined where the text wrapped, as a copy of them reads.</summary>
    private static List<string> Lines(TerminalScreen screen)
    {
        List<string> lines = [];
        System.Text.StringBuilder line = new();
        for (long at = screen.FirstLine; at < screen.ScreenTop + screen.Rows; at++)
        {
            TerminalLine row = screen.LineAt(at);
            line.Append(row.Text());
            if (!row.Wrapped)
            {
                lines.Add(line.ToString());
                line.Clear();
            }
        }
        return lines;
    }

    /// <summary>A drag with the mouse from one cell to another, as a person chooses text.</summary>
    private static void Drag(MainWindow window, long fromLine, int fromColumn, long toLine, int toColumn)
    {
        TerminalView view = window.TerminalPanel.View;
        Point from = view.TranslatePoint(view.PointOf(fromLine, fromColumn), window)!.Value;
        Point to = view.TranslatePoint(view.PointOf(toLine, toColumn), window)!.Value;
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);
        Ui.Settle();
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

    /// <summary>The terminal's Copy: Cmd+C on the Mac, Ctrl+Shift+C elsewhere.</summary>
    private static string CopyKey => OperatingSystem.IsMacOS() ? "meta C" : "control shift C";

    private static void Copy(TopLevel window, string text)
    {
        Task copied = window.Clipboard!.SetTextAsync(text);
        while (!copied.IsCompleted)
        {
            Ui.Settle();
        }
    }

    /// <summary>The number of the last of seq's lines in the terminal, its scrollback included; -1 before the first.</summary>
    private static long LastLine(TerminalSession session)
    {
        long last = -1;
        foreach (string line in Screen(session).Split('\n'))
        {
            string[] words = line.Split(' ', 3);
            if (words.Length == 3 && words[0] == "line" && long.TryParse(words[1], NumberStyles.None, CultureInfo.InvariantCulture, out long number))
            {
                last = Math.Max(last, number);
            }
        }
        return last;
    }

    private static string Screen(TerminalSession session) =>
        string.Join('\n', session.Screen.Scrollback.Select(l => l.Text()).Concat(session.Screen.Lines.Select(l => l.Text())));
}
