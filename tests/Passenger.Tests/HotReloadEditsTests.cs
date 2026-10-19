using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Debugging;
using Passenger.Core;
using Passenger.Editor;
using Passenger.Text;

namespace Passenger.Tests;

/// <summary>Which everyday edits Hot Reload applies to a program debugged on .NET 10, each made in the editor while the
/// program runs (or, for a method on the stack, while it is paused in it) and applied with Apply Hot Reload Changes: the
/// program then prints what the edited code prints, or, for an edit the runtime cannot take, Hot Reload names it at its line
/// and applies nothing. Debugged, so run alone.</summary>
[Collection(Alone.Name)]
public sealed class HotReloadEditsTests : IClassFixture<RestoredFolders>, IDisposable
{
    private readonly string folder;

    public HotReloadEditsTests(RestoredFolders folders)
    {
        folder = folders.Take("hotreload-edits-", made =>
        {
            File.WriteAllText(Path.Combine(made, "Ticks.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n    <Nullable>enable</Nullable>\n  </PropertyGroup>\n</Project>\n");
            File.WriteAllText(Path.Combine(made, "Program.cs"), "for (int i = 1; ; i++)\n{\n    Demo.Tick(i);\n    Thread.Sleep(100);\n}\n");
            File.WriteAllText(Path.Combine(made, "Demo.cs"), Demo);
            return made;
        });
    }

    public void Dispose() => TestFolders.Delete(folder);

    /// <summary>A static class, a class with an instance, a lambda, an async method, a record and a generic method, each
    /// printing a line every tick.</summary>
    private const string Demo = """
        static class Demo
        {
            static readonly Counter counter = new();

            public static void Tick(int i)
            {
                Console.WriteLine($"tick {i}");
                Console.WriteLine($"counter {counter.Next()}");
                Func<int, string> format = x => $"lambda {x}";
                Console.WriteLine(format(i));
                Console.WriteLine(FetchAsync(i).GetAwaiter().GetResult());
                Console.WriteLine(new Pet("Rex").Describe());
                Console.WriteLine(Echo($"generic {i}"));
            }

            static async Task<string> FetchAsync(int i)
            {
                await Task.Yield();
                return $"async {i}";
            }

            static T Echo<T>(T value) => value;
        }

        sealed class Counter
        {
            public int Next() => 1;
        }

        record Pet(string Name)
        {
            public string Describe() => $"pet {Name}";
        }

        """;

    /// <summary>Each edit: the text replaced and what replaces it, in order, and what the program prints once it applies;
    /// null for an edit the runtime cannot take while the program runs.</summary>
    public static readonly Dictionary<string, (IReadOnlyList<(string Old, string New)> Edits, string? Prints)> Edits = new()
    {
        ["a method body"] = ([("$\"tick {i}\"", "$\"tick edited {i}\"")], "tick edited"),
        ["a method added"] = ([("$\"tick {i}\"", "Word(i)"), ("    public static void Tick(int i)", "    static string Word(int i) => $\"added method {i}\";\n\n    public static void Tick(int i)")], "added method"),
        ["a static field added"] = ([("    static readonly Counter counter = new();", "    static readonly Counter counter = new();\n    static int ticks;"), ("$\"tick {i}\"", "$\"added static field {++ticks}\"")], "added static field 1"),
        ["an instance field added"] = ([("    public int Next() => 1;", "    int calls;\n\n    public int Next() => 100 + ++calls;")], "counter 101"),
        ["a class added"] = ([("sealed class Counter", "static class Added\n{\n    public static string Say(int i) => $\"added class {i}\";\n}\n\nsealed class Counter"), ("$\"tick {i}\"", "Added.Say(i)")], "added class"),
        ["a lambda's body"] = ([("x => $\"lambda {x}\"", "x => $\"lambda edited {x}\"")], "lambda edited"),
        ["a lambda capturing a new variable"] = ([("x => $\"lambda {x}\"", "x => $\"lambda captured {x} {i}\"")], "lambda captured"),
        ["an async method's body"] = ([("$\"async {i}\"", "$\"async edited {i}\"")], "async edited"),
        ["an await added to an async method"] = ([("        return $\"async {i}\";", "        await Task.Yield();\n        return $\"async awaited twice {i}\";")], "async awaited twice"),
        ["a record's method"] = ([("$\"pet {Name}\"", "$\"pet edited {Name}\"")], "pet edited"),
        ["a property added to a record"] = ([("    public string Describe() => $\"pet {Name}\";", "    public int Age { get; init; } = 3;\n\n    public string Describe() => $\"pet {Name} aged {Age}\";")], "aged 3"),
        ["a generic method's body"] = ([("static T Echo<T>(T value) => value;", "static T Echo<T>(T value) { Console.WriteLine(\"generic edited\"); return value; }")], "generic edited"),
        ["a method's signature"] = ([("$\"tick {i}\"", "Word(i, \"signature\")"), ("    public static void Tick(int i)", "    static string Word(int i, string what) => $\"{what} {i}\";\n\n    public static void Tick(int i)")], "signature"),
        ["a parameter added with its callers"] = ([("    public int Next() => 1;", "    public int Next(int by) => by;"), ("counter.Next()", "counter.Next(7)")], "counter 7"),
        // Program.cs still calls Tick(int), whose body the update would leave throwing: only a restart runs the edit.
        ["a parameter added with a default its callers leave out"] = ([("    public static void Tick(int i)", "    public static void Tick(int i, int times = 1)")], null),
        ["a field's type"] = ([("    static readonly Counter counter = new();", "    static readonly Counter counter = new();\n    static int ticks;")], null),
    };

    public static TheoryData<string> Kinds => [.. Edits.Keys];

    [AvaloniaTheory]
    [MemberData(nameof(Kinds))]
    public void An_edit_to_a_running_program_applies_as_the_runtime_allows(string kind)
    {
        (IReadOnlyList<(string Old, string New)> edits, string? prints) = Edits[kind];
        if (kind == "a field's type")
        {
            // The field exists in the build; its type is what the edit changes.
            File.WriteAllText(DemoPath, Demo.Replace("    static readonly Counter counter = new();", "    static readonly Counter counter = new();\n    static int ticks;", StringComparison.Ordinal));
            edits = [("    static int ticks;", "    static long ticks;")];
        }
        MainWindow window = Open();
        try
        {
            Ui.Run(window, AppActions.Debug);
            Ui.WaitFor(() => Output(window).Contains("generic 2", StringComparison.Ordinal), $"the program to tick ({Output(window)})");
            window.OpenFile(DemoPath);
            foreach ((string old, string typed) in edits)
            {
                Replace(window, old, typed);
            }
            int before = Output(window).Length;
            HotReloadOutcome outcome = Ui.Await(window.Debugger.ApplyChangesAsync(), "Hot Reload to apply the edit", TimeSpan.FromMinutes(2));
            TestContext.Current.TestOutputHelper?.WriteLine($"{kind}: {outcome.Result}: {outcome.Said}");
            if (prints is null)
            {
                Assert.True(outcome.Result == HotReloadResult.RestartNeeded, $"Changing {kind} should need a restart: {outcome.Said}");
                Assert.Contains(outcome.Problems, p => p.Path == DemoPath && p.Code?.StartsWith("ENC", StringComparison.Ordinal) == true);
                // Nothing was applied: the program goes on ticking as built, never into a body the update left throwing.
                Ui.WaitFor(() => Output(window)[before..].Contains("generic", StringComparison.Ordinal), $"the program to tick on as it was ({Output(window)[before..]})");
                Assert.True(window.Debugger.Session?.State == DebugState.Running, $"The program stopped after an edit that was not applied: {Output(window)[before..]}");
                return;
            }
            Assert.True(outcome.Result == HotReloadResult.Applied, $"Changing {kind} should apply: {outcome.Said}");
            Ui.WaitFor(() => Output(window)[before..].Contains(prints, StringComparison.Ordinal), $"the program to print '{prints}' ({Output(window)[before..]})");
        }
        finally
        {
            End(window);
        }
    }

    [AvaloniaFact]
    public void An_edit_to_a_method_paused_on_the_stack_runs_from_its_statement()
    {
        MainWindow window = Open();
        try
        {
            window.OpenFile(DemoPath);
            window.EditorArea.CodeEditor.GoTo(new TextPosition(6, 8));
            Ui.Run(window, AppActions.ToggleBreakpoint);
            Ui.Run(window, AppActions.Debug);
            Ui.WaitFor(() => window.Debugger.Session?.State == DebugState.Paused && window.Debugger.Stop is not null, "the program to stop in Tick");
            Ui.Settle();
            Replace(window, "$\"tick {i}\"", "$\"tick edited {i}\"");
            HotReloadOutcome outcome = Ui.Await(window.Debugger.ApplyChangesAsync(), "Hot Reload to apply the edit", TimeSpan.FromMinutes(2));
            TestContext.Current.TestOutputHelper?.WriteLine($"a method on the stack: {outcome.Result}: {outcome.Said}");
            Assert.True(outcome.Result == HotReloadResult.Applied, $"Editing the method the program is paused in should apply: {outcome.Said}");
            Ui.Run(window, AppActions.ToggleBreakpoint);
            Ui.Run(window, AppActions.Resume);
            Ui.WaitFor(() => Output(window).Contains("tick edited 2", StringComparison.Ordinal), $"the program to tick with the edit ({Output(window)})");
            Assert.Contains("tick edited 1", Output(window), StringComparison.Ordinal);
        }
        finally
        {
            End(window);
        }
    }

    private string DemoPath => Path.Combine(PathNames.Canonical(folder), "Demo.cs");

    private static string Output(MainWindow window) => window.Debugger.Output().ToString();

    private static void Replace(MainWindow window, string old, string typed)
    {
        CodeEditor editor = window.EditorArea.CodeEditor;
        int at = editor.Document.Snapshot.ToString().IndexOf(old, StringComparison.Ordinal);
        Assert.True(at >= 0, $"The file should have '{old}' to replace: {editor.Document.Snapshot}");
        editor.Focus();
        editor.Select(new TextRange(at, old.Length));
        window.KeyTextInput(typed);
        Ui.Settle();
        // A line break typed is indented as the editor indents one, so the text is compared without its spaces.
        Assert.Contains(Squeezed(typed), Squeezed(editor.Document.Snapshot.ToString()), StringComparison.Ordinal);
    }

    private static string Squeezed(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c)));

    private MainWindow Open()
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        Ui.WaitFor(() => window.CSharpService is { IsReady: true }, $"the language server to load the project ({window.CSharpService?.State})", TimeSpan.FromMinutes(3));
        return window;
    }

    private static void End(MainWindow window)
    {
        if (window.Debugger.IsDebugging)
        {
            Ui.Run(window, AppActions.Stop);
            Ui.WaitFor(() => !window.Debugger.IsDebugging, "the session to end");
        }
        window.Close();
    }
}
