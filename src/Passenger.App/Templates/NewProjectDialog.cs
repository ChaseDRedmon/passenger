using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Passenger.Core;
using Passenger.Editor;

namespace Passenger.App.Templates;

/// <summary>Where Add New Project puts what it makes: the open solution, the solution folder chosen (by its path in the
/// solution, null for the solution's top) and the names of the projects it has, which a new one cannot take.</summary>
public sealed record ProjectTarget(string Solution, string? Folder, IReadOnlyList<string> Projects);

/// <summary>What Create made, canonical: New Solution's solution (null for Add New Project's) and the projects.</summary>
public sealed record CreatedProjects(string? Solution, IReadOnlyList<string> Projects);

/// <summary>Rider's New Project dialog, for New Solution and Add, New Project: dotnet new's templates down the left; on the
/// right the names, where it goes (for a solution, whether the project shares its folder, and its format), the target
/// framework, the language where there is more than C#, and the template's own options. Create refuses a name taken or a
/// folder not empty, saying why, before anything is made; otherwise it runs dotnet new and closes with what it made
/// (<see cref="Created"/>). What a Create that failed or was canceled made is removed.</summary>
public sealed class NewProjectDialog : Window
{
    private const double LabelWidth = 170;

    private readonly ProjectTarget? target;
    private readonly ListBox list;
    private readonly TextBlock loading;
    private readonly TextBlock heading;
    private readonly TextBlock description;
    private readonly TextBox solutionName = new() { Classes = { "search" } };
    private readonly TextBox projectName = new() { Classes = { "search" } };
    private readonly TextBox directory = new() { Classes = { "search" } };
    private readonly CheckBox sameDirectory = new() { Content = "Put solution and project in the same directory" };
    private readonly ComboBox format = new() { MinWidth = 160 };
    private readonly TextBlock where = new() { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox framework = new() { MinWidth = 160 };
    private readonly ComboBox language = new() { MinWidth = 160 };
    private readonly Control languageRow;
    private readonly StackPanel options = new() { Spacing = 10 };
    private readonly TextBlock command = new() { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly Control details;
    private readonly ProgressBar bar = new() { IsIndeterminate = true, Height = 4, MinHeight = 4, Width = 160, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock progress = new() { Classes = { "muted" }, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel working;
    private readonly TextBlock problem = new() { TextWrapping = TextWrapping.Wrap, IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button create = new() { Content = "Create", IsDefault = true, Classes = { "primary" }, MinWidth = 80, IsEnabled = false };
    private readonly Button browse = new() { Content = "Browse...", Margin = new Thickness(8, 0, 0, 0) };

    /// <summary>The shown options' values, and those the person set, which another template with the option keeps.</summary>
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> byPerson = new(StringComparer.Ordinal);
    private readonly List<(TemplateOption Option, Control Row, Control Editor)> shown = [];
    private TemplateCatalog? catalog;
    private ProjectTemplate? template;
    private CancellationTokenSource? creating;

    /// <summary>The names the dialog gave last: one that differs is the person's, which a template chosen later keeps.</summary>
    private string solutionGiven = string.Empty;
    private string projectGiven = string.Empty;

    /// <summary>Set while the dialog fills its lists, so their choice is not taken for the person's.</summary>
    private bool setting;

    /// <param name="folder">The directory offered: where a solution goes, or the project's folder in the solution.</param>
    /// <param name="target">The solution a project is added to; null for New Solution.</param>
    public NewProjectDialog(string folder, ProjectTarget? target)
    {
        this.target = target;
        Title = target is null ? "New Solution" : "New Project";
        Width = 920;
        Height = 640;
        MinWidth = 720;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Bind(BackgroundProperty, this.GetResourceObservable("Passenger.Chrome"));

        list = new ListBox
        {
            Classes = { "tree" },
            ItemTemplate = new FuncDataTemplate<ProjectTemplate?>((row, _) => row is null ? new Panel() : new TextBlock
            {
                Text = row.Name,
                Padding = new Thickness(10, 6),
                TextTrimming = TextTrimming.CharacterEllipsis,
            }, supportsRecycling: false),
        };
        list.SelectionChanged += (_, _) => Choose(list.SelectedItem as ProjectTemplate);
        loading = new TextBlock { Classes = { "muted" }, Text = "Loading templates...", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12) };
        Border left = new() { Width = 260, BorderThickness = new Thickness(0, 0, 1, 0), Child = new Panel { Children = { list, loading } } };
        left.Bind(Border.BorderBrushProperty, left.GetResourceObservable("Passenger.Divider"));

        heading = new TextBlock { FontSize = 16, FontWeight = FontWeight.SemiBold };
        description = new TextBlock { Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        directory.Text = folder;
        browse.Click += (_, _) => _ = BrowseAsync();
        DockPanel folderRow = new();
        DockPanel.SetDock(browse, Dock.Right);
        folderRow.Children.Add(browse);
        folderRow.Children.Add(directory);
        solutionName.TextChanged += (_, _) => NameChanged(solution: true);
        projectName.TextChanged += (_, _) => NameChanged(solution: false);
        directory.TextChanged += (_, _) => ShowWhere();
        sameDirectory.IsCheckedChanged += (_, _) => ShowWhere();
        format.SelectionChanged += (_, _) => ShowWhere();
        framework.SelectionChanged += (_, _) => Retarget();
        language.SelectionChanged += (_, _) => Retarget();

        StackPanel fields = new() { Spacing = 10 };
        if (target is null)
        {
            fields.Children.Add(Row("Solution name:", solutionName));
            fields.Children.Add(Row("Project name:", projectName));
            fields.Children.Add(Row("Solution directory:", folderRow));
            fields.Children.Add(Row(null, sameDirectory));
            fields.Children.Add(Row("Solution format:", format));
        }
        else
        {
            fields.Children.Add(Row("Project name:", projectName));
            fields.Children.Add(Row("Project directory:", folderRow));
        }
        fields.Children.Add(Row(null, where));
        languageRow = Row("Language:", language);
        Border rule = new() { Height = 1, Margin = new Thickness(0, 6) };
        rule.Bind(Border.BackgroundProperty, rule.GetResourceObservable("Passenger.Divider"));
        details = new StackPanel
        {
            Spacing = 10,
            Children = { rule, Row("Target framework:", framework), languageRow, options, Row(null, command) },
        };
        StackPanel right = new()
        {
            Margin = new Thickness(24, 18, 24, 18),
            Spacing = 10,
            Children = { new StackPanel { Children = { heading, description }, Margin = new Thickness(0, 0, 0, 8) }, fields, details },
        };
        Border rightPane = new() { Child = new ScrollViewer { Content = right } };
        rightPane.Bind(Border.BackgroundProperty, rightPane.GetResourceObservable("Passenger.Editor.Background"));

        problem.Bind(TextBlock.ForegroundProperty, problem.GetResourceObservable("Passenger.Editor.Error"));
        working = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, IsVisible = false, Children = { bar, progress } };
        Button cancel = new() { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        cancel.Click += (_, _) => Cancel();
        create.Click += (_, _) => _ = CreateAsync();
        Closing += (_, _) => creating?.Cancel();
        StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancel, create } };
        DockPanel bottomRow = new() { Margin = new Thickness(16, 10) };
        DockPanel.SetDock(buttons, Dock.Right);
        bottomRow.Children.Add(buttons);
        bottomRow.Children.Add(new Panel { Margin = new Thickness(0, 0, 16, 0), Children = { problem, working } });
        Border bottom = new() { BorderThickness = new Thickness(0, 1, 0, 0), Child = bottomRow };
        bottom.Bind(Border.BorderBrushProperty, bottom.GetResourceObservable("Passenger.Divider"));

        DockPanel root = new();
        DockPanel.SetDock(bottom, Dock.Bottom);
        DockPanel.SetDock(left, Dock.Left);
        root.Children.Add(bottom);
        root.Children.Add(left);
        root.Children.Add(rightPane);
        Content = root;
        ShowTemplate(null);
        TemplatesRead = LoadAsync(target is null ? folder : Path.GetDirectoryName(target.Solution)!);
        // The name has the keys as the dialog opens, its default chosen to be typed over; again once laid out.
        void FocusName()
        {
            TextBox first = target is null ? solutionName : projectName;
            first.Focus();
            first.SelectAll();
        }
        Opened += (_, _) =>
        {
            FocusName();
            Dispatcher.UIThread.Post(FocusName, DispatcherPriority.Loaded);
        };
    }

    /// <summary>The templates' read, done once they are listed or the dialog says why they could not be.</summary>
    public Task TemplatesRead { get; }

    /// <summary>The templates listed, in Rider's order.</summary>
    public IReadOnlyList<ProjectTemplate> Templates => catalog?.Templates ?? [];

    /// <summary>The template chosen.</summary>
    public ProjectTemplate? Chosen
    {
        get => template;
        set => list.SelectedItem = value;
    }

    /// <summary>The template list's row for one, for a click on it; null when it is not listed.</summary>
    public Control? TemplateRow(ProjectTemplate row)
    {
        int at = Templates.ToList().IndexOf(row);
        if (at < 0)
        {
            return null;
        }
        list.ScrollIntoView(at);
        list.UpdateLayout();
        return list.ContainerFromIndex(at);
    }

    public string SolutionName
    {
        get => solutionName.Text?.Trim() ?? string.Empty;
        set => solutionName.Text = value;
    }

    public string ProjectName
    {
        get => projectName.Text?.Trim() ?? string.Empty;
        set => projectName.Text = value;
    }

    /// <summary>The directory the solution's folder (or the project's) is made in.</summary>
    public string Folder
    {
        get => directory.Text?.Trim() ?? string.Empty;
        set => directory.Text = value;
    }

    public bool SameDirectory
    {
        get => sameDirectory.IsChecked == true;
        set => sameDirectory.IsChecked = value;
    }

    /// <summary>The solution's format: "slnx" or "sln", as dotnet new sln takes it.</summary>
    public string SolutionFormat => format.SelectedItem as string is { } chosen ? chosen.TrimStart('.') : catalog?.SolutionFormat ?? "sln";

    public string? Framework => framework.SelectedItem as string;

    public string? Language => language.SelectedItem as string;

    /// <summary>The labels of the template's options shown now.</summary>
    public IReadOnlyList<string> OptionLabels => [.. shown.Select(s => s.Option.Label)];

    /// <summary>The control that sets an option, by its label, for a click; null when none is shown.</summary>
    public Control? OptionEditor(string label) => shown.FirstOrDefault(s => s.Option.Label == label).Editor;

    /// <summary>The command line Create runs for the project, as the dialog shows it.</summary>
    public string Command => command.Text ?? string.Empty;

    /// <summary>Why the last Create was refused or failed, or why the templates could not be read; null when neither.</summary>
    public string? Problem => problem.IsVisible ? problem.Text : null;

    public bool IsCreating => creating is not null;

    private bool SolutionNamed => SolutionName.Length > 0 && SolutionName != solutionGiven;

    private bool ProjectNamed => ProjectName.Length > 0 && ProjectName != projectGiven;

    /// <summary>The Create button, for a click on it.</summary>
    public Button CreateButton => create;

    /// <summary>What Create made, once it closed the dialog; null until then, and when given up.</summary>
    public CreatedProjects? Created { get; private set; }

    /// <summary>Create, as its button does: what it makes, or why not, the dialog closing once it is made.</summary>
    public async Task CreateAsync()
    {
        if (creating is not null || template is null)
        {
            return;
        }
        if (WhyNot() is { } refused)
        {
            Fail(refused);
            return;
        }
        Plan plan = Planned();
        bool existed = Directory.Exists(plan.Root);
        using CancellationTokenSource cancel = new();
        creating = cancel;
        Fail(null);
        Busy(true);
        try
        {
            Directory.CreateDirectory(plan.Root);
            if (plan.Solution is not null)
            {
                progress.Text = $"Creating the solution {SolutionName}...";
                ChildResult solution = await RunAsync(["new", "sln", "--name", SolutionName, "--output", plan.Root, .. FormatArguments()], plan.Root, cancel.Token);
                if (!solution.Succeeded)
                {
                    await FailedAsync($"The solution could not be created: {ProjectTemplates.Said(solution)}", plan, existed);
                    return;
                }
            }
            progress.Text = $"Creating {ProjectName} from {template.Name} (dotnet new {template.ShortName})...";
            ChildResult made = await RunAsync(Arguments(plan.ProjectFolder), plan.Root, cancel.Token);
            if (!made.Succeeded)
            {
                await FailedAsync($"{ProjectName} could not be created: {ProjectTemplates.Said(made)}", plan, existed);
                return;
            }
            List<string> projects = ProjectsIn(plan.ProjectFolder);
            if (projects.Count == 0)
            {
                await FailedAsync($"dotnet new {template.ShortName} made no project in {plan.ProjectFolder}", plan, existed);
                return;
            }
            Created = new CreatedProjects(plan.Solution is null ? null : PathNames.Canonical(plan.Solution), projects);
            creating = null;
            Close(Created);
        }
        catch (OperationCanceledException)
        {
            // Stopped: dotnet new is ended, and what it made goes.
            await RemoveAsync(plan, existed);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or TimeoutException)
        {
            await FailedAsync($"{ProjectName} could not be created: {error.Message}", plan, existed);
        }
        finally
        {
            creating = null;
            Busy(false);
        }
    }

    /// <summary>Cancel: stops a Create under way, removing what it made, and closes the dialog.</summary>
    public void Cancel()
    {
        creating?.Cancel();
        Close(null);
    }

    /// <summary>Where Create puts things: the folder it makes (the solution's, or the project's), the solution file, and
    /// the project's folder.</summary>
    private sealed record Plan(string Root, string? Solution, string ProjectFolder);

    private Plan Planned()
    {
        string folder = Path.GetFullPath(Folder);
        if (target is not null)
        {
            string project = Path.Combine(folder, ProjectName);
            return new Plan(project, null, project);
        }
        string root = Path.Combine(folder, SolutionName);
        return new Plan(root, Path.Combine(root, $"{SolutionName}.{SolutionFormat}"), SameDirectory ? root : Path.Combine(root, ProjectName));
    }

    /// <summary>Why Create would make nothing, or null: Rider's reasons for a name, then where it goes, then the options.</summary>
    private string? WhyNot()
    {
        if (template is null)
        {
            return "Choose a template";
        }
        if (target is null && NameProblem(SolutionName, "Solution") is { } solution)
        {
            return solution;
        }
        if (NameProblem(ProjectName, "Project") is { } project)
        {
            return project;
        }
        if (Folder.Length == 0)
        {
            return "A directory is needed";
        }
        if (!Path.IsPathFullyQualified(Folder))
        {
            return $"{Folder} is not a full path";
        }
        if (target is not null && target.Projects.Contains(ProjectName, StringComparer.OrdinalIgnoreCase))
        {
            return $"Project {ProjectName} already exists in the solution";
        }
        string root = Planned().Root;
        string what = target is null ? $"Solution {SolutionName}" : $"Project {ProjectName}";
        if (File.Exists(root))
        {
            return $"{what} cannot be created: {root} is a file";
        }
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            return $"{what} already exists in this location: {root} is not empty";
        }
        foreach ((TemplateOption option, _, _) in shown.Where(s => s.Editor.IsEnabled))
        {
            string value = values.GetValueOrDefault(option.Name, option.Default);
            if (option.Required && value.Length == 0)
            {
                return $"{option.Label} is needed";
            }
            if (option.Kind == TemplateOptionKind.Integer && value.Length > 0 && !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                return $"{option.Label}: please enter an integer number";
            }
            if (option.Kind == TemplateOptionKind.Float && value.Length > 0 && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                return $"{option.Label}: please enter a float number";
            }
        }
        return null;
    }

    /// <summary>Rider's reasons a solution or project name cannot be one: none, or characters no file name takes.</summary>
    private static string? NameProblem(string name, string what) =>
        name.Length == 0 ? $"{what} name cannot be empty"
        : name is "." or ".." || name.IndexOfAny([.. Path.GetInvalidFileNameChars(), '/', '\\', ':', '*', '?', '"', '<', '>', '|']) >= 0 ? $"{what} name contains invalid characters"
        : null;

    /// <summary>The project's dotnet new command line: its template by short name, name and folder (none in the line the
    /// dialog shows), language, framework, and each option enabled and changed from its default.</summary>
    private List<string> Arguments(string? output)
    {
        TemplateVariant variant = Variant();
        List<string> arguments = ["new", template!.ShortName, "--name", ProjectName];
        if (output is not null)
        {
            arguments.AddRange(["--output", output, "--no-update-check"]);
        }
        if (variant.Language is { } chosen)
        {
            arguments.AddRange(["--language", chosen]);
        }
        if (variant.FrameworkOption is { } option && Framework is { } target)
        {
            arguments.AddRange([$"--{option}", target]);
        }
        foreach ((TemplateOption each, _, Control editor) in shown)
        {
            if (editor.IsEnabled && values.TryGetValue(each.Name, out string? value) && !string.Equals(value, each.Default, StringComparison.OrdinalIgnoreCase))
            {
                arguments.AddRange([$"--{each.Option}", value]);
            }
        }
        return arguments;
    }

    private IEnumerable<string> FormatArguments() =>
        catalog is { SolutionFormats.Count: > 1 } ? ["--format", SolutionFormat] : [];

    private TemplateVariant Variant() => template!.Variant(Language, Framework);

    private static Task<ChildResult> RunAsync(IEnumerable<string> arguments, string folder, CancellationToken cancel)
    {
        Task<ChildResult> run = ChildProcesses.RunAsync(ChildProcesses.DotnetCommand(arguments, folder), limit: TimeSpan.FromMinutes(10), cancel: cancel);
        BackgroundWork.Track(run);
        return run;
    }

    /// <summary>The project files a template made, the one of its name first, outside build output.</summary>
    private List<string> ProjectsIn(string folder) =>
        [.. Directory.EnumerateFiles(folder, "*.*proj", SearchOption.AllDirectories)
            .Where(p => Path.GetExtension(p).ToLowerInvariant() is ".csproj" or ".fsproj" or ".vbproj")
            .Where(p => !Path.GetRelativePath(folder, p).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .OrderBy(p => Path.GetFileNameWithoutExtension(p).Equals(ProjectName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p, StringComparer.Ordinal)
            .Select(PathNames.Canonical)];

    private async Task FailedAsync(string why, Plan plan, bool existed)
    {
        await RemoveAsync(plan, existed);
        Fail(why);
    }

    /// <summary>What a Create that failed or was stopped made: the folder it made, or what it put in one that was there and
    /// empty.</summary>
    private static Task RemoveAsync(Plan plan, bool existed) => BackgroundWork.Run(() =>
    {
        try
        {
            if (!Directory.Exists(plan.Root))
            {
                return;
            }
            if (!existed)
            {
                Directory.Delete(plan.Root, recursive: true);
                return;
            }
            foreach (string entry in Directory.EnumerateFileSystemEntries(plan.Root))
            {
                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, recursive: true);
                }
                else
                {
                    File.Delete(entry);
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            IdeErrors.Write($"A new project that could not be made left {plan.Root}: {error.Message}");
        }
    });

    private async Task LoadAsync(string folder)
    {
        try
        {
            catalog = await BackgroundWork.Run(() => ProjectTemplates.LoadAsync(folder));
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException or JsonException
            or TimeoutException or KeyNotFoundException or System.ComponentModel.Win32Exception)
        {
            loading.Text = $"The templates could not be read: {error.Message}";
            Fail(loading.Text);
            return;
        }
        loading.IsVisible = catalog.Templates.Count == 0;
        loading.Text = "dotnet new has no project templates installed";
        setting = true;
        format.ItemsSource = catalog.SolutionFormats.Select(f => $".{f}").ToList();
        format.SelectedIndex = 0;
        setting = false;
        list.ItemsSource = catalog.Templates;
        list.SelectedIndex = catalog.Templates.Count > 0 ? 0 : -1;
    }

    /// <summary>A template chosen: its names (unless the person typed their own), languages, frameworks and options.</summary>
    private void Choose(ProjectTemplate? chosen)
    {
        if (ReferenceEquals(chosen, template))
        {
            return;
        }
        template = chosen;
        ShowTemplate(chosen);
        if (chosen is null)
        {
            return;
        }
        setting = true;
        language.ItemsSource = chosen.Languages;
        language.SelectedIndex = chosen.Languages.Count > 0 ? 0 : -1;
        setting = false;
        if (target is null)
        {
            if (!SolutionNamed)
            {
                Set(solutionName, Unique(chosen.DefaultName, name => !Directory.Exists(Path.Combine(Folder, name)) && !File.Exists(Path.Combine(Folder, name))));
            }
            if (!ProjectNamed)
            {
                Set(projectName, SolutionName);
            }
        }
        else if (!ProjectNamed)
        {
            Set(projectName, Unique(chosen.DefaultName, name => !target.Projects.Contains(name, StringComparer.OrdinalIgnoreCase) && !Directory.Exists(Path.Combine(Folder, name))));
        }
        Retarget();
    }

    /// <summary>The frameworks of the language chosen, and the options of the template dotnet new makes for them.</summary>
    private void Retarget()
    {
        if (setting || template is null)
        {
            return;
        }
        setting = true;
        IReadOnlyList<string> frameworks = template.Frameworks(Language);
        string? was = Framework;
        framework.ItemsSource = frameworks;
        TemplateVariant newest = template.Variant(Language, null);
        framework.SelectedItem = was is not null && frameworks.Contains(was) ? was : newest.DefaultFramework ?? frameworks.FirstOrDefault();
        setting = false;
        framework.IsEnabled = frameworks.Count > 1;
        languageRow.IsVisible = template.Languages.Count > 1;
        heading.Text = template.Name;
        description.Text = Variant().Description;
        ShowOptions(Variant());
        ShowWhere();
    }

    /// <summary>The template's options, each as Rider shows one: a checkbox, a list of its choices, or a box for text.</summary>
    private void ShowOptions(TemplateVariant variant)
    {
        options.Children.Clear();
        shown.Clear();
        values.Clear();
        foreach (TemplateOption option in variant.Options)
        {
            string value = byPerson.TryGetValue(option.Name, out string? kept) && (option.Kind != TemplateOptionKind.Choice || option.Choices.Any(c => c.Value == kept))
                ? kept : option.Default;
            values[option.Name] = value;
            Control editor;
            Control row;
            switch (option.Kind)
            {
                case TemplateOptionKind.Bool:
                    CheckBox box = new() { Content = option.Label, IsChecked = IsTrue(value) != option.Inverted };
                    box.IsCheckedChanged += (_, _) => Changed(option, (box.IsChecked == true) != option.Inverted ? "true" : "false");
                    editor = box;
                    row = Row(null, box);
                    break;
                case TemplateOptionKind.Choice:
                    ComboBox choices = new() { MinWidth = 240, ItemsSource = option.Choices.Select(c => c.Label).ToList() };
                    choices.SelectedIndex = Math.Max(0, option.Choices.ToList().FindIndex(c => c.Value.Equals(value, StringComparison.OrdinalIgnoreCase)));
                    choices.SelectionChanged += (_, _) =>
                    {
                        if (choices.SelectedIndex >= 0)
                        {
                            Changed(option, option.Choices[choices.SelectedIndex].Value);
                        }
                    };
                    editor = choices;
                    row = Row($"{option.Label}:", choices);
                    break;
                default:
                    TextBox text = new() { Classes = { "search" }, Text = value, MaxWidth = 360, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 240 };
                    text.TextChanged += (_, _) => Changed(option, text.Text ?? string.Empty);
                    editor = text;
                    row = Row($"{option.Label}:", text);
                    break;
            }
            if (option.Description is { Length: > 0 } said)
            {
                ToolTip.SetTip(row, said);
            }
            options.Children.Add(row);
            shown.Add((option, row, editor));
        }
        Enable();
    }

    private void Changed(TemplateOption option, string value)
    {
        values[option.Name] = value;
        byPerson[option.Name] = value;
        Enable();
    }

    /// <summary>Enables each option its template's condition allows with the others as they are now.</summary>
    private void Enable()
    {
        foreach ((TemplateOption option, Control row, Control editor) in shown)
        {
            editor.IsEnabled = ProjectTemplates.IsEnabled(option.EnabledWhen, values);
            row.Opacity = editor.IsEnabled ? 1 : 0.55;
        }
        command.Text = template is null ? string.Empty : $"dotnet {string.Join(' ', Arguments(null).Select(Quoted))}";
    }

    /// <summary>A name typed: the project's follows the solution's until the person types one of its own.</summary>
    private void NameChanged(bool solution)
    {
        if (solution && !ProjectNamed && ProjectName != SolutionName)
        {
            Set(projectName, SolutionName);
        }
        ShowWhere();
    }

    /// <summary>Where Create puts it, as Rider says it under the directory.</summary>
    private void ShowWhere()
    {
        string? root = Folder.Length > 0 && Path.IsPathFullyQualified(Folder) && (target is not null || SolutionName.Length > 0) && ProjectName.Length > 0
            ? Planned().Root : null;
        string into = target?.Folder is { } folder ? $"the solution folder {folder.Split('/', StringSplitOptions.RemoveEmptyEntries)[^1]}"
            : $"the solution {Path.GetFileNameWithoutExtension(target?.Solution)}";
        where.Text = root is null ? string.Empty
            : target is null ? $"The solution will be created in {root}"
            : $"The project will be created in {root} and added to {into}";
        if (template is not null)
        {
            Enable();
        }
    }

    private void ShowTemplate(ProjectTemplate? chosen)
    {
        details.IsVisible = chosen is not null;
        create.IsEnabled = chosen is not null;
        if (chosen is null)
        {
            heading.Text = target is null ? "New Solution" : "New Project";
            description.Text = string.Empty;
        }
    }

    /// <summary>Puts the dialog's own name in a box; one with the keys keeps it chosen, so what is typed replaces it.</summary>
    private void Set(TextBox box, string text)
    {
        if (box == solutionName)
        {
            solutionGiven = text;
        }
        else
        {
            projectGiven = text;
        }
        box.Text = text;
        if (box.IsFocused)
        {
            box.SelectAll();
        }
    }

    /// <summary>A template's default name with the first number that makes it one nothing has (ConsoleApp1, ConsoleApp2).</summary>
    private static string Unique(string name, Func<string, bool> free)
    {
        for (int n = 1; ; n++)
        {
            if (free($"{name}{n}"))
            {
                return $"{name}{n}";
            }
        }
    }

    private static bool IsTrue(string value) => value.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static string Quoted(string argument) => argument.Length == 0 || argument.Any(char.IsWhiteSpace) ? $"\"{argument}\"" : argument;

    private void Fail(string? why)
    {
        problem.Text = why;
        problem.IsVisible = why is not null;
    }

    private void Busy(bool busy)
    {
        working.IsVisible = busy;
        problem.IsVisible = !busy && problem.Text is not null;
        create.IsEnabled = !busy && template is not null;
        list.IsEnabled = !busy;
        foreach (Control field in (Control[])[solutionName, projectName, directory, browse, sameDirectory, format, framework, language, options])
        {
            field.IsEnabled = !busy;
        }
        if (!busy)
        {
            Enable();
        }
    }

    /// <summary>A row of the right side: its label (none for a checkbox, which carries its own) and what sets it.</summary>
    private static Control Row(string? label, Control editor)
    {
        DockPanel row = new();
        TextBlock name = new() { Text = label ?? string.Empty, Width = LabelWidth, Padding = new Thickness(0, 0, 12, 0), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(name, Dock.Left);
        row.Children.Add(name);
        if (editor is ComboBox)
        {
            editor.HorizontalAlignment = HorizontalAlignment.Left;
        }
        row.Children.Add(editor);
        return row;
    }

    private async Task BrowseAsync()
    {
        IReadOnlyList<IStorageFolder> picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = target is null ? "Select Solution Directory" : "Select Project Directory",
            SuggestedStartLocation = Directory.Exists(Folder) ? await StorageProvider.TryGetFolderFromPathAsync(Folder) : null,
        });
        if (picked.Count == 1 && picked[0].TryGetLocalPath() is { } path)
        {
            Folder = path;
        }
    }
}
