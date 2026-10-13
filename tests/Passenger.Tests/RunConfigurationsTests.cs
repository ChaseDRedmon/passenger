using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Passenger.App;
using Passenger.App.Run;
using Passenger.Core;

namespace Passenger.Tests;

/// <summary>Run configurations as Rider's Edit Configurations makes them: one added in the dialog runs with what was
/// typed there and is there when the folder opens again; a generated one is changed by copying it, and stays as its
/// project makes it; one removed is gone for good. The dialog opens from the Run menu's action and the toolbar's chooser.</summary>
public sealed class RunConfigurationsTests : IDisposable
{
    private readonly string folder = TestFolders.Create("configs-");
    private readonly string settings = TestFolders.Create("settings-");
    private readonly string project;

    public RunConfigurationsTests()
    {
        Write("Hello/Hello.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <OutputType>Exe</OutputType>\n    <TargetFramework>net10.0</TargetFramework>\n    <ImplicitUsings>enable</ImplicitUsings>\n  </PropertyGroup>\n</Project>\n");
        Write("Hello/Program.cs",
            "Console.WriteLine($\"{Environment.GetEnvironmentVariable(\"GREETING\")} [{string.Join(\"|\", args)}] in {Environment.CurrentDirectory}\");\n");
        project = PathNames.Canonical(Path.Combine(folder, "Hello"));
        TestFolders.Prepare(() => Restore.Run(Path.Combine(folder, "Hello")), folder);
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

        Assert.Null(Ui.Await(window.RunAsync(), "the run to start", TimeSpan.FromMinutes(2)));
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
            Assert.Null(Ui.Await(window.RunAsync(), "the run to start", TimeSpan.FromMinutes(2)));
            Ui.WaitFor(() => window.Runner.Ended.IsCompleted, "the run to end");
            string ran = window.BottomPanel.Run.Document.Snapshot.ToString();
            Assert.True(ran.Contains("Hi [--name|Grace] in ", StringComparison.Ordinal), $"The copy did not run with its own arguments and the profile's variable:\n{ran}");
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

    private void Write(string relative, string text)
    {
        string path = Path.Combine(folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
