using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Passenger.Core;

namespace Passenger.Tests;

/// <summary>Agents using the code tools on every language Passenger serves, each answered by that language's own service as
/// the window's commands are: Avalonia's and WPF's XAML through Passenger's own XAML (the galleries of XamlTests and
/// WpfXamlTests, each in a folder of the test's own, restored), the web languages through their servers with the
/// project's own ESLint and Prettier (skipped without a Node.js, as WebLanguagesTests are), and saying plainly what a
/// language lacks.</summary>
public sealed partial class McpTests
{
    [Fact]
    public async Task An_agent_reads_navigates_renames_and_refactors_avalonia_xaml_through_passengers_own_xaml()
    {
        string gallery = Made("mcpaxaml-", made =>
        {
            File.WriteAllText(Path.Combine(made, "Gallery.csproj"), AvaloniaApp.HeadlessProject());
            Directory.CreateDirectory(Path.Combine(made, "ViewModels"));
            Directory.CreateDirectory(Path.Combine(made, "Styles"));
            File.WriteAllText(Path.Combine(made, "ViewModels", "MainViewModel.cs"), XamlTests.Gallery.MainViewModel);
            File.WriteAllText(Path.Combine(made, "Styles", "Palette.axaml"), XamlTests.Gallery.Palette);
            File.WriteAllText(Path.Combine(made, "App.axaml"), XamlTests.Gallery.AppXaml);
            File.WriteAllText(Path.Combine(made, "App.axaml.cs"),
                "using Avalonia;\nusing Avalonia.Markup.Xaml;\n\nnamespace Gallery;\n\npublic partial class App : Application\n{\n    public override void Initialize() => AvaloniaXamlLoader.Load(this);\n}\n");
            File.WriteAllText(Path.Combine(made, "Program.cs"), "namespace Gallery;\n\npublic static class Program\n{\n    public static void Main()\n    {\n    }\n}\n");
            // The gallery's window, with a caption bound to a property its view model lacks.
            File.WriteAllText(Path.Combine(made, "MainWindow.axaml"), XamlTests.Gallery.Window.Replace(
                "<TextBlock Text=\"{Binding #Go.Content}\" />", "<TextBlock Text=\"{Binding #Go.Content}\" />\n        <TextBlock Text=\"{Binding Subtitle}\" />", StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(made, "MainWindow.axaml.cs"), XamlTests.Gallery.CodeBehind);
            Restore.Run(made);
        });
        await using McpClient client = await Start(["--mcp", gallery]);
        string window = File.ReadAllText(Path.Combine(gallery, "MainWindow.axaml"));

        // The element tree, each element with what names it.
        string outline = (await Call(client, "outline", new() { ["path"] = "MainWindow.axaml" })).GetString()!;
        Assert.True(outline.StartsWith("Window x:Class=\"Gallery.MainWindow\" x:DataType=\"vm:MainViewModel\" (line 1)\n  StackPanel (line 10)\n", StringComparison.Ordinal)
            && outline.Contains("\n    Button Name=\"Go\" (line 12)", StringComparison.Ordinal), $"The window's outline is not its element tree:\n{outline}");

        // The hover card's words for a tag, and where its names lead: a binding to its view model's property, a resource to
        // each theme's definition of it.
        Assert.Equal("class Avalonia.Controls.Button : ContentControl", (await Call(client, "symbol_info", At(window, "MainWindow.axaml", "<Butt"))).GetString());
        JsonElement greeting = Assert.Single((await Call(client, "definition", At(window, "MainWindow.axaml", "{Binding Gree"))).EnumerateArray());
        Assert.Equal((Path.Combine("ViewModels", "MainViewModel.cs"), 5), (greeting.GetProperty("path").GetString(), greeting.GetProperty("line").GetInt32()));
        Assert.Contains("public string Greeting", greeting.GetProperty("text").GetString(), StringComparison.Ordinal);
        JsonElement[] accents = [.. (await Call(client, "definition", At(window, "MainWindow.axaml", "{StaticResource Acc"))).EnumerateArray()];
        Assert.Equal([(Path.Combine("Styles", "Palette.axaml"), 5), (Path.Combine("Styles", "Palette.axaml"), 9)],
            accents.Select(a => (a.GetProperty("path").GetString(), a.GetProperty("line").GetInt32())));

        // The button's name is used in its file and by its code-behind, through the field Avalonia's generator makes.
        string[] uses = [.. (await Call(client, "usages", At(window, "MainWindow.axaml", "Name=\"G"))).EnumerateArray()
            .Select(u => $"{u.GetProperty("path").GetString()}:{u.GetProperty("line").GetInt32()}")];
        Assert.True(uses.Contains("MainWindow.axaml:12") && uses.Contains("MainWindow.axaml:13") && uses.Contains("MainWindow.axaml.cs:10"),
            $"The button's name was not found where its file and its code-behind use it: {string.Join(", ", uses)}");
        // A binding's path is not what XAML's Find Usages finds, and the agent hears what it does find.
        CallToolResult path = await Result(client, "usages", At(window, "MainWindow.axaml", "{Binding Gree"));
        Assert.True(path.IsError == true && Text(path).Contains("finds an element's name or a resource's key", StringComparison.Ordinal),
            $"usages on a binding's path did not say what XAML's Find Usages finds: {Text(path)}");

        // The property the caption binds is marked, and its fix creates it in the view model.
        JsonElement missing = Assert.Single((await Call(client, "diagnostics", new() { ["path"] = "MainWindow.axaml" })).EnumerateArray());
        Assert.Contains("'Subtitle'", missing.GetProperty("message").GetString(), StringComparison.Ordinal);
        Dictionary<string, object?> subtitle = At(window, "MainWindow.axaml", "{Binding Subt");
        Assert.Contains("Create property 'Subtitle' [quickfix]", Strings(await Call(client, "code_actions", subtitle)));
        await Call(client, "apply_code_action", new(subtitle) { ["title"] = "Create property 'Subtitle'" });
        Assert.Contains("public string? Subtitle { get; set; }", File.ReadAllText(Path.Combine(gallery, "ViewModels", "MainViewModel.cs")), StringComparison.Ordinal);

        // The panel's margin to a resource of the window's, under a name of the agent's, among the places the dialog offers.
        Dictionary<string, object?> margin = At(window, "MainWindow.axaml", "<StackPanel Margin=\"1");
        Assert.Equal(["Extract Resource... [refactor.extract], choices: MainWindow.axaml <Window.Resources>; App.axaml <Application.Resources>; Palette.axaml <ResourceDictionary>",
            "Extract Style... [refactor.extract], choices: MainWindow.axaml <Window.Styles>; App.axaml <Application.Styles>"], Strings(await Call(client, "code_actions", margin)));
        Assert.Equal("Extracted resource 'PanelMargin' to MainWindow.axaml <Window.Resources>", (await Call(client, "apply_code_action",
            new(margin) { ["title"] = "Extract Resource...", ["choice"] = "MainWindow.axaml <Window.Resources>", ["name"] = "PanelMargin" })).GetString());
        window = File.ReadAllText(Path.Combine(gallery, "MainWindow.axaml"));
        Assert.True(window.Contains("<Thickness x:Key=\"PanelMargin\">12</Thickness>", StringComparison.Ordinal)
            && window.Contains("<StackPanel Margin=\"{StaticResource PanelMargin}\"", StringComparison.Ordinal), $"Extract Resource did not reach the file on disk:\n{window}");

        // Renamed, the button's name changes in the XAML and the code-behind, both saved.
        string renamed = (await Call(client, "rename", new(At(window, "MainWindow.axaml", "Name=\"G")) { ["newName"] = "Start" })).GetString()!;
        Assert.Equal("Renamed Go to Start in 2 files: MainWindow.axaml, MainWindow.axaml.cs", renamed);
        Assert.Contains("Start.Content = \"Start\";", File.ReadAllText(Path.Combine(gallery, "MainWindow.axaml.cs")), StringComparison.Ordinal);
        Assert.Contains("{Binding #Start.Content}", File.ReadAllText(Path.Combine(gallery, "MainWindow.axaml")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_agent_reads_navigates_renames_and_refactors_wpf_xaml_through_passengers_own_xaml()
    {
        string gallery = Made("mcpwxaml-", made =>
        {
            WpfXamlTests.Gallery.Make(made);
            Restore.Run(made);
        });
        await using McpClient client = await Start(["--mcp", gallery]);
        string window = File.ReadAllText(Path.Combine(gallery, "MainWindow.xaml"));

        string outline = (await Call(client, "outline", new() { ["path"] = "MainWindow.xaml" })).GetString()!;
        Assert.True(outline.StartsWith("Window x:Class=\"Gallery.MainWindow\" (line 1)\n  Window.Resources (line 10)\n    Style TargetType=\"Button\" (line 11)\n", StringComparison.Ordinal)
            && outline.Contains("\n    Button x:Name=\"Go\" (line 30)", StringComparison.Ordinal), $"The window's outline is not its element tree:\n{outline}");
        Assert.Equal("class System.Windows.Controls.TextBlock : FrameworkElement",
            (await Call(client, "symbol_info", At(window, "MainWindow.xaml", "<StackPanel Margin=\"12\">\n        <TextBl"))).GetString());
        JsonElement accent = Assert.Single((await Call(client, "definition", At(window, "MainWindow.xaml", "{StaticResource Acc"))).EnumerateArray());
        Assert.Equal((Path.Combine("Themes", "Brushes.xaml"), 4), (accent.GetProperty("path").GetString(), accent.GetProperty("line").GetInt32()));
        // WPF's own TextBlock, decompiled from WindowsDesktop's reference assembly.
        JsonElement library = Assert.Single((await Call(client, "definition", At(window, "MainWindow.xaml", "<StackPanel Margin=\"12\">\n        <TextBl"))).EnumerateArray());
        Assert.EndsWith("System.Windows.Controls.TextBlock.cs", library.GetProperty("path").GetString(), StringComparison.Ordinal);

        // A resource key, wherever the project's XAML uses or defines it.
        string[] keyed = [.. (await Call(client, "usages", At(window, "MainWindow.xaml", "{StaticResource Acc"))).EnumerateArray()
            .Select(u => $"{u.GetProperty("path").GetString()}:{u.GetProperty("line").GetInt32()}")];
        Assert.True(keyed.Contains("App.xaml:12") && keyed.Contains("MainWindow.xaml:30") && keyed.Contains($"{Path.Combine("Themes", "Brushes.xaml")}:4"),
            $"The brush's key was not found where the project's XAML uses and defines it: {string.Join(", ", keyed)}");
        // The button's name, used through the field MarkupCompile makes.
        string[] named = [.. (await Call(client, "usages", At(window, "MainWindow.xaml", "x:Name=\"G"))).EnumerateArray().Select(u => u.GetProperty("text").GetString()!)];
        Assert.True(named.Any(u => u.Contains("Go.Content = \"Start\";", StringComparison.Ordinal)) && named.Any(u => u.Contains("ElementName=Go", StringComparison.Ordinal)),
            $"The button's name was not found where its file and its code-behind use it: {string.Join(" | ", named)}");

        Dictionary<string, object?> margin = At(window, "MainWindow.xaml", "<StackPanel Margin=\"1");
        Assert.Contains("Extract Resource... [refactor.extract], choices: MainWindow.xaml <Window.Resources>; App.xaml <Application.Resources>; Brushes.xaml <ResourceDictionary>",
            Strings(await Call(client, "code_actions", margin)));
        // A choice the action does not offer is refused, naming those it does.
        CallToolResult nowhere = await Result(client, "apply_code_action", new(margin) { ["title"] = "Extract Resource...", ["choice"] = "Elsewhere" });
        Assert.True(nowhere.IsError == true && Text(nowhere).Contains("it can go in: MainWindow.xaml <Window.Resources>", StringComparison.Ordinal),
            $"Extract Resource to a place it does not offer answered: {Text(nowhere)}");
        Dictionary<string, object?> button = At(window, "MainWindow.xaml", "<Butt");
        Assert.Equal("Extracted 2 properties of Button (Content, Background) to style 'ButtonStyle' in MainWindow.xaml <Window.Resources>",
            (await Call(client, "apply_code_action", new(button) { ["title"] = "Extract Style...", ["choice"] = "MainWindow.xaml <Window.Resources>" })).GetString());
        Assert.Contains("<Style x:Key=\"ButtonStyle\" TargetType=\"Button\">", File.ReadAllText(Path.Combine(gallery, "MainWindow.xaml")), StringComparison.Ordinal);

        string renamed = (await Call(client, "rename", new(At(File.ReadAllText(Path.Combine(gallery, "MainWindow.xaml")), "MainWindow.xaml", "x:Name=\"G")) { ["newName"] = "Start" })).GetString()!;
        Assert.Equal("Renamed Go to Start in 2 files: MainWindow.xaml, MainWindow.xaml.cs", renamed);
        Assert.Contains("Start.Content = \"Start\";", File.ReadAllText(Path.Combine(gallery, "MainWindow.xaml.cs")), StringComparison.Ordinal);
        Assert.Contains("ElementName=Start", File.ReadAllText(Path.Combine(gallery, "MainWindow.xaml")), StringComparison.Ordinal);
    }

    /// <summary>A script, a stylesheet and a package.json, with the project's own ESLint and Prettier (the tests' packages
    /// copied into its node_modules): each tool through the file's language server as the window asks it, ESLint's fix
    /// first among the actions, Prettier formatting what it covers, and JSON's Go to Declaration and Rename, which its server
    /// lacks, Passenger's own; its lack of usages said plainly.</summary>
    [Fact]
    public async Task An_agent_reads_navigates_renames_fixes_and_formats_the_web_languages_through_their_servers_eslint_and_prettier()
    {
        WebLanguagesTests.Shop.SkipWithoutNode(typescript: true);
        EslintPrettierTests.Project.SkipWithoutNode();
        string web = Made("mcpweb-", made =>
        {
            string packages = Path.Combine(AppContext.BaseDirectory, "node-packages", "node_modules");
            foreach (string file in Directory.EnumerateFiles(packages, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(made, "node_modules", Path.GetRelativePath(packages, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            File.WriteAllText(Path.Combine(made, "package.json"), "{\n  \"name\": \"shop-client\",\n  \"version\": \"1.0.0\",\n  \"private\": true,\n  \"type\": \"module\",\n"
                + "  \"scripts\": {\n    \"build\": \"node src/app.js\",\n    \"all\": \"npm run build\"\n  },\n"
                + $"  \"devDependencies\": {{\n    \"eslint\": \"{EslintPrettierTests.Project.VersionOf("eslint")}\",\n    \"prettier\": \"{EslintPrettierTests.Project.VersionOf("prettier")}\"\n  }}\n}}\n");
            File.WriteAllText(Path.Combine(made, "eslint.config.js"), EslintPrettierTests.Project.Config);
            File.WriteAllText(Path.Combine(made, ".prettierrc"), "{ \"semi\": false, \"singleQuote\": true }\n");
            Directory.CreateDirectory(Path.Combine(made, "src"));
            File.WriteAllText(Path.Combine(made, "src", "app.js"), "function greet(name) {\n  return `Hello, ${name}`\n}\nlet total = greet('Ada')\nconsole.log(total)\n");
            File.WriteAllText(Path.Combine(made, "src", "format.js"), EslintPrettierTests.Project.Format);
            Directory.CreateDirectory(Path.Combine(made, "styles"));
            File.WriteAllText(Path.Combine(made, "styles", "site.css"), ":root {\n  --accent: #3574f0;\n}\n\n.cart {\n  color: var(--accent);\n}\n");
        });
        await using McpClient client = await Start(["--mcp", web]);
        string app = Path.Combine("src", "app.js");
        string script = File.ReadAllText(Path.Combine(web, app));

        // TypeScript's server on the script: its outline, its card, where a call's function is declared and every use of it.
        string outline = (await Call(client, "outline", new() { ["path"] = app })).GetString()!;
        Assert.True(outline.Contains("greet (function, line 1)", StringComparison.Ordinal) && outline.Contains("total (variable, line 4)", StringComparison.Ordinal),
            $"The script's outline is not its server's:\n{outline}");
        Dictionary<string, object?> call = At(script, app, "let total = gre");
        Assert.Contains("function greet(name: any): string", (await Call(client, "symbol_info", call)).GetString(), StringComparison.Ordinal);
        JsonElement declared = Assert.Single((await Call(client, "definition", call)).EnumerateArray());
        Assert.Equal((app, 1), (declared.GetProperty("path").GetString(), declared.GetProperty("line").GetInt32()));
        Assert.Equal([1, 4], (await Call(client, "usages", call)).EnumerateArray().Select(u => u.GetProperty("line").GetInt32()).Order());

        // ESLint's problem is among the file's, and its fix comes first among the line's actions and is made by ESLint.
        JsonElement problem = Assert.Single((await Call(client, "diagnostics", new() { ["path"] = app })).EnumerateArray(),
            d => d.GetProperty("message").GetString()!.Contains("(prefer-const)", StringComparison.Ordinal));
        Assert.Equal(("error", 4), (problem.GetProperty("severity").GetString(), problem.GetProperty("line").GetInt32()));
        Dictionary<string, object?> let = At(script, app, "let t");
        string[] actions = Strings(await Call(client, "code_actions", let));
        Assert.True(actions.Length > 0 && actions[0].StartsWith("ESLint: Fix 'prefer-const'", StringComparison.Ordinal), $"ESLint's fix is not first among the line's actions: {string.Join(" | ", actions)}");
        Assert.Equal($"Applied 'ESLint: Fix 'prefer-const'' to {app}", (await Call(client, "apply_code_action", new(let) { ["title"] = "ESLint: Fix 'prefer-const'" })).GetString());
        Assert.Contains("const total = greet('Ada')", File.ReadAllText(Path.Combine(web, app)), StringComparison.Ordinal);

        string renamed = (await Call(client, "rename", new(At(script, app, "function gre")) { ["newName"] = "welcome" })).GetString()!;
        Assert.Equal($"Renamed in 1 file: {app}", renamed);
        string written = File.ReadAllText(Path.Combine(web, app));
        Assert.True(written.Contains("function welcome(name)", StringComparison.Ordinal) && written.Contains("welcome('Ada')", StringComparison.Ordinal),
            $"The rename did not reach the script on disk:\n{written}");

        // The project's Prettier formats what it covers, by its .prettierrc.
        Assert.StartsWith("Prettier: formatted ", (await Call(client, "format", new() { ["path"] = Path.Combine("src", "format.js") })).GetString(), StringComparison.Ordinal);
        Assert.Equal("const label = { name: 'tea', price: 3 }\nconsole.log(label)\n", File.ReadAllText(Path.Combine(web, "src", "format.js")));

        // CSS's server: a custom property's declaration from its use, and its rename through the stylesheet.
        string css = Path.Combine("styles", "site.css");
        string sheet = File.ReadAllText(Path.Combine(web, css));
        Assert.Equal(2, Assert.Single((await Call(client, "definition", At(sheet, css, "var(--acc"))).EnumerateArray()).GetProperty("line").GetInt32());
        await Call(client, "rename", new(At(sheet, css, "var(--acc")) { ["newName"] = "--brand" });
        Assert.Equal(":root {\n  --brand: #3574f0;\n}\n\n.cart {\n  color: var(--brand);\n}\n", File.ReadAllText(Path.Combine(web, css)));

        // JSON's server has no usages, and says so; a dependency leads to its installed package, and a script's rename
        // takes the scripts that run it along.
        string manifest = File.ReadAllText(Path.Combine(web, "package.json"));
        CallToolResult none = await Result(client, "usages", At(manifest, "package.json", "\"bui"));
        Assert.True(none.IsError == true && Text(none).Contains("JSON has no usages", StringComparison.Ordinal), $"usages in package.json answered: {Text(none)}");
        JsonElement package = Assert.Single((await Call(client, "definition", At(manifest, "package.json", "\"esl"))).EnumerateArray());
        Assert.Equal(Path.Combine("node_modules", "eslint", "package.json"), package.GetProperty("path").GetString());
        Assert.Equal("Renamed build to compile in package.json, and in 1 script that runs it",
            (await Call(client, "rename", new(At(manifest, "package.json", "\"bui")) { ["newName"] = "compile" })).GetString());
        manifest = File.ReadAllText(Path.Combine(web, "package.json"));
        Assert.True(manifest.Contains("\"compile\": \"node src/app.js\"", StringComparison.Ordinal) && manifest.Contains("\"all\": \"npm run compile\"", StringComparison.Ordinal),
            $"The script's rename did not reach package.json on disk:\n{manifest}");
    }

    /// <summary>A folder of the test's own, written (and restored) by <paramref name="write"/>, removed with the class.</summary>
    private string Made(string name, Action<string> write)
    {
        string made = PathNames.Canonical(TestFolders.Create(name));
        madeFolders.Add(made);
        TestFolders.Prepare(() => write(made), made);
        return made;
    }

    /// <summary>A tool's path, line and column (from 1) just after the first <paramref name="after"/> in a file's text.</summary>
    private static Dictionary<string, object?> At(string text, string path, string after)
    {
        int at = text.IndexOf(after, StringComparison.Ordinal);
        Assert.True(at >= 0, $"{path} has no {after}");
        at += after.Length;
        int line = text[..at].Count(c => c == '\n');
        int column = at - (text.LastIndexOf('\n', at - 1) + 1);
        return new() { ["path"] = path, ["line"] = line + 1, ["column"] = column + 1 };
    }

    private static string[] Strings(JsonElement list) => [.. list.EnumerateArray().Select(e => e.GetString()!)];
}
