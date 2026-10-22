using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Passenger.App;
using Passenger.App.Mcp;
using Passenger.App.Settings;
using Passenger.Core;
using Passenger.Editor;

namespace Passenger.Tests;

/// <summary>Passenger as an agent's IDE with no window (<c>--mcp</c>), over the Model Context Protocol with the SDK's own
/// client: the tools an agent reads as its session starts, and a project worked through them (its code, tests, packages,
/// actions, publish, Git and the terminal, and debugging it). <see cref="McpRig"/>'s other classes join a person's window
/// (McpWindowTests, McpAttachTests) or drive what Passenger runs (McpAvaloniaAppTests).</summary>
[Collection(Alone.Name)]
public sealed class McpTests : McpRig, IClassFixture<RestoredFolders>
{
    public McpTests(RestoredFolders folders)
        : base(folders)
    {
    }

    [Fact]
    public async Task An_agent_with_a_windowless_Passenger_finds_code_reads_its_warnings_and_debugs_it()
    {
        // A name that has the one asked for in it, in a file the server reads before Helper.cs.
        Write("Aaa.cs", "static class Aaa\n{\n    public static int TwiceMore(int value) => value;\n}\n");
        await using McpClient client = await Start(["--mcp", Folder]);
        IList<McpClientTool> tools = await Exchange("Listing the tools", TimeSpan.FromMinutes(1), cancel => client.ListToolsAsync(cancellationToken: cancel).AsTask());
        Assert.Contains(tools, t => t.Name == "debug_start");
        // An agent reads how its tests will run from the tool: an xUnit v3 project's through the Testing Platform, not VSTest.
        string testsRun = Assert.Single(tools, t => t.Name == "tests_run").Description;
        Assert.True(testsRun.Contains("Testing Platform", StringComparison.Ordinal) && testsRun.Contains("xUnit v3", StringComparison.Ordinal),
            $"tests_run tells an agent its tests run otherwise than they do: {testsRun}");
        // What every session pays to read says each thing once: the app_ tools' element query in app_tree's description alone
        // (it was in nine tools), and no optional parameter's null default or type; the list was 69 KB and is held under 60 KB.
        Assert.Equal(["app_tree"], tools.Where(t => JsonSerializer.Serialize(t.ProtocolTool).Contains("#Panel/Button", StringComparison.Ordinal)).Select(t => t.Name));
        Assert.DoesNotContain(tools, t => SaysNothing(t.ProtocolTool.InputSchema));
        int listed = tools.Sum(t => JsonSerializer.Serialize(t.ProtocolTool, ModelContextProtocol.McpJsonUtilities.DefaultOptions).Length);
        Assert.True(listed < 60_000, $"The tools an agent reads as each session starts take {listed} bytes");

        // The symbol named as asked comes first, so a limit of one finds it rather than one with the name inside its own.
        JsonElement symbol = Assert.Single((await Call(client, "find_symbol", new() { ["query"] = "Twice", ["limit"] = 1 })).EnumerateArray());
        Assert.Equal(("Twice", "Helper.cs", 3), (symbol.GetProperty("name").GetString(), symbol.GetProperty("path").GetString(), symbol.GetProperty("line").GetInt32()));

        // Code in an answer reads as written, its quotes and angle brackets as they are, not as \u0022s an agent pays for.
        string found = Text(await Result(client, "find_in_files", new() { ["text"] = "total {total}" }));
        Assert.True(found.Contains("Console.WriteLine($\\\"total {total}\\\");", StringComparison.Ordinal) && !found.Contains("\\u00", StringComparison.Ordinal),
            $"find_in_files wrote a line of code as: {found}");

        JsonElement warning = Assert.Single((await Call(client, "diagnostics", new() { ["path"] = "Helper.cs" })).EnumerateArray(),
            d => d.GetProperty("code").GetString() == "CS0219");
        Assert.Equal("warning", warning.GetProperty("severity").GetString());
        Assert.Equal(5, warning.GetProperty("line").GetInt32());

        // The breakpoint is the window's own: the stop comes back with where it is and the locals there.
        await Call(client, "breakpoint_set", new() { ["path"] = "Program.cs", ["line"] = 4 });
        JsonElement stop = await Call(client, "debug_start");
        Assert.Equal("paused", stop.GetProperty("state").GetString());
        Assert.StartsWith("Program.cs:4:", stop.GetProperty("location").GetString(), StringComparison.Ordinal);
        Assert.Equal("1", Local(stop, "i"));
        Assert.Equal("0", Local(stop, "total"));
        Assert.Equal("42", (await Call(client, "debug_evaluate", new() { ["expression"] = "Helper.Twice(i) + 40" })).GetProperty("value").GetString());
        // A frame past the stack is named as one, with how deep the stack is, never as a list's index out of its range.
        int depth = stop.GetProperty("frames").GetArrayLength();
        foreach ((string tool, Dictionary<string, object?> arguments) in new (string, Dictionary<string, object?>)[]
        {
            ("debug_evaluate", new() { ["expression"] = "i", ["frame"] = 99 }),
            ("debug_set_value", new() { ["expression"] = "i", ["value"] = "1", ["frame"] = 99 }),
        })
        {
            CallToolResult past = await Result(client, tool, arguments);
            Assert.True(past.IsError == true && Text(past).Contains("There is no frame 99: the stopped thread has ", StringComparison.Ordinal)
                && Text(past).Contains($"{depth} frame", StringComparison.Ordinal),
                $"{tool} in frame 99 of a stack {depth} deep answered: {Text(past)}");
        }

        stop = await Call(client, "debug_continue");
        Assert.Equal("2", Local(stop, "i"));

        // A breakpoint set while debugging is in the program when the answer comes, which says it bound: the agent's
        // next step (a click into an app, which goes no way the debugger sees) cannot come before it.
        JsonElement later = Assert.Single((await Call(client, "breakpoint_set", new() { ["path"] = "Program.cs", ["line"] = 6 })).EnumerateArray(),
            b => b.GetProperty("line").GetInt32() == 6);
        Assert.Equal("bound", later.GetProperty("status").GetString());
        await Call(client, "breakpoint_remove", new() { ["path"] = "Program.cs", ["line"] = 6 });

        // An edit made while it is paused runs once it goes on, as the window's Resume applies one: Twice(2) and Twice(3)
        // add one each.
        await Call(client, "open_file", new() { ["path"] = "Helper.cs", ["line"] = 6, ["column"] = 25 });
        await Call(client, "type", new() { ["text"] = " + 1" });

        // With the breakpoint gone the program runs to its unhandled exception, which is named.
        await Call(client, "breakpoint_remove", new() { ["path"] = "Program.cs", ["line"] = 4 });
        stop = await Call(client, "debug_continue");
        Assert.Equal("exception", stop.GetProperty("reason").GetString());
        Assert.StartsWith("System.InvalidOperationException: the end", stop.GetProperty("exception").GetString(), StringComparison.Ordinal);
        string output = (await Call(client, "debug_output")).GetString()!;
        Assert.True(output.Contains("total 14", StringComparison.Ordinal), $"The edit made while paused did not run once the program went on:\n{output}");

        CallToolResult picture = await Result(client, "screenshot");
        ImageContentBlock image = Assert.IsType<ImageContentBlock>(Assert.Single(picture.Content));
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], image.DecodedData.Slice(0, 4).ToArray());

        await Call(client, "stop");
        Assert.Equal("not debugging", (await Call(client, "debug_state")).GetProperty("state").GetString());

        // What a tool refuses reaches the agent in words that say what to do instead.
        CallToolResult refused = await Result(client, "debug_continue");
        Assert.True(refused.IsError);
        Assert.Contains("debug_start starts it", Assert.IsType<TextContentBlock>(Assert.Single(refused.Content)).Text, StringComparison.Ordinal);

        // A program already running, started by the test as a terminal would start it, in its loop when it says so: listed,
        // attached to and stopped at a breakpoint, then let go to run on in its loop, which the debugger no longer stops.
        ProcessStartInfo start = ChildProcesses.Command(Debugging.Helpers.Debuggees.Apphost("LoopingConsoleApp"), ["sleep"]);
        start.RedirectStandardOutput = true;
        using (Process looping = ChildProcesses.Start(start))
        {
            try
            {
                Assert.Equal("looping", looping.StandardOutput.ReadLine());
                JsonElement program = Assert.Single((await Call(client, "debug_processes")).EnumerateArray(), p => p.GetProperty("processId").GetInt32() == looping.Id);
                Assert.Equal("LoopingConsoleApp", program.GetProperty("program").GetString());
                string source = Debugging.Helpers.Debuggees.Source("LoopingConsoleApp", "Program.cs");
                int line = Debugging.Helpers.Debuggees.Line("LoopingConsoleApp", "turns++;");
                await Call(client, "breakpoint_set", new() { ["path"] = source, ["line"] = line });
                JsonElement attached = await Call(client, "debug_attach", new() { ["processId"] = looping.Id });
                Assert.Equal(("paused", "breakpoint"), (attached.GetProperty("state").GetString(), attached.GetProperty("reason").GetString()));
                Assert.EndsWith($"Program.cs:{line}:5", attached.GetProperty("location").GetString(), StringComparison.Ordinal);
                Assert.True(long.TryParse(Local(attached, "turns"), out long turns) && turns >= 0, $"The stop's locals had no count of turns: {attached}");
                Assert.Equal("Detached from LoopingConsoleApp, which runs on", (await Call(client, "debug_detach")).GetString());
                Assert.Equal("not debugging", (await Call(client, "debug_state")).GetProperty("state").GetString());
                Assert.False(looping.WaitForExit(TimeSpan.FromSeconds(2)), "The program the agent detached from ended");
            }
            finally
            {
                Processes.Kill(looping.Id);
            }
        }

        // A project file's problems are what the editor marks, at once: the C# server never takes the file, so a wait for
        // it to would have run out its two minutes.
        await Call(client, "open_file", new() { ["path"] = "Sum.csproj", ["line"] = 7, ["column"] = 1 });
        await Call(client, "type", new() { ["text"] = "<Import Project=\"Missing.props\" />" });
        Stopwatch asked = Stopwatch.StartNew();
        JsonElement missing = Assert.Single((await Call(client, "diagnostics", new() { ["path"] = "Sum.csproj" })).EnumerateArray());
        Assert.True(asked.Elapsed < TimeSpan.FromSeconds(60).Scaled(), $"The project file's diagnostics took {asked.Elapsed}");
        Assert.Equal(("error", 7), (missing.GetProperty("severity").GetString(), missing.GetProperty("line").GetInt32()));
        Assert.Contains("Missing.props", missing.GetProperty("message").GetString(), StringComparison.Ordinal);
        // Its code help is MSBuild's, from the evaluation before the import that is not there broke it.
        // The hover card's lines end as the system's do (Windows' "\r\n").
        string framework = (await Call(client, "symbol_info", new() { ["path"] = "Sum.csproj", ["line"] = 4, ["column"] = 8 })).GetString()!.ReplaceLineEndings("\n");
        Assert.True(framework.StartsWith("TargetFramework = net10.0\nMSBuild property, set in Sum.csproj, line 4", StringComparison.Ordinal),
            $"symbol_info on a project file's property did not say its value and where it was set: {framework}");
    }

    [Fact]
    public async Task An_agent_that_lists_the_tests_as_it_starts_gets_those_the_test_projects_code_declares()
    {
        // A test project as written, never built or restored: its tests come from its code, which the agent's first look waits for.
        string tests = PathNames.Canonical(TestFolders.Create("mcp-tests-"));
        DeleteAtEnd(tests);
        File.WriteAllText(Path.Combine(tests, "Sums.Tests.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n    <OutputType>Exe</OutputType>\n  </PropertyGroup>\n"
            + "  <ItemGroup>\n    <PackageReference Include=\"xunit.v3\" Version=\"3.2.2\" />\n  </ItemGroup>\n</Project>\n");
        File.WriteAllText(Path.Combine(tests, "SumTests.cs"),
            "using Xunit;\n\nnamespace Sums;\n\npublic class SumTests\n{\n    [Fact]\n    public void Two_and_two_make_four() => Assert.Equal(4, 2 + 2);\n\n"
            + "    [Fact]\n    public void Three_and_three_make_six() => Assert.Equal(6, 3 + 3);\n}\n");
        await using McpClient client = await Start(["--mcp", tests]);
        Assert.Equal(2, (await Call(client, "tests_list")).GetArrayLength());
        // A solution's thousands of tests narrowed as tests_run narrows them, by part of the name, and by the last run's state.
        JsonElement test = Assert.Single((await Call(client, "tests_list", new() { ["filter"] = "two_make_four" })).EnumerateArray());
        Assert.Equal(("Sums.SumTests.Two_and_two_make_four", "notRun", "SumTests.cs:8"),
            (test.GetProperty("test").GetString(), test.GetProperty("state").GetString(), test.GetProperty("location").GetString()));
        Assert.Equal(1, (await Call(client, "tests_list", new() { ["limit"] = 1 })).GetArrayLength());
        Assert.Equal(0, (await Call(client, "tests_list", new() { ["state"] = "failed" })).GetArrayLength());
        // A state is asked for in the words each test's is said in, as the description names them.
        JsonElement notRun = await Call(client, "tests_list", new() { ["state"] = test.GetProperty("state").GetString() });
        Assert.True(notRun.GetArrayLength() == 2 && notRun.EnumerateArray().All(t => t.GetProperty("state").GetString() == "notRun"),
            $"tests_list asked for the state it says the tests are in answered: {notRun}");
        CallToolResult wrong = await Result(client, "tests_list", new() { ["state"] = "broken" });
        Assert.True(wrong.IsError == true && Text(wrong).Contains("there are passed, failed, skipped and notRun", StringComparison.Ordinal), $"tests_list in no state answered: {Text(wrong)}");
    }

    /// <summary>A run refused for a reason of its own is answered with that reason alone, though the last build's errors are
    /// still in Problems: they were added to any refusal whose words held "failed" (here a launch profile's command named
    /// RetryFailed, which Passenger does not run), and still follow a run refused for the build it ran failing.</summary>
    [Fact]
    public async Task An_agents_run_refused_for_anything_but_its_own_failed_build_names_no_build_errors()
    {
        Write("Program.cs", "int count = \"three\";\nConsole.WriteLine(count);\n");
        Directory.CreateDirectory(Path.Combine(Folder, "Properties"));
        File.WriteAllText(Path.Combine(Folder, "Properties", "launchSettings.json"), "{\n  \"profiles\": {\n    \"Jobs\": { \"commandName\": \"RetryFailed\" }\n  }\n}\n");
        await using McpClient client = await Start(["--mcp", Folder]);
        JsonElement built = await Call(client, "build");
        JsonElement error = built.GetProperty("problems").EnumerateArray().FirstOrDefault(p => p.GetProperty("severity").GetString() == "error");
        Assert.True(!built.GetProperty("succeeded").GetBoolean() && error.ValueKind == JsonValueKind.Object, $"The build of a string given to an int did not fail with an error: {built}");
        string code = error.GetProperty("code").GetString()!;

        CallToolResult jobs = await Result(client, "run", new() { ["configuration"] = "Sum: Jobs" });
        Assert.True(jobs.IsError == true && Text(jobs).Contains("Passenger does not know the RetryFailed command", StringComparison.Ordinal),
            $"The run of a profile Passenger does not run was answered: {Text(jobs)}");
        Assert.False(Text(jobs).Contains(code, StringComparison.Ordinal), $"A run refused before any build named the last build's errors: {Text(jobs)}");

        CallToolResult sum = await Result(client, "run", new() { ["configuration"] = "Sum" });
        Assert.True(sum.IsError == true && Text(sum).Contains($"{code} ", StringComparison.Ordinal) && Text(sum).Contains("Program.cs:1:", StringComparison.Ordinal),
            $"A run refused for its own build's failure did not name that build's error: {Text(sum)}");
    }

    /// <summary>A debugged test run an agent goes on with from its stop answers, as its test host ends, with the tally and
    /// failures tests_run gives, where only tests_list had them.</summary>
    [Fact]
    public async Task An_agent_going_on_from_a_stop_in_a_debugged_test_run_gets_its_tally_as_it_ends()
    {
        string tests = PathNames.Canonical(TestFolders.Create("mcp-debug-tests-"));
        DeleteAtEnd(tests);
        TestFolders.Prepare(() =>
        {
            File.WriteAllText(Path.Combine(tests, "Sums.Tests.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n" +
                "  <ItemGroup>\n    <PackageReference Include=\"Microsoft.NET.Test.Sdk\" Version=\"18.10.1\" />\n    <PackageReference Include=\"xunit\" Version=\"2.9.3\" />\n" +
                "    <PackageReference Include=\"xunit.runner.visualstudio\" Version=\"3.1.5\" />\n  </ItemGroup>\n</Project>\n");
            File.WriteAllText(Path.Combine(tests, "SumTests.cs"),
                "using Xunit;\n\nnamespace Sums.Tests;\n\npublic class SumTests\n{\n    [Fact]\n    public void Two_and_two_make_four()\n    {\n        int sum = 2 + 2;\n" +
                "        Assert.Equal(4, sum);\n    }\n\n    [Fact]\n    public void Two_and_two_make_five() => Assert.Equal(5, 2 + 2);\n}\n");
            Restore.Run(tests);
        }, tests);
        await using McpClient client = await Start(["--mcp", tests]);
        await Call(client, "breakpoint_set", new() { ["path"] = "SumTests.cs", ["line"] = 10 });
        // Stopped at the breakpoint, and at the failing test's exception where the exception breakpoints say so, in either order.
        JsonElement ended = (await Call(client, "tests_run", new() { ["debug"] = true })).GetProperty("debugger");
        List<string> stops = [];
        while (ended.GetProperty("state").GetString() == "paused" && stops.Count < 5)
        {
            stops.Add(ended.GetProperty("location").GetString()!);
            ended = await Call(client, "debug_continue");
        }
        Assert.Contains(stops, s => s.StartsWith("SumTests.cs:10:", StringComparison.Ordinal));
        Assert.Equal("not debugging", ended.GetProperty("state").GetString());
        Assert.True(ended.TryGetProperty("tests", out JsonElement tally), $"debug_continue answered the debugged run's end without its tests: {ended}");
        Assert.StartsWith("Tests passed: 1, failed: 1 of 2", tally.GetProperty("summary").GetString(), StringComparison.Ordinal);
        JsonElement failure = Assert.Single(tally.GetProperty("failures").EnumerateArray());
        Assert.Equal("Sums.Tests.SumTests.Two_and_two_make_five", failure.GetProperty("test").GetString());
        Assert.Contains("Assert.Equal() Failure", failure.GetProperty("details").GetString(), StringComparison.Ordinal);
    }

    /// <summary>An agent's stop ends a debug session still starting, as Stop does: while debug_start waits for a program whose
    /// runtime is held at its start (a launch profile's diagnostic port, the test's own), stop ends the session and the
    /// program at once, and debug_start answers that it was stopped.</summary>
    [Fact]
    public async Task An_agents_stop_ends_a_debug_session_whose_program_is_still_starting()
    {
        using Debugging.Helpers.StartHold hold = new();
        Directory.CreateDirectory(Path.Combine(Folder, "Properties"));
        File.WriteAllText(Path.Combine(Folder, "Properties", "launchSettings.json"), JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["profiles"] = new Dictionary<string, object> { ["Held"] = new Dictionary<string, object> { ["commandName"] = "Project", ["environmentVariables"] = hold.Environment } },
        }));
        await using McpClient client = await Start(["--mcp", Folder]);
        Task<int> held = hold.Held(TimeSpan.FromMinutes(3));
        Task<CallToolResult> debugging = Result(client, "debug_start", new() { ["configuration"] = "Sum: Held" });
        int program = 0;
        try
        {
            if (await Task.WhenAny(held, debugging) == debugging)
            {
                Assert.Fail($"debug_start answered before the program came to the hold: {Words(await debugging)}{Said()}");
            }
            program = await held;

            Assert.Equal("Stopped", (await Call(client, "stop")).GetString());
            Assert.True(await Waiting.Within(debugging, TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken),
                $"debug_start still waited for the program {TimeSpan.FromSeconds(15).Scaled().TotalSeconds} s after the agent's stop{Said()}");
            CallToolResult stopped = await debugging;
            Assert.True(stopped.IsError == true && Words(stopped).Contains("Debugging Sum: Held was stopped as it started", StringComparison.Ordinal),
                $"debug_start, stopped as it started, answered: {Words(stopped)}");
            Assert.True(Processes.Gone(program), $"The agent's stop left the program running: {Processes.Describe(program)}");
            Assert.Equal("not debugging", (await Call(client, "debug_state")).GetProperty("state").GetString());
        }
        finally
        {
            Processes.Kill(program);
        }

        static string Words(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(t => t.Text));
    }

    /// <summary>A package an agent installs or removes is in or out of the project file and Directory.Packages.props the
    /// editor holds once the tool answers, which names the version installed and the files it changed, so the agent's
    /// next look sees what it did, not the text from before.</summary>
    [Fact]
    public async Task An_agents_package_installed_and_removed_shows_in_the_open_project_file_as_each_tool_answers()
    {
        string library = PathNames.Canonical(TestFolders.Create("mcp-nuget-"));
        DeleteAtEnd(library);
        string feed = Directory.CreateDirectory(Path.Combine(library, "feed")).FullName;
        File.WriteAllBytes(Path.Combine(feed, "Passenger.Sample.1.0.0.nupkg"), StandInFeed.Package("Passenger.Sample", "1.0.0"));
        // Only the folder feed, and restores kept out of the person's own package cache.
        File.WriteAllText(Path.Combine(library, "nuget.config"),
            "<configuration>\n  <packageSources>\n    <clear />\n    <add key=\"local\" value=\"feed\" />\n  </packageSources>\n"
            + "  <config>\n    <add key=\"globalPackagesFolder\" value=\"packages\" />\n  </config>\n</configuration>\n");
        File.WriteAllText(Path.Combine(library, "Lib.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
        // Central package management: the version goes in Directory.Packages.props.
        File.WriteAllText(Path.Combine(library, "Directory.Packages.props"),
            "<Project>\n  <PropertyGroup>\n    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>\n  </PropertyGroup>\n</Project>\n");
        await using McpClient client = await Start(["--mcp", library]);
        Dictionary<string, object?> project = new() { ["path"] = "Lib.csproj" };
        Dictionary<string, object?> versions = new() { ["path"] = "Directory.Packages.props" };
        Assert.DoesNotContain("Passenger.Sample", (await Call(client, "editor_text", project)).GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Passenger.Sample", (await Call(client, "editor_text", versions)).GetString(), StringComparison.Ordinal);
        // Found on the folder's own feed, as the NuGet window's search finds it.
        JsonElement found = Assert.Single((await Call(client, "nuget_search", new() { ["query"] = "Passenger" })).EnumerateArray());
        Assert.Equal(("Passenger.Sample", "1.0.0"), (found.GetProperty("id").GetString(), found.GetProperty("version").GetString()));

        // No version asked for: the newest on the feed, which the answer names with each file it changed.
        JsonElement install = await Call(client, "nuget_install", new() { ["project"] = "Lib", ["id"] = "Passenger.Sample" });
        Assert.Equal(("Passenger.Sample", "1.0.0", "Lib"), (install.GetProperty("package").GetString(), install.GetProperty("version").GetString(), install.GetProperty("project").GetString()));
        Assert.Equal(["Directory.Packages.props", "Lib.csproj"], install.GetProperty("changed").EnumerateArray().Select(f => f.GetString()!).Order(StringComparer.Ordinal));
        string installed = (await Call(client, "editor_text", project)).GetString()!;
        Assert.True(installed.Contains("<PackageReference Include=\"Passenger.Sample\" />", StringComparison.Ordinal),
            $"nuget_install answered with the open project file still as it was:\n{installed}");
        string pinned = (await Call(client, "editor_text", versions)).GetString()!;
        Assert.True(pinned.Contains("<PackageVersion Include=\"Passenger.Sample\" Version=\"1.0.0\" />", StringComparison.Ordinal),
            $"nuget_install answered with the open Directory.Packages.props still as it was:\n{pinned}");

        JsonElement remove = await Call(client, "nuget_remove", new() { ["project"] = "Lib", ["id"] = "Passenger.Sample" });
        Assert.Contains("Lib.csproj", remove.GetProperty("changed").EnumerateArray().Select(f => f.GetString()));
        Assert.True(!remove.TryGetProperty("version", out JsonElement left) || left.ValueKind == JsonValueKind.Null, $"nuget_remove answered a version: {remove}");
        string removed = (await Call(client, "editor_text", project)).GetString()!;
        Assert.False(removed.Contains("Passenger.Sample", StringComparison.Ordinal), $"nuget_remove answered with the open project file still holding the package:\n{removed}");
    }

    /// <summary>Git's tools on a repository, a merge stopped for a conflict among them: the agent finds it in vcs_status as
    /// the Conflicts dialog lists it (what is under way, each side's branch, what each did to the file; ConflictsTests resolve
    /// it in the window), resolves it with theirs, and its commit finishes the merge; and a command in the IDE's terminal.</summary>
    [Fact]
    public async Task An_agent_reads_and_commits_changes_resolves_a_merges_conflict_and_runs_a_command_in_the_ides_terminal()
    {
        string repository = TestFolders.Create("mcp-git-");
        try
        {
            File.WriteAllText(Path.Combine(repository, "Notes.txt"), "one\ntwo\nthree\n");
            TestGit.Init(repository);
            File.WriteAllText(Path.Combine(repository, "Notes.txt"), "one\n2\nthree\n");
            File.WriteAllText(Path.Combine(repository, "New.txt"), "new\n");

            await using McpClient client = await Start(["--mcp", repository]);
            JsonElement status = await Call(client, "vcs_status");
            Assert.Equal("main", status.GetProperty("branch").GetString());
            Assert.Equal(["Notes.txt modified", "New.txt unversioned"], status.GetProperty("changes").EnumerateArray()
                .Select(c => $"{c.GetProperty("path").GetString()} {c.GetProperty("kind").GetString()}").Order(StringComparer.Ordinal).Reverse());
            string diff = (await Call(client, "vcs_diff", new() { ["path"] = "Notes.txt" })).GetString()!;
            Assert.Contains("-two\n+2\n", diff, StringComparison.Ordinal);

            // With no paths, every change but the new file goes in, as the Commit window has them chosen.
            Assert.EndsWith(" Two as a number", (await Call(client, "vcs_commit", new() { ["message"] = "Two as a number" })).GetString(), StringComparison.Ordinal);
            JsonElement log = await Call(client, "vcs_log", new() { ["count"] = 5 });
            Assert.Equal(["Two as a number", "First"], log.EnumerateArray().Select(c => c.GetProperty("subject").GetString()));
            Assert.Equal(["New.txt"], (await Call(client, "vcs_status")).GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("path").GetString()));

            // Stash Changes takes the new file away, and Unstash Changes brings it back.
            JsonElement stashed = Assert.Single((await Call(client, "vcs_stash", new() { ["action"] = "push", ["message"] = "the new file" })).EnumerateArray());
            Assert.EndsWith("the new file", stashed.GetProperty("message").GetString(), StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(repository, "New.txt")), "Stash Changes left the new file in place");
            Assert.Empty((await Call(client, "vcs_stash", new() { ["action"] = "pop", ["index"] = 0 })).EnumerateArray());
            Assert.Equal("new\n", File.ReadAllText(Path.Combine(repository, "New.txt")));

            // A branch an agent names is a name, never one of git's options: git switch --orphan=x would empty the tree.
            CallToolResult orphan = await Result(client, "vcs_checkout", new() { ["branch"] = "--orphan=x" });
            Assert.True(orphan.IsError, "vcs_checkout took the branch --orphan=x for an option, and checked out an empty branch");
            Assert.True(File.Exists(Path.Combine(repository, "Notes.txt")), "Checking out the branch --orphan=x emptied the working tree");
            Assert.Equal("main", (await Call(client, "vcs_status")).GetProperty("branch").GetString());

            // A merge stopped for its conflict: listed, a commit refused while it stands, resolved with theirs, then committed.
            string notes = Path.Combine(repository, "Notes.txt");
            TestGit.Run(repository, "switch", "-q", "-c", "feature");
            File.WriteAllText(notes, "one\nTWO\nthree\n");
            TestGit.Run(repository, "commit", "-q", "-am", "Two in capitals");
            TestGit.Run(repository, "switch", "-q", "main");
            File.WriteAllText(notes, "one\nII\nthree\n");
            TestGit.Run(repository, "commit", "-q", "-am", "Two in Roman");
            ChildResult merging = await ChildProcesses.RunAsync(ChildProcesses.Command("git", ["merge", "feature"], repository), limit: TimeSpan.FromMinutes(1), cancel: Cancel);
            Assert.False(merging.Succeeded, $"The merge was to stop for its conflict: {merging.Output}");
            JsonElement conflicts = (await Call(client, "vcs_status")).GetProperty("conflicts");
            Assert.Equal("Merging branch feature into branch main", conflicts.GetProperty("operation").GetString());
            Assert.Equal(("main", "feature"), (conflicts.GetProperty("yours").GetString(), conflicts.GetProperty("theirs").GetString()));
            JsonElement conflicted = Assert.Single(conflicts.GetProperty("files").EnumerateArray());
            Assert.Equal(("Notes.txt", "modified", "modified"), (conflicted.GetProperty("path").GetString(), conflicted.GetProperty("yours").GetString(), conflicted.GetProperty("theirs").GetString()));
            CallToolResult early = await Result(client, "vcs_commit", new() { ["message"] = "Half a merge" });
            Assert.True(early.IsError == true, "A commit was made with a file still in conflict");
            Assert.Equal("Notes.txt resolved; no conflicts are left", (await Call(client, "vcs_resolve", new() { ["path"] = "Notes.txt", ["take"] = "theirs" })).GetString());
            Assert.Equal("one\nTWO\nthree\n", File.ReadAllText(notes));
            JsonElement resolved = await Call(client, "vcs_status");
            Assert.True(!resolved.TryGetProperty("conflicts", out JsonElement left) || left.ValueKind == JsonValueKind.Null, $"vcs_status still lists conflicts: {left}");
            await Call(client, "vcs_commit", new() { ["message"] = "Merge feature" });
            Assert.Equal(3, TestGit.Run(repository, "rev-list", "--parents", "-n", "1", "HEAD").Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
            Assert.Equal("one\nTWO\nthree\n", TestGit.Run(repository, "show", "HEAD:Notes.txt"));

            // The IDE's own terminal runs it in the folder, and its answer comes back.
            // Windows' own PowerShell (5.1, where pwsh is not installed) has no &&, and sh has no PowerShell subexpressions.
            string command = OperatingSystem.IsWindows() ? "echo (6*7); ls" : "echo $((6*7)) && ls";
            string answer = (await Call(client, "terminal_run", new() { ["command"] = command })).GetString()!;
            Assert.Contains("42", answer, StringComparison.Ordinal);
            Assert.Contains("Notes.txt", answer, StringComparison.Ordinal);
            string screen = (await Call(client, "terminal_screen")).GetString()!;
            Assert.True(screen.Contains("42", StringComparison.Ordinal), $"terminal_screen did not show what the command printed:\n{screen}");
            if (OperatingSystem.IsWindows())
            {
                // PowerShell's history is the run's (HISTFILE), as sh's is: never the person's, which PSReadLine keeps.
                string history = Path.Combine(TestRun.Root, "history");
                Assert.True(File.Exists(history) && File.ReadAllText(history).Contains(command, StringComparison.Ordinal),
                    $"PowerShell kept the command typed into the terminal, {command}, elsewhere than the run's history ({history}): in the person's own");
            }
            else
            {
                // Past the scrollback's limit, where each line printed drops the oldest, a command's answer is still its own.
                int beyond = global::Passenger.Terminal.TerminalScreen.ScrollbackLimit + 100;
                await Call(client, "terminal_run", new() { ["command"] = $"seq 1 {beyond}" });
                string past = (await Call(client, "terminal_run", new() { ["command"] = "echo one; echo two" })).GetString()!;
                Assert.True(past.Contains("one\ntwo", StringComparison.Ordinal), $"past the scrollback's limit, terminal_run answered a command that printed one and two with:\n{past}");
            }
        }
        finally
        {
            TestFolders.Delete(repository);
        }
    }

    /// <summary>Commit and Push to a remote that refuses every push: the agent hears the refusal, in git's words, as the
    /// tool's error, and the commit stays made, as the Commit window's Commit and Push leaves it.</summary>
    [Fact]
    public async Task An_agents_push_the_remote_refuses_is_an_error_naming_the_refusal_and_the_commit_stays_made()
    {
        string root = TestFolders.Create("mcp-push-");
        try
        {
            string work = Repository(root, refusing: true);
            await using McpClient client = await Start(["--mcp", work]);
            CallToolResult refused = await Result(client, "vcs_commit", new() { ["message"] = "Two as a number", ["push"] = true });
            string said = string.Concat(refused.Content.OfType<TextContentBlock>().Select(t => t.Text));
            Assert.True(refused.IsError == true, $"A push the remote refused was answered as done: {said}");
            Assert.Contains("pre-receive hook declined", said, StringComparison.Ordinal);
            Assert.Contains("pushes are closed here", said, StringComparison.Ordinal);
            Assert.Equal("Two as a number", TestGit.Run(work, "log", "-1", "--format=%s").Trim());
            Assert.True(TestGit.Run(Path.Combine(root, "remote.git"), "for-each-ref", "refs/heads").Trim().Length == 0, "The remote took the push it refused");
        }
        finally
        {
            TestFolders.Delete(root);
        }
    }

    /// <summary>A call that takes a while tells an agent that asked for progress what it is doing as it goes (the run's
    /// stages, then the wait for the program), at rising counts, so a client that gives up on a silent call (OpenCode's
    /// 60 s) waits on; and the agent's own Passenger writes nothing to the standard error an agent may never read.</summary>
    [Fact]
    public async Task A_call_that_takes_a_while_tells_its_agent_what_it_is_doing_and_nothing_reaches_standard_error()
    {
        Write("Program.cs", "Console.WriteLine(\"waiting\");\nThread.Sleep(Timeout.Infinite);\n");
        await using McpClient client = await Start(["--mcp", Folder]);
        Heard heard = new();
        CallToolResult ran = await Result(client, "run", new() { ["waitSeconds"] = 4 }, heard: heard);
        Assert.False(ran.IsError == true, $"run failed: {Text(ran)}{Said()}");
        IReadOnlyList<ModelContextProtocol.ProgressNotificationValue> told = heard.Told;
        Assert.True(told.Count >= 2, $"A run that waited 4 s for its program told its agent {told.Count} times (every second asked): {heard}");
        // Each told once, under counts of its own: the SDK's client hands them over as they come, not always in order.
        Assert.True(told.Select(t => t.Progress).Distinct().Count() == told.Count, $"The progress told repeated a count: {heard}");
        Assert.True(told.Any(t => t.Message?.StartsWith("Running Sum: waiting up to 4 s for it to end", StringComparison.Ordinal) == true),
            $"The agent was never told the run waits for its program: {heard}");
        // Each word says how long the call has taken so far.
        Assert.Matches(@"\(\d+\.\d s\)$", told[^1].Message ?? string.Empty);
        await Call(client, "stop");
        Assert.True(Errors().Length == 0, $"The agent's Passenger wrote to its standard error, which an agent may never read:\n{string.Join('\n', Errors())}");
    }

    /// <summary>Started as the Finder or the Dock start it, Passenger runs the git its login shell's PATH finds, though the
    /// profile leaves a job running, and no git it starts waits on that job.</summary>
    [Fact]
    public async Task A_login_profile_that_leaves_a_job_running_holds_up_no_git_Passenger_starts()
    {
        if (OperatingSystem.IsWindows())
        {
            // A Windows program gets the person's whole environment, PATH included.
            return;
        }
        string home = TestFolders.Create("home-");
        string repository = TestFolders.Create("mcp-shell-");
        try
        {
            // As `some-daemon &` in a profile: the job keeps the shell's output open long after the shell has ended.
            string used = ProfileGit(home, "sleep 300 &\n");
            TestGit.Run(repository, "init", "-q");
            await using McpClient client = await Start(["--mcp", repository], environment: Launchd(home));
            await Call(client, "vcs_status");
            Assert.True(File.Exists(used), "With a job its profile left running, Passenger never took the login shell's PATH, and ran a git other than the one it finds");

            // Each git start waits for the shell's environment while it is still being read.
            long began = Stopwatch.GetTimestamp();
            await Call(client, "vcs_status");
            TimeSpan took = Stopwatch.GetElapsedTime(began);
            Assert.True(took < TimeSpan.FromSeconds(10).Scaled(),
                $"Git's status took {took.TotalSeconds:0.0} s the second time: the git it ran waited for the login shell's environment again, the profile's job holding up its read");
        }
        finally
        {
            TestFolders.Delete(home);
            TestFolders.Delete(repository);
        }
    }

    /// <summary>An agent's view of the IDE's actions: every one the IDE declares, listed with why it cannot run now, and run by
    /// its id as its shortcut would.</summary>
    [Fact]
    public async Task An_agent_lists_every_action_the_ide_declares_and_one_it_runs_says_why_when_it_cannot()
    {
        await using McpClient client = await Start(["--mcp", Folder]);
        string[] listed = [.. (await Call(client, "ide_actions")).EnumerateArray().Select(a => a.GetString()!)];
        HashSet<string> ids = [.. listed.Select(line => line[..line.IndexOf(": ", StringComparison.Ordinal)])];
        // Every id the IDE declares, the MVP's newest among them (Hot Reload, the Live Visual Tree's, the overlays, XAML's).
        string[] declared = [.. typeof(AppActions).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()!)];
        string[] missing = [.. declared.Where(id => !ids.Contains(id))];
        Assert.True(missing.Length == 0, $"ide_actions leaves out {string.Join(", ", missing)}");

        // One that cannot run here says why, there and when it is run.
        string hotReload = Assert.Single(listed, l => l.StartsWith($"{AppActions.ApplyHotReloadChanges}: ", StringComparison.Ordinal));
        Assert.Contains(" - cannot run: Nothing runs", hotReload, StringComparison.Ordinal);
        CallToolResult refused = await Result(client, "ide_action", new() { ["action"] = AppActions.ApplyHotReloadChanges });
        Assert.True(refused.IsError == true && Text(refused).Contains($"{AppActions.ApplyHotReloadChanges} did not run: Nothing runs", StringComparison.Ordinal),
            $"Apply Hot Reload Changes with nothing running answered: {Text(refused)}");
        string selectElement = Assert.Single(listed, l => l.StartsWith($"{AppActions.SelectElement}: ", StringComparison.Ordinal));
        Assert.Contains("cannot run: No Avalonia or WPF app is being debugged", selectElement, StringComparison.Ordinal);

        // One that can, runs: the Live Visual Tree shown, as its action shows it.
        Assert.Equal($"Ran {AppActions.ShowLiveTree}", (await Call(client, "ide_action", new() { ["action"] = AppActions.ShowLiveTree })).GetString());
    }

    /// <summary>An agent's publish, build's with a project to publish: the project to a folder, as Publish does, answered with
    /// where it landed or why it did not.</summary>
    [Fact]
    public async Task An_agent_publishes_a_project_and_is_told_the_folder_it_landed_in_or_why_not()
    {
        await using McpClient client = await Start(["--mcp", Folder]);
        JsonElement published = await Call(client, "build", new() { ["publish"] = "Sum" });
        Assert.True(published.GetProperty("succeeded").GetBoolean(), $"The publish ended: {published}");
        string landed = published.GetProperty("folder").GetString() ?? string.Empty;
        Assert.True(File.Exists(Path.Combine(landed, "Sum.dll")), $"The publish says it landed in {landed}, which holds no Sum.dll");
        Assert.StartsWith("Published Sum to ", published.GetProperty("outcome").GetString(), StringComparison.Ordinal);

        // A profile the project does not have publishes nothing and says so, never what MSBuild would make of no profile.
        JsonElement missing = await Call(client, "build", new() { ["publish"] = "Sum", ["profile"] = "Nightly" });
        Assert.False(missing.GetProperty("succeeded").GetBoolean(), $"A publish with no such profile ended: {missing}");
        Assert.Contains("has no publish profile Nightly", missing.GetProperty("outcome").GetString(), StringComparison.Ordinal);
    }

    /// <summary>Whether a schema says what leaving a parameter out says already: a null default, or null among its types.</summary>
    private static bool SaysNothing(JsonElement schema) => schema.ValueKind switch
    {
        JsonValueKind.Object => schema.EnumerateObject().Any(p => (p.Name == "default" && p.Value.ValueKind == JsonValueKind.Null)
            || (p.Name == "type" && p.Value.ValueKind == JsonValueKind.Array) || SaysNothing(p.Value)),
        JsonValueKind.Array => schema.EnumerateArray().Any(SaysNothing),
        _ => false,
    };

    /// <summary>A home whose login profile (zsh's and sh's) puts a git of its own first on the PATH, as Homebrew's shellenv
    /// does, and then does <paramref name="more"/>; the git notes that it ran in the file whose path this returns.</summary>
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static string ProfileGit(string home, string more = "")
    {
        string tools = Path.Combine(home, "tools");
        Directory.CreateDirectory(tools);
        string git = Path.Combine(tools, "git");
        string used = Path.Combine(home, "used");
        string real = global::Passenger.Core.Programs.Git()!;
        File.WriteAllText(git, $"#!/bin/sh\ntouch \"{used}\"\nexec {real} \"$@\"\n");
        File.SetUnixFileMode(git, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.WriteAllText(Path.Combine(home, ".zprofile"), $"export PATH=\"{tools}:$PATH\"\n{more}");
        File.WriteAllText(Path.Combine(home, ".profile"), $"export PATH=\"{tools}:$PATH\"\n{more}");
        return used;
    }

    /// <summary>launchd's environment, as the Finder or the Dock start Passenger: the bare PATH, no terminal, no .NET or zsh
    /// settings of the person's, the home given.</summary>
    private static Dictionary<string, string?> Launchd(string home) => new()
    {
        ["PATH"] = "/usr/bin:/bin:/usr/sbin:/sbin",
        ["HOME"] = home,
        ["SHELL"] = File.Exists("/bin/zsh") ? "/bin/zsh" : "/bin/sh",
        ["TERM"] = null,
        ["ZDOTDIR"] = null,
        ["DOTNET_ROOT"] = null,
    };
}
