using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Passenger.App;
using Passenger.App.Run;
using Passenger.Core;

namespace Passenger.Tests;

/// <summary>Run configurations as Rider's Edit Configurations makes them: one added in the dialog runs with what was
/// typed there and is there when the folder opens again; a generated one is changed by copying it, and stays as its
/// project makes it; one removed is gone for good. The dialog opens from the Run menu's action and the toolbar's chooser.</summary>
public sealed class RunConfigurationsTests : IClassFixture<RestoredFolders>, IDisposable
{
    private readonly string folder;
    private readonly string settings = TestFolders.Create("settings-");
    private readonly string project;

    public RunConfigurationsTests(RestoredFolders folders)
    {
        string made = string.Empty;
        TestFolders.Prepare(() => made = folders.Take("configs-", root =>
        {
            Write(root, "Hello/Hello.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n</Project>\n");
            Write(root, "Hello/Program.cs",
                "Console.WriteLine($\"{Environment.GetEnvironmentVariable(\"GREETING\")} [{string.Join(\"|\", args)}] in {Environment.CurrentDirectory}\");\n");
            return Path.Combine(root, "Hello");
        }), settings);
        folder = made;
        project = PathNames.Canonical(Path.Combine(folder, "Hello"));
    }

    public void Dispose()
    {
        TestFolders.Delete(folder);
        TestFolders.Delete(settings);
    }

    [AvaloniaFact]
    public void A_configuration_added_in_the_dialog_runs_with_what_was_typed_and_is_there_when_the_folder_opens_again()
    {
        MainWindow window = Keeping();
        Assert.Equal(["Hello"], Names(window));

        // The toolbar's chooser lists the configurations, and Edit Configurations below them opens the dialog.
        Click(window.FindControl<Button>("RunConfigButton")!);
        ContextMenu chooser = Assert.IsType<ContextMenu>(window.OpenContextMenu);
        Assert.Equal(["Hello", "Edit Configurations..."], chooser.Items.OfType<MenuItem>().Select(i => (string?)i.Header));
        Click(chooser.Items.OfType<MenuItem>().Last());
        chooser.Close();
        RunConfigurationsDialog dialog = Assert.IsType<RunConfigurationsDialog>(window.RunConfigurationsDialog);
        Assert.Equal("Hello", dialog.Chosen?.Name);

        Click(dialog.AddButton);
        Assert.Equal("Unnamed", dialog.Chosen?.Name);
        Assert.True(dialog.NameBox.IsEnabled, "A configuration just added cannot be named");
        Assert.True(dialog.NameBox.IsFocused, "A configuration just added does not have its name ready to type");
        dialog.NameBox.SelectAll();
        dialog.KeyTextInput("Greeting");
        Ui.Settle();
        Type(dialog.ArgumentsBox, "one \"two three\"");
        Type(dialog.WorkingDirectoryBox, "$(ProjectDir)");
        Click(dialog.AddVariableButton);
        (TextBox name, TextBox value) = Assert.Single(dialog.Variables);
        Type(name, "GREETING");
        Type(value, "Hi");
        Assert.Equal(["Hello", "Greeting"], dialog.Rows.Select(r => r.Name));
        Click(dialog.OkButton);
        Ui.Settle();
        Assert.Null(window.RunConfigurationsDialog);
        Assert.Equal(["Hello", "Greeting"], Names(window));
        Assert.Equal("Greeting", window.Configuration?.Name);

        Ui.Launched(window, window.RunAsync(), "the run to start", TimeSpan.FromMinutes(2));
        Ui.WaitFor(() => window.Runner.Ended.IsCompleted, "the run to end");
        string ran = window.BottomPanel.Run.Document.Snapshot.ToString();
        Assert.True(ran.Contains($"Hi [one|two three] in {project}", StringComparison.Ordinal),
            $"The program did not get the arguments, variable and folder typed into the dialog:\n{ran}");
        window.Close();

        MainWindow again = Keeping();
        try
        {
            Assert.Equal(["Hello", "Greeting"], Names(again));
            Assert.Equal("Greeting", again.Configuration?.Name);
            RunConfiguration kept = again.Configurations[1];
            Assert.Equal(Path.Combine(project, "Hello.csproj"), kept.Project);
            Assert.Equal("one \"two three\"", kept.Arguments);
            Assert.Equal(new Dictionary<string, string> { ["GREETING"] = "Hi" }, kept.Environment);
            Assert.Equal("$(ProjectDir)", kept.WorkingDirectory);
            Assert.False(kept.Generated);
        }
        finally
        {
            again.Close();
        }
    }

    [AvaloniaFact]
    public void A_generated_configuration_is_changed_by_copying_it_and_stays_as_its_launch_profile_makes_it()
    {
        Write("Hello/Properties/launchSettings.json",
            "{ \"profiles\": { \"Greet\": { \"commandName\": \"Project\", \"commandLineArgs\": \"--name Ada\", \"environmentVariables\": { \"GREETING\": \"Hi\" } } } }");
        MainWindow window = Keeping();
        try
        {
            Assert.Equal(["Hello: Greet"], Names(window));
            Assert.True(window.Perform(AppActions.EditConfigurations));
            RunConfigurationsDialog dialog = Assert.IsType<RunConfigurationsDialog>(window.RunConfigurationsDialog);
            Assert.Equal("Hello: Greet", dialog.Chosen?.Name);
            Assert.True(dialog.Chosen!.Generated);
            Assert.False(dialog.ArgumentsBox.IsEnabled, "A generated configuration's arguments can be changed in place, though its launch profile makes them");
            Assert.False(dialog.RemoveButton.IsEnabled, "A generated configuration can be removed, though its project makes it again");
            Assert.Equal("--name Ada", dialog.ArgumentsBox.Text);
            Assert.Equal("Greet", dialog.ProfileBox.SelectedItem);

            Click(dialog.CopyButton);
            Assert.Equal("Hello: Greet (1)", dialog.Chosen?.Name);
            Assert.False(dialog.Chosen!.Generated);
            Assert.True(dialog.ArgumentsBox.IsEnabled, "The copy of a generated configuration cannot be changed");
            Assert.Equal("--name Ada", dialog.ArgumentsBox.Text);
            Type(dialog.ArgumentsBox, "--name Grace");
            Click(dialog.OkButton);
            Ui.Settle();

            Assert.Equal(["Hello: Greet", "Hello: Greet (1)"], Names(window));
            Assert.Equal("--name Ada", window.Configurations[0].Arguments);
            Assert.Equal("Hello: Greet (1)", window.Configuration?.Name);
            Ui.Launched(window, window.RunAsync(), "the run to start", TimeSpan.FromMinutes(2));
            Ui.WaitFor(() => window.Runner.Ended.IsCompleted, "the run to end");
            string ran = window.BottomPanel.Run.Document.Snapshot.ToString();
            Assert.True(ran.Contains("Hi [--name|Grace] in ", StringComparison.Ordinal), $"The copy did not run with its own arguments and the profile's variable:\n{ran}");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Rider's Use external console, chosen in the dialog for a copy of the generated configuration (whose own stays
    /// the Run window's), runs the program in a tab of the Terminal window named for the configuration, and is still chosen
    /// when the folder opens again.</summary>
    [AvaloniaFact]
    public void Use_external_console_chosen_in_the_dialog_runs_the_program_in_the_terminal_and_is_kept_with_the_configuration()
    {
        MainWindow window = Keeping();
        RunConfigurationsDialog dialog = window.ShowRunConfigurations();
        Assert.Equal("Hello", dialog.Chosen?.Name);
        Assert.False(dialog.ConsoleBox.IsEnabled, "A generated configuration's console can be changed in place, though its project makes it");
        Assert.False(dialog.ConsoleBox.IsChecked == true, "A generated configuration runs in the terminal");
        Click(dialog.CopyButton);
        Assert.True(dialog.ConsoleBox.IsEnabled, "The copy of a generated configuration cannot choose its console");
        Type(dialog.NameBox, "Hello in the terminal");
        Type(dialog.ArgumentsBox, "one");
        dialog.ConsoleBox.IsChecked = true;
        Ui.Settle();
        Click(dialog.OkButton);
        Ui.Settle();
        Assert.Equal(ConsoleKind.Internal, window.Configurations[0].Console);
        Assert.Equal(ConsoleKind.Terminal, window.Configuration?.Console);

        Ui.Launched(window, window.RunAsync(), "the run to start", TimeSpan.FromMinutes(2));
        TerminalRun run = Assert.IsType<TerminalRun>(window.RunInTerminal);
        Assert.Equal("Hello in the terminal", window.TerminalPanel.ActiveTab?.Name);
        Ui.WaitFor(() => run.Ended.IsCompleted, "the run to end");
        // Rows the terminal wrapped joined again, as a copy of them reads.
        string shown = string.Concat(run.Session.Screen.Scrollback.Concat(run.Session.Screen.Lines).Select(l => l.Text() + (l.Wrapped ? string.Empty : "\n")));
        Assert.True(shown.Contains($" [one] in {project}", StringComparison.Ordinal) && shown.Contains("Process finished with exit code 0.", StringComparison.Ordinal),
            $"The program did not run in the terminal with the configuration's arguments and folder:\n{shown}");
        Assert.Equal("Hello in the terminal exited with code 0", window.Status);
        window.Close();

        MainWindow again = Keeping();
        try
        {
            Assert.Equal(ConsoleKind.Terminal, again.Configuration?.Console);
            RunConfigurationsDialog reopened = again.ShowRunConfigurations();
            Assert.Equal("Hello in the terminal", reopened.Chosen?.Name);
            Assert.True(reopened.ConsoleBox.IsChecked == true, "Use external console was not kept with the configuration");
            reopened.Close();
        }
        finally
        {
            again.Close();
        }
    }

    /// <summary>Rider's Open browser, After launch: a launch profile's launchBrowser shows ticked with the URL its launchUrl
    /// and applicationUrl make, and opens that URL once the program listens; turned off in a copy, the copy opens none.</summary>
    [AvaloniaFact]
    public void A_launch_profiles_browser_shows_under_open_browser_and_a_copy_with_it_turned_off_opens_none()
    {
        System.Net.Sockets.TcpListener probe = new(System.Net.IPAddress.Loopback, 0);
        probe.Start();
        int port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        Write("Hello/Program.cs", "using System.Net;\nusing System.Net.Sockets;\n\n"
            + "TcpListener listener = new(IPAddress.Loopback, int.Parse(Environment.GetEnvironmentVariable(\"PORT\")!));\n"
            + "listener.Start();\nConsole.WriteLine(\"Listening\");\nThread.Sleep(4000);\n");
        Write("Hello/Properties/launchSettings.json", "{ \"profiles\": { \"Serve\": { \"commandName\": \"Project\", \"launchBrowser\": true, \"launchUrl\": \"status\", "
            + $"\"applicationUrl\": \"http://127.0.0.1:{port}\", \"environmentVariables\": {{ \"PORT\": \"{port}\" }} }} }} }}");
        MainWindow window = Keeping();
        try
        {
            RunConfigurationsDialog dialog = window.ShowRunConfigurations();
            Assert.Equal("Hello: Serve", dialog.Chosen?.Name);
            Assert.True(dialog.BrowserBox.IsChecked == true, "The launch profile's launchBrowser does not show as Open browser after launch");
            Assert.Equal($"http://127.0.0.1:{port}/status", dialog.BrowserUrlBox.Text);
            Assert.False(dialog.BrowserBox.IsEnabled, "A generated configuration's browser can be changed in place, though its launch profile makes it");
            Click(dialog.CopyButton);
            Assert.True(dialog.BrowserBox.IsEnabled, "The copy of a generated configuration cannot turn its browser off");
            dialog.BrowserBox.IsChecked = false;
            Ui.Settle();
            Click(dialog.OkButton);
            Ui.Settle();
            Assert.Equal("Hello: Serve (1)", window.Configuration?.Name);
            Assert.False(window.Configuration!.LaunchBrowser);

            Ui.Launched(window, window.RunAsync(), "the copy's run to start", TimeSpan.FromMinutes(2));
            Ui.WaitFor(() => window.Runner.Ended.IsCompleted, "the copy's run to end");
            Assert.Contains("Listening", window.BottomPanel.Run.Document.Snapshot.ToString(), StringComparison.Ordinal);
            Assert.Empty(window.Browsed);

            Assert.True(window.SelectConfiguration("Hello: Serve"));
            Ui.Launched(window, window.RunAsync(), "the profile's run to start", TimeSpan.FromMinutes(2));
            Ui.WaitFor(() => window.Browsed.Count > 0, "the browser to open once the program listens", TimeSpan.FromSeconds(60));
            Assert.Equal([$"http://127.0.0.1:{port}/status"], window.Browsed.Select(u => u.AbsoluteUri));
            Ui.WaitFor(() => window.Runner.Ended.IsCompleted, "the profile's run to end");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_configuration_removed_in_the_dialog_is_gone_from_the_toolbar_and_the_folder()
    {
        MainWindow window = Keeping();
        window.SetConfigurations(
        [
            new RunConfiguration { Name = "First", Project = Path.Combine(project, "Hello.csproj"), Arguments = "1" },
            new RunConfiguration { Name = "Second", Project = Path.Combine(project, "Hello.csproj"), Arguments = "2" },
        ], "Second");
        Assert.Equal(["Hello", "First", "Second"], Names(window));

        RunConfigurationsDialog dialog = window.ShowRunConfigurations();
        Assert.Equal("Second", dialog.Chosen?.Name);
        Assert.True(dialog.Choose("First"));
        Click(dialog.RemoveButton);
        Assert.Equal(["Hello", "Second"], dialog.Rows.Select(r => r.Name));
        Assert.Equal("Second", dialog.Chosen?.Name);
        Click(dialog.OkButton);
        Ui.Settle();
        Assert.Equal(["Hello", "Second"], Names(window));
        window.Close();

        MainWindow again = Keeping();
        try
        {
            Assert.Equal(["Hello", "Second"], Names(again));
        }
        finally
        {
            again.Close();
        }
    }

    [AvaloniaFact]
    public void A_name_taken_or_missing_is_refused_with_why_and_nothing_changes()
    {
        MainWindow window = Keeping();
        try
        {
            RunConfigurationsDialog dialog = window.ShowRunConfigurations();
            Click(dialog.AddButton);
            Type(dialog.NameBox, "Hello");
            Click(dialog.OkButton);
            Assert.Equal("Two configurations are named Hello", dialog.Problem);
            Assert.Same(dialog, window.RunConfigurationsDialog);
            Assert.Equal(["Hello"], Names(window));
            Type(dialog.NameBox, string.Empty);
            Click(dialog.OkButton);
            Assert.Equal("A configuration needs a name", dialog.Problem);
            dialog.Close();
            Ui.Settle();
            Assert.Equal(["Hello"], Names(window));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>A name longer than the list is wide ends in an ellipsis inside its row, as the other narrowing rows do,
    /// where it had been cut mid-letter at the list's edge.</summary>
    [AvaloniaFact]
    public void A_configuration_named_longer_than_the_list_is_wide_ends_in_an_ellipsis_inside_its_row()
    {
        const string Long = "TopLevelStatementsConsoleApp project, named at more length than the list has room for";
        MainWindow window = Keeping();
        try
        {
            RunConfigurationsDialog dialog = window.ShowRunConfigurations();
            Click(dialog.AddButton);
            Type(dialog.NameBox, Long);
            Click(dialog.OkButton);
            dialog = window.ShowRunConfigurations();
            Ui.Settle();
            ListBox list = dialog.GetVisualDescendants().OfType<ListBox>().First();
            TextBlock name = list.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == Long && t.IsEffectivelyVisible);
            ListBoxItem row = name.FindAncestorOfType<ListBoxItem>()!;
            double right = Avalonia.VisualExtensions.TranslatePoint(name, new Avalonia.Point(name.Bounds.Width, 0), row)!.Value.X;
            Assert.True(right <= row.Bounds.Width + 0.5 && name.TextTrimming == Avalonia.Media.TextTrimming.CharacterEllipsis,
                $"'{Long}' runs {right:0.#} px into a row {row.Bounds.Width:0.#} px wide, cut at its edge rather than ending in an ellipsis");
            dialog.Close();
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>A window that keeps its state in the tests' own store, on the folder.</summary>
    private MainWindow Keeping()
    {
        MainWindow window = new() { Width = 1200, Height = 800, StateFolder = settings };
        window.Show();
        window.KeepLayout();
        window.OpenFolder(folder);
        Ui.Settle();
        return window;
    }

    private static string[] Names(MainWindow window) => [.. window.Configurations.Select(c => c.Name)];

    /// <summary>A field's text replaced, as typing over it does, and the dialog having heard it.</summary>
    private static void Type(TextBox box, string text)
    {
        box.Text = text;
        Ui.Settle();
    }

    private static void Click(Control control)
    {
        control.RaiseEvent(new RoutedEventArgs(control is MenuItem ? MenuItem.ClickEvent : Button.ClickEvent));
        Ui.Settle();
    }

    private void Write(string relative, string text) => Write(folder, relative, text);

    private static void Write(string folder, string relative, string text)
    {
        string path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
