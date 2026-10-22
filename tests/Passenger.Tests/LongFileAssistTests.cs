using System.Text;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>The language server's help in a file of 20,000 lines costs a key what the key changed, never the file: its
/// colours, squiggles and completion as you type, held to what the UI thread allocates and what reaches the server. The
/// files are the project's from the start, so the class's windows share one warm server, which compiles them once
/// (<see cref="SharedProject"/>); each test still opens its own window on them as written. Allocations counted, so run
/// alone.</summary>
[Collection(Alone.Name)]
public sealed class LongFileAssistTests : IClassFixture<LongFileAssistTests.Project>
{
    private readonly Project project;

    public LongFileAssistTests(Project project)
    {
        this.project = project;
        project.Reset();
    }

    /// <summary>Completion after a dot reads the file's syntax (the live templates that fit there, the postfix templates
    /// for what is before the dot), which in a file of 20,000 lines is the highlighter's tree carried over the edit, never
    /// the file parsed again on the UI thread. What the window allocates is the count, not a time a slow machine would
    /// stretch: the frame that opens the list allocates less than one parse of the file does.</summary>
    [AvaloniaFact]
    public void Typing_a_dot_in_a_file_of_twenty_thousand_lines_opens_completion_without_parsing_the_file_again()
    {
        MainWindow window = project.Open("Dotted.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        Ui.PlaceAfter(editor, "        Console");
        editor.Focus();
        Ui.Settle();

        long before = GC.GetAllocatedBytesForCurrentThread();
        _ = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(Project.Dotted).GetRoot();
        long parse = GC.GetAllocatedBytesForCurrentThread() - before;

        // The list opens in the frame that has the server's answer, where the syntax at the dot is read.
        window.KeyTextInput(".");
        long opening = 0;
        DateTime limit = DateTime.UtcNow + TimeSpan.FromSeconds(60).Scaled();
        while (!window.Assist.IsCompletionOpen && DateTime.UtcNow < limit)
        {
            before = GC.GetAllocatedBytesForCurrentThread();
            Ui.Hold(TimeSpan.FromMilliseconds(10), "one frame, whose allocations are measured");
            opening = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Assert.True(window.Assist.IsCompletionOpen, "Completion never opened after the dot");
        Assert.Contains("WriteLine", window.Assist.CompletionLabels);
        Assert.True(opening < parse,
            $"The frame that opened completion allocated {opening / 1024} KB on the UI thread, more than the {parse / 1024} KB of one parse of the file: it parsed the file again");
        window.Close();
    }

    /// <summary>The server's colours for a file of 20,000 lines are hundreds of thousands of tokens, decoded off the UI
    /// thread: no frame while they land allocates a quarter of what one decoded copy of them takes, nor a key half of it
    /// (a key's drawing costs the same in a short file). Through the keys typed before they land, and after, a local keeps
    /// its colour on its word, on screen and off it.</summary>
    [AvaloniaFact]
    public void Semantic_colours_in_a_file_of_twenty_thousand_lines_land_without_decoding_it_on_the_UI_thread_and_stay_on_their_words()
    {
        string source = Project.Long;
        MainWindow window = project.Open("Long.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        OpenDocument document = window.EditorArea.Active!;
        TokenKind? KindAt(int offset) => document.Highlighter?.GetSpans(document.Text.Snapshot, offset, 1)
            .Where(s => s.Start == offset).Select(s => (TokenKind?)s.Kind).FirstOrDefault();
        // A use of a local on screen, below where the keys go, and one at the end of the file.
        int near = source.IndexOf("value0 * 3", StringComparison.Ordinal);
        int far = source.LastIndexOf("value98 * 3", StringComparison.Ordinal);
        Ui.WaitFor(() => KindAt(near) == TokenKind.Local && KindAt(far) == TokenKind.Local, "the server's colours on the locals");

        Passenger.App.CSharp.CSharpDocument csharp = window.CSharpService!.For(document)!;
        Task<(Passenger.Lsp.SemanticTokens? Tokens, int Version)> asking = csharp.Lsp!.SemanticTokensAsync();
        Ui.WaitFor(() => asking.IsCompleted, "the whole file's tokens");
        long decoded = asking.Result.Tokens!.Data.Length / 5 * System.Runtime.CompilerServices.Unsafe.SizeOf<SyntaxSpan>();
        Assert.True(decoded > 1_000_000, $"The file's tokens decode to only {decoded / 1024} KB");

        // Keys that open no completion, before the first statement's end: everything after them moves.
        int typing = source.IndexOf("seed + 0;", StringComparison.Ordinal) + "seed + ".Length;
        editor.Selections = SelectionSet.Single(typing);
        editor.Focus();
        Ui.Settle();
        long keys = 0;
        const string Digits = "123";
        for (int i = 0; i < Digits.Length; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            window.KeyTextInput(Digits[i].ToString());
            Ui.Hold(TimeSpan.FromMilliseconds(10), "a frame after the key, whose allocations are measured");
            keys = Math.Max(keys, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.True(KindAt(near + i + 1) == TokenKind.Local, $"After {i + 1} keys the local's use below them lost its colour: {KindAt(near + i + 1)}");
        }

        // A statement put in at once, whose use of the local only the server colours as one.
        editor.Selections = SelectionSet.Single(document.Text.Snapshot.ToString().IndexOf("0;", typing, StringComparison.Ordinal) + 2);
        const string Statement = " int fresh = value0;";
        window.KeyTextInput(Statement);
        int fresh = editor.Selections.Primary.Caret - "value0;".Length;
        int moved = Digits.Length + Statement.Length;
        long landing = 0;
        DateTime limit = DateTime.UtcNow + TimeSpan.FromSeconds(90).Scaled();
        while ((KindAt(fresh) != TokenKind.Local || BackgroundWork.Pending > 0) && DateTime.UtcNow < limit)
        {
            Assert.True(KindAt(near + moved) == TokenKind.Local, $"While the server's colours were out the local's use below lost its colour: {KindAt(near + moved)}");
            long before = GC.GetAllocatedBytesForCurrentThread();
            Ui.Hold(TimeSpan.FromMilliseconds(10), "one frame, whose allocations are measured");
            landing = Math.Max(landing, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Assert.True(KindAt(fresh) == TokenKind.Local, "The local used in the statement put in never took the server's colour");
        Assert.True(KindAt(near + moved) == TokenKind.Local, "The local's use below the keys lost its colour once the server's landed");
        Assert.True(landing < decoded / 4,
            $"A frame while the server's colours landed allocated {landing / 1024} KB on the UI thread, a quarter or more of the {decoded / 1024} KB the file's tokens decode to: they were decoded there");
        Assert.True(keys < decoded / 2, $"A key allocated {keys / 1024} KB on the UI thread, half or more of the {decoded / 1024} KB the file's tokens decode to");

        // Off screen, the whole file's colours are carried through keys until they come again.
        for (int i = 0; i < Digits.Length; i++)
        {
            window.KeyTextInput(Digits[i].ToString());
            Ui.Hold(TimeSpan.FromMilliseconds(10), "a frame after the key");
            Assert.True(KindAt(far + moved + i + 1) == TokenKind.Local, $"After {i + 1} more keys the local's use at the end of the file lost its colour: {KindAt(far + moved + i + 1)}");
        }
        Ui.Settle();
        Assert.Equal(TokenKind.Local, KindAt(far + moved + Digits.Length));
        Assert.Equal(TokenKind.Local, KindAt(fresh));
        window.Close();
    }

    /// <summary>Squiggles in a file of 20,000 lines stay on their words as lines go in above them, and so do the diagnostics
    /// a fix asked for at the caret is given, which are on the server's lines. A key finds those lines from what it changed,
    /// reading a few characters rather than the whole file (twice, once with diagnostics to move and once for the change
    /// sent to the server).</summary>
    [AvaloniaFact]
    public void Squiggles_in_a_file_of_twenty_thousand_lines_stay_on_their_words_as_lines_go_in_above_and_a_key_reads_only_what_it_changed()
    {
        string source = Project.Long;
        MainWindow window = project.Open("Long.cs");
        CodeEditor editor = window.EditorArea.CodeEditor;
        OpenDocument document = window.EditorArea.Active!;
        Passenger.App.CSharp.CSharpDocument csharp = window.CSharpService!.For(document)!;
        string[] literals = ["25", "75", "125", "175"];
        Ui.WaitFor(() => document.Markers.Count(m => m.Kind == MarkerKind.Error) == literals.Length, "the four errors' squiggles");
        void OnTheirWords(string when)
        {
            TextSnapshot text = document.Text.Snapshot;
            Marker[] errors = [.. document.Markers.Where(m => m.Kind == MarkerKind.Error).OrderBy(m => m.Range.Start)];
            Assert.True(errors.Select(m => text.GetText(m.Range)).SequenceEqual(literals),
                $"{when} the squiggles are under {string.Join(", ", errors.Select(m => $"'{text.GetText(m.Range)}'"))}");
            Passenger.Lsp.Diagnostic[] diagnosed = [.. csharp.Diagnostics.Where(d => d.Severity == 1).OrderBy(d => d.Range.Start.Line)];
            Assert.Equal(literals.Length, diagnosed.Length);
            for (int i = 0; i < literals.Length; i++)
            {
                int line = text.GetLineFromOffset(errors[i].Range.Start);
                Assert.Equal($"        string wrong{literals[i]} = {literals[i]};", text.GetLineText(line));
                TextPosition start = new(diagnosed[i].Range.Start.Line, diagnosed[i].Range.Start.Character);
                TextPosition end = new(diagnosed[i].Range.End.Line, diagnosed[i].Range.End.Character);
                Assert.True(start.Line == line && text.GetText(TextRange.FromBounds(text.GetOffset(start), text.GetOffset(end))) == literals[i],
                    $"{when} the diagnostic under '{literals[i]}' on line {line + 1} is at {start.Line + 1}:{start.Column + 1}");
            }
        }
        OnTheirWords("Before any key");

        int top = source.IndexOf("seed + 0;", StringComparison.Ordinal) + "seed + 0;".Length;
        editor.Selections = SelectionSet.Single(top);
        editor.Focus();
        Ui.Settle();
        const int Keys = 5;
        long read = 0;
        for (int i = 0; i < Keys; i++)
        {
            long before = Passenger.Lsp.ServerLines.CharactersRead;
            window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
            window.KeyRelease(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
            Ui.Hold(TimeSpan.FromMilliseconds(10), "a frame after the key");
            read += Passenger.Lsp.ServerLines.CharactersRead - before;
            OnTheirWords($"After {i + 1} lines went in above them");
        }
        Assert.Equal(source.Count(c => c == '\n') + Keys, document.Text.Snapshot.LineCount - 1);
        Assert.True(read / Keys < source.Length / 100,
            $"A key read {read / Keys} characters to find the server's lines, of the file's {source.Length}: it read the whole file again");

        Ui.Settle();
        OnTheirWords("Once the server's diagnostics came again");
        window.Close();
    }

    /// <summary>A file of 20,000 lines is a million numbers of tokens, which the server takes a second to make and the IDE
    /// megabytes to read: while typing goes on with pauses, only the lines on screen are asked for again (Rider's and VS
    /// Code's way), and what only the server colours on screen is coloured from them; once typing stops, the whole file is
    /// asked for once. Counted in the protocol's own trace.</summary>
    [AvaloniaFact]
    public void Typing_with_pauses_in_a_file_of_twenty_thousand_lines_asks_for_the_screens_colours_and_the_whole_files_once_it_stops()
    {
        string source = Project.Long;
        MainWindow? window = null;
        int whole = 0;
        int screens = 0;
        int refreshedSince = 0;
        try
        {
            window = project.Open("Long.cs");
            CodeEditor editor = window.EditorArea.CodeEditor;
            OpenDocument document = window.EditorArea.Active!;
            TokenKind? KindAt(int offset) => document.Highlighter?.GetSpans(document.Text.Snapshot, offset, 1)
                .Where(s => s.Start == offset).Select(s => (TokenKind?)s.Kind).FirstOrDefault();
            int far = source.LastIndexOf("value98 * 3", StringComparison.Ordinal);
            Ui.WaitFor(() => KindAt(far) == TokenKind.Local, "the server's colours on the file's last local");
            Ui.Settle();
            // Every colour request and refresh, as the wire had them: what a failure says, since CI keeps no protocol trace.
            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            System.Collections.Concurrent.ConcurrentQueue<string> wire = new();
            Passenger.Lsp.LanguageServer.Trace = (server, message) =>
            {
                if (System.Text.RegularExpressions.Regex.Match(message, "\"method\":\"([^\"]*semanticTokens[^\"]*)\"") is { Success: true } method)
                {
                    string file = System.Text.RegularExpressions.Regex.Match(message, "\"uri\":\"[^\"]*/([^\"/]+)\"") is { Success: true } uri ? $" {uri.Groups[1].Value}" : "";
                    wire.Enqueue($"{clock.ElapsedMilliseconds} ms, server {server}: {message[..3]} {method.Groups[1].Value}{file}");
                }
                if (message.StartsWith("-->", StringComparison.Ordinal) && message.Contains("\"textDocument/semanticTokens/full\"", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref whole);
                }
                else if (message.StartsWith("-->", StringComparison.Ordinal) && message.Contains("\"textDocument/semanticTokens/range\"", StringComparison.Ordinal))
                {
                    Interlocked.Increment(ref screens);
                }
                else if (message.StartsWith("<--", StringComparison.Ordinal) && message.Contains("\"workspace/semanticTokens/refresh\"", StringComparison.Ordinal)
                    && Volatile.Read(ref whole) > 0)
                {
                    // The server's word that its colours moved, after the file's were asked for: they are rightly asked again.
                    Interlocked.Increment(ref refreshedSince);
                }
            };

            // Statements typed one by one, a pause after each long enough for the screen's colours and short of the idle.
            int at = source.IndexOf("seed + 0;", StringComparison.Ordinal) + "seed + 0;".Length;
            editor.Selections = SelectionSet.Single(at);
            editor.Focus();
            for (int i = 0; i < 4; i++)
            {
                window.KeyTextInput($" int fresh{i} = value0;");
                int used = editor.Selections.Primary.Caret - "value0;".Length;
                Ui.PumpUntil(() => KindAt(used) == TokenKind.Local, $"the screen's colours on the local used in statement {i + 1}");
                Ui.Hold(TimeSpan.FromMilliseconds(300), "a pause in typing, shorter than the idle after which the whole file's colours are asked for");
            }
            Assert.True(screens > 0, "The screen's colours were never asked for as the statements were typed");
            Assert.True(whole == 0, $"The whole file's colours were asked for {whole} times while typing went on with pauses:\n{string.Join('\n', wire)}");

            // Once typing stops, the whole file's, once.
            Ui.PumpUntil(() => Volatile.Read(ref whole) > 0, "the whole file's colours once typing stopped");
            Ui.Settle();
            Ui.Hold(TimeSpan.FromSeconds(3), "past the idle a needless second ask would wait out after the screen's colours, which no settle waits for");
            Ui.Settle();
            int refreshes = Volatile.Read(ref refreshedSince);
            Assert.True(whole <= 1 + refreshes,
                $"Once typing stopped the whole file's colours were asked for {whole} times, with {refreshes} word from the server since that they moved:\n{string.Join('\n', wire)}");
            Assert.Equal(TokenKind.Local, KindAt(far + 4 * " int freshN = value0;".Length));
        }
        finally
        {
            Passenger.Lsp.LanguageServer.Trace = null;
            window?.Close();
        }
    }

    /// <summary>A small console project on disk with two files of 20,000 lines, restored once for every test, and the warm
    /// server they share on it, which loads and compiles the files once.</summary>
    public sealed class Project : SharedProject
    {
        /// <summary>Long.cs: two hundred methods of a hundred lines and more, every fiftieth with an error, a string given a number.</summary>
        public static readonly string Long = TwentyThousandLines(errors: true, "Long", "}\n");

        /// <summary>Dotted.cs: two hundred methods of a hundred lines with no error, and a last one where a dot goes after Console.</summary>
        public static readonly string Dotted = TwentyThousandLines(errors: false, "Dotted", "    public static void Last()\n    {\n        Console\n    }\n}\n");

        public Project()
            : base("long-assist-")
        {
            File.WriteAllText(PathOf("Sample.csproj"),
                TestProjects.Console());
            File.WriteAllText(PathOf("Program.cs"), "class Program\n{\n    static void Main()\n    {\n    }\n}\n");
            File.WriteAllText(PathOf("Long.cs"), Long);
            File.WriteAllText(PathOf("Dotted.cs"), Dotted);
            Prepare();
        }

        /// <summary>Two hundred methods of a hundred lines and more: 20,000 lines, as many as Roslyn parses fresh every time.</summary>
        private static string TwentyThousandLines(bool errors, string name, string end)
        {
            StringBuilder text = new($"using System;\n\nstatic class {name}\n{{\n");
            for (int i = 0; i < 200; i++)
            {
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"    public static int Method{i}(int seed)\n    {{\n        int value0 = seed + {i};\n");
                if (errors && i % 50 == 25)
                {
                    text.Append(System.Globalization.CultureInfo.InvariantCulture, $"        string wrong{i} = {i};\n");
                }
                for (int j = 1; j < 100; j++)
                {
                    text.Append(System.Globalization.CultureInfo.InvariantCulture, $"        int value{j} = value{j - 1} * 3 + \"{i}:{j}\".Length;\n");
                }
                text.Append("        return value99;\n    }\n\n");
            }
            string source = text.Append(end).ToString();
            Assert.True(source.Count(c => c == '\n') > 20_000);
            return source;
        }
    }
}
