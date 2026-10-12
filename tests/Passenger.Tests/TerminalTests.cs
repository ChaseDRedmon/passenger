using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Passenger.App;
using Passenger.Terminal;

namespace Passenger.Tests;

/// <summary>The terminal as programs use it: the screen reads xterm's sequences as xterm does (wrapping, scroll regions,
/// the alternate screen, colours, wide characters, answers to a program's questions, resizing into the scrollback),
/// and the Terminal window runs a real shell in a pseudo-terminal that takes typed commands and keys ahead of the IDE.</summary>
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
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string Screen(TerminalSession session) =>
        string.Join('\n', session.Screen.Scrollback.Select(l => l.Text()).Concat(session.Screen.Lines.Select(l => l.Text())));
}
