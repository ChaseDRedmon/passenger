using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Passenger.App.Run;
using Passenger.App.ToolWindows;
using Passenger.Core;
using Passenger.Editor;

namespace Passenger.App.Templates;

/// <summary>Where Add New Project puts what it makes: the open solution, the solution folder chosen (by its path in the
/// solution, null for the solution's top) and the names of the projects it has, which a new one cannot take.</summary>
public sealed record ProjectTarget(string Solution, string? Folder, IReadOnlyList<string> Projects);

/// <summary>What Create made, canonical: New Solution's solution (null for Add New Project's) and the projects.</summary>
public sealed record CreatedProjects(string? Solution, IReadOnlyList<string> Projects);

/// <summary>Rider's New Project dialog, for New Solution and Add, New Project: dotnet new's templates down the left, then
/// the known packages' kinds it has not got, each offering to install its package; on the right the names, where it goes,
/// the target framework, the language, and the template's own options. Create refuses a name taken or a folder not empty,
/// saying why, before anything is made; otherwise it runs dotnet new, takes Accelerate's tools out of what it made, and
/// closes with it (<see cref="Created"/>). What a Create that failed or was canceled made is removed.</summary>
public sealed class NewProjectDialog : Window
{
    private const double LabelWidth = 170;

    private readonly ProjectTarget? target;
    private readonly string readFrom;
    private readonly ListBox list;
    private readonly TextBlock loading;
    private readonly TextBlock heading;
    private readonly TextBlock description;
    private readonly TextBox solutionName = new() { Classes = { "search" } };
    private readonly TextBox projectName = new() { Classes = { "search" } };
    private readonly TextBox directory = new() { Classes = { "search" } };
    private readonly CheckBox sameDirectory = new() { Content = "Put solution and project in the same directory" };
    private readonly ComboBox format = new() { MinWidth = 160 };
    private readonly Control projectRow;
    private readonly Control sameRow;
    private readonly Control? formatRow;
    private readonly TextBlock where = new() { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly ComboBox framework = new() { MinWidth = 160 };
    private readonly ComboBox language = new() { MinWidth = 160 };
    private readonly Control languageRow;
    private readonly StackPanel options = new() { Spacing = 10 };
    private readonly TextBlock command = new() { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly Control details;
    private readonly Control offer;
    private readonly TextBlock offerText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock offerCommand = new() { Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly Button install = new() { Content = "Install", Classes = { "primary" }, MinWidth = 80, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly OutputView installOutput = new(toolbar: false) { Height = 220, IsVisible = false };
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
    private IReadOnlyList<ProjectTemplate> templates = [];
    private ProjectTemplate? template;
    private CancellationTokenSource? creating;
    private CancellationTokenSource? installing;

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
            ItemTemplate = new FuncDataTemplate<ProjectTemplate?>((row, _) => row is null ? new Panel() : Item(row), supportsRecycling: false),
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
        projectRow = Row("Project name:", projectName);
        sameRow = Row(null, sameDirectory);
        if (target is null)
        {
            formatRow = Row("Solution format:", format);
            fields.Children.Add(Row("Solution name:", solutionName));
            fields.Children.Add(projectRow);
            fields.Children.Add(Row("Solution directory:", folderRow));
            fields.Children.Add(sameRow);
            fields.Children.Add(formatRow);
        }
        else
        {
            fields.Children.Add(projectRow);
            fields.Children.Add(Row("Project directory:", folderRow));
        }
        fields.Children.Add(Row(null, where));
        languageRow = Row("Language:", language);
        details = new StackPanel
        {
            Spacing = 10,
            Children = { Rule(), Row("Target framework:", framework), languageRow, options, Row(null, command) },
        };
        install.Click += (_, _) => _ = InstallAsync();
        offer = new StackPanel
        {
            Spacing = 10,
            IsVisible = false,
            Children = { Rule(), offerText, offerCommand, install, installOutput },
        };
        StackPanel right = new()
        {
            Margin = new Thickness(24, 18, 24, 18),
            Spacing = 10,
            Children = { new StackPanel { Children = { heading, description }, Margin = new Thickness(0, 0, 0, 8) }, fields, details, offer },
        };
        Border rightPane = new() { Child = new ScrollViewer { Content = right } };
        rightPane.Bind(Border.BackgroundProperty, rightPane.GetResourceObservable("Passenger.Editor.Background"));

        problem.Bind(TextBlock.ForegroundProperty, problem.GetResourceObservable("Passenger.Editor.Error"));
        working = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, IsVisible = false, Children = { bar, progress } };
        Button cancel = new() { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        cancel.Click += (_, _) => Cancel();
        create.Click += (_, _) => _ = CreateAsync();
        Closing += (_, _) =>
        {
            creating?.Cancel();
            installing?.Cancel();
        };
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
        readFrom = target is null ? folder : Path.GetDirectoryName(target.Solution)!;
        TemplatesRead = LoadAsync(null);
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

    /// <summary>The templates listed, in Rider's order: those installed, and the known packages' kinds dotnet new has not
    /// got (<see cref="ProjectTemplate.Pack"/>); a solution template in New Solution only.</summary>
    public IReadOnlyList<ProjectTemplate> Templates => templates;

    /// <summary>The template chosen.</summary>
    public ProjectTemplate? Chosen
    {
        get => template;
        set => list.SelectedItem = value;
    }

    /// <summary>The template list's row for one, for a click on it; null when it is not listed.</summary>
    public Control? TemplateRow(ProjectTemplate row)
    {
        int at = templates.ToList().IndexOf(row);
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

    /// <summary>Whether the project's name is asked: not for a solution template, which names its projects after the solution.</summary>
    public bool AsksProjectName => projectRow.IsVisible;

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

    /// <summary>What the dialog offers for a template not installed (Rider's words for its package, and the command
    /// Install runs); null when the template chosen is installed.</summary>
    public string? Offer => offer.IsVisible ? $"{offerText.Text}\n{offerCommand.Text}" : null;

    /// <summary>The Install button, for a click on it.</summary>
    public Button InstallButton => install;

    /// <summary>What the last install's dotnet new install wrote, as the dialog shows it.</summary>
    public string InstallOutput
    {
        get
        {
            installOutput.Flush();
            return installOutput.Document.Snapshot.ToString();
        }
    }

    public bool IsInstalling => installing is not null;

    /// <summary>Why the last Create was refused or failed, why an install failed, or why the templates could not be read;
    /// null when none.</summary>
    public string? Problem => problem.IsVisible ? problem.Text : null;

    public bool IsCreating => creating is not null;

    private bool SolutionNamed => SolutionName.Length > 0 && SolutionName != solutionGiven;

    private bool ProjectNamed => ProjectName.Length > 0 && ProjectName != projectGiven;

    /// <summary>The name dotnet new is given: a solution template's is the solution's.</summary>
    private string NameGiven => template is { IsSolution: true } ? SolutionName : ProjectName;

    /// <summary>The Create button, for a click on it.</summary>
    public Button CreateButton => create;

    /// <summary>What Create made, once it closed the dialog; null until then, and when given up.</summary>
    public CreatedProjects? Created { get; private set; }

    /// <summary>Create, as its button does: what it makes, or why not, the dialog closing once it is made.</summary>
    public async Task CreateAsync()
    {
        if (creating is not null || installing is not null || template is null)
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
            if (plan.Solution is not null && !template.IsSolution)
            {
                progress.Text = $"Creating the solution {SolutionName}...";
                if (!await MakeSolutionAsync(plan, existed, cancel.Token))
                {
                    return;
                }
            }
            progress.Text = $"Creating {NameGiven} from {template.Name} (dotnet new {template.ShortName})...";
            ChildResult made = await RunAsync(Arguments(plan.ProjectFolder), plan.Root, cancel.Token);
            if (!made.Succeeded)
            {
                await FailedAsync($"{NameGiven} could not be created: {ProjectTemplates.Said(made)}", plan, existed);
                return;
            }
            List<string> projects = ProjectsIn(plan.ProjectFolder);
            if (projects.Count == 0)
            {
                await FailedAsync($"dotnet new {template.ShortName} made no project in {plan.ProjectFolder}", plan, existed);
                return;
            }
            await BackgroundWork.Run(() => DeveloperTools.TakeOut(plan.ProjectFolder), cancel.Token);
            if (Variant().SkipRestoreOption is not null)
            {
                // The template's own restore, after the take-out; one that fails, as its own may, leaves the build to say why.
                foreach (string project in projects)
                {
                    progress.Text = $"Restoring {Path.GetFileNameWithoutExtension(project)}...";
                    Task<ChildResult> restore = ChildProcesses.RunAsync(ChildProcesses.DotnetCommand(["restore", project], plan.Root), limit: TimeSpan.FromMinutes(10), cancel: cancel.Token);
                    BackgroundWork.Track(restore);
                    await restore;
                }
            }
            string? madeSolution = plan.Solution;
            if (template.IsSolution)
            {
                // The template's own solution, else one made for what it made, as dotnet new sln would.
                madeSolution = SolutionIn(plan.Root);
                if (madeSolution is null)
                {
                    progress.Text = $"Creating the solution {SolutionName}...";
                    if (!await MakeSolutionAsync(plan, existed, cancel.Token))
                    {
                        return;
                    }
                    madeSolution = plan.Solution;
                }
            }
            Created = new CreatedProjects(madeSolution is null ? null : PathNames.Canonical(madeSolution), projects);
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
            await FailedAsync($"{NameGiven} could not be created: {error.Message}", plan, existed);
        }
        finally
        {
            creating = null;
            Busy(false);
        }
    }

    /// <summary>dotnet new sln for the plan's solution; false, what Create made removed and why said, when it failed.</summary>
    private async Task<bool> MakeSolutionAsync(Plan plan, bool existed, CancellationToken cancel)
    {
        ChildResult solution = await RunAsync(["sln", "--name", SolutionName, "--output", plan.Root, .. FormatArguments()], plan.Root, cancel);
        if (!solution.Succeeded)
        {
            await FailedAsync($"The solution could not be created: {ProjectTemplates.Said(solution)}", plan, existed);
        }
        return solution.Succeeded;
    }

    /// <summary>Install, as its button does: dotnet new install of the chosen template's package, its output shown as it
    /// comes, then the templates read again and the one installed chosen, with its options.</summary>
    public async Task InstallAsync()
    {
        if (template is not { Pack: { } pack } wanted || installing is not null || creating is not null)
        {
            return;
        }
        using CancellationTokenSource cancel = new();
        installing = cancel;
        Fail(null);
        progress.Text = $"Installing {pack.Id} (dotnet new install {pack.Id})...";
        Busy(true);
        installOutput.Clear();
        installOutput.IsVisible = true;
        using ProcessRunner runner = new(installOutput);
        try
        {
            if (runner.StartDotnet(ProjectTemplates.New(["install", pack.Id]), InstallFolder()) is { } refused)
            {
                Fail($"Installation of {pack.Id} has failed: {refused}");
                return;
            }
            BackgroundWork.Track(runner.Ended);
            int code;
            using (cancel.Token.Register(runner.Stop))
            {
                code = await runner.Ended;
            }
            if (cancel.IsCancellationRequested)
            {
                return;
            }
            if (code != 0)
            {
                Fail($"Installation of {pack.Id} has failed (exit code {code}): its output says why");
                return;
            }
            ProjectTemplates.Forget();
            progress.Text = "Reading the templates...";
            await LoadAsync(wanted);
            if (template is { Pack: not null })
            {
                Fail($"{pack.Id} is installed, but dotnet new has no {wanted.Name} ({wanted.ShortName}) from it");
            }
        }
        finally
        {
            installing = null;
            Busy(false);
        }
    }

    /// <summary>Where dotnet new install runs, whose NuGet configuration says where the package comes from: the directory
    /// chosen, else where the templates were read.</summary>
    private string InstallFolder() => Folder.Length > 0 && Path.IsPathFullyQualified(Folder) && Directory.Exists(Folder) ? Folder : readFrom;

    /// <summary>Cancel: stops a Create or an install under way, removing what a Create made, and closes the dialog.</summary>
    public void Cancel()
    {
        creating?.Cancel();
        installing?.Cancel();
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
        string solution = Path.Combine(root, $"{SolutionName}.{SolutionFormat}");
        return template is { IsSolution: true } ? new Plan(root, solution, root)
            : new Plan(root, solution, SameDirectory ? root : Path.Combine(root, ProjectName));
    }

    /// <summary>The solution file a solution template made at its top, the one of the solution's name first.</summary>
    private string? SolutionIn(string root) =>
        Directory.EnumerateFiles(root).Where(f => Path.GetExtension(f).ToLowerInvariant() is ".sln" or ".slnx")
            .OrderBy(f => Path.GetFileNameWithoutExtension(f).Equals(SolutionName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(f => f, StringComparer.Ordinal).FirstOrDefault();

    /// <summary>Why Create would make nothing, or null: Rider's reasons for a name, then where it goes, then the options.</summary>
    private string? WhyNot()
    {
        if (template is null)
        {
            return "Choose a template";
        }
        if (template.Pack is { } pack)
        {
            return $"{template.Name} comes with {pack.Id}, which is not installed: Install it first";
        }
        if (target is null && NameProblem(SolutionName, "Solution") is { } solution)
        {
            return solution;
        }
        if (!template.IsSolution && NameProblem(ProjectName, "Project") is { } project)
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

    /// <summary>dotnet new's arguments for the project: its template by short name, name and folder (none in the line the
    /// dialog shows), language, framework, and each option enabled and changed from its default.</summary>
    private List<string> Arguments(string? output)
    {
        TemplateVariant variant = Variant();
        List<string> arguments = [template!.ShortName, "--name", NameGiven];
        if (output is not null)
        {
            arguments.AddRange(["--output", output, "--no-update-check"]);
            if (variant.SkipRestoreOption is { } skip)
            {
                // Restored once Accelerate's tools are out of it, rather than with them.
                arguments.AddRange([$"--{skip}", "true"]);
            }
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

    /// <summary>dotnet new with these arguments, in the IDE's hive.</summary>
    private static Task<ChildResult> RunAsync(IEnumerable<string> arguments, string folder, CancellationToken cancel)
    {
        Task<ChildResult> run = ChildProcesses.RunAsync(ProjectTemplates.NewCommand(arguments, folder), limit: TimeSpan.FromMinutes(10), cancel: cancel);
        BackgroundWork.Track(run);
        return run;
    }

    /// <summary>The project files a template made, the one of its name first, outside build output.</summary>
    private List<string> ProjectsIn(string folder) =>
        [.. Directory.EnumerateFiles(folder, "*.*proj", SearchOption.AllDirectories)
            .Where(p => Path.GetExtension(p).ToLowerInvariant() is ".csproj" or ".fsproj" or ".vbproj")
            .Where(p => !Path.GetRelativePath(folder, p).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .OrderBy(p => Path.GetFileNameWithoutExtension(p).Equals(NameGiven, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
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

    /// <summary>Reads the templates and lists them, the first chosen; after an install, the installed one of the kind that
    /// was offered (<paramref name="installed"/>).</summary>
    private async Task LoadAsync(ProjectTemplate? installed)
    {
        try
        {
            catalog = await BackgroundWork.Run(() => ProjectTemplates.LoadAsync(readFrom));
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException or JsonException
            or TimeoutException or KeyNotFoundException or System.ComponentModel.Win32Exception)
        {
            loading.Text = $"The templates could not be read: {error.Message}";
            loading.IsVisible = true;
            Fail(loading.Text);
            return;
        }
        templates = [.. catalog.Templates.Where(t => target is null || !t.IsSolution)];
        loading.IsVisible = templates.Count == 0;
        loading.Text = "dotnet new has no project templates installed";
        setting = true;
        format.ItemsSource = catalog.SolutionFormats.Select(f => $".{f}").ToList();
        format.SelectedIndex = 0;
        setting = false;
        list.ItemsSource = templates;
        ProjectTemplate? chosen = installed is null ? templates.FirstOrDefault()
            : templates.FirstOrDefault(t => t.Pack is null && t.IsSolution == installed.IsSolution && t.ShortNames.Contains(installed.ShortName, StringComparer.OrdinalIgnoreCase))
              ?? templates.FirstOrDefault(t => t.Name == installed.Name);
        list.SelectedItem = chosen;
        if (chosen is not null)
        {
            list.ScrollIntoView(chosen);
        }
    }

    /// <summary>A template chosen: its names (unless the person typed their own), languages, frameworks and options, or
    /// for one not installed, the offer of its package.</summary>
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
        command.Text = template is null ? string.Empty : $"dotnet new {string.Join(' ', Arguments(null).Select(Quoted))}";
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
        string? root = Folder.Length > 0 && Path.IsPathFullyQualified(Folder) && (target is not null || SolutionName.Length > 0)
            && (template is { IsSolution: true } || ProjectName.Length > 0)
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

    /// <summary>The right side for a template: its details once installed, else the offer of its package; a solution
    /// template asks no project name, nor where its project goes, nor the format of a solution it makes itself.</summary>
    private void ShowTemplate(ProjectTemplate? chosen)
    {
        details.IsVisible = chosen is { Pack: null };
        offer.IsVisible = chosen is { Pack: not null };
        create.IsEnabled = chosen is { Pack: null } && installing is null && creating is null;
        bool solution = chosen is { IsSolution: true };
        projectRow.IsVisible = !solution;
        sameRow.IsVisible = target is null && !solution;
        formatRow?.IsVisible = !solution;
        if (chosen is { Pack: { } pack })
        {
            offerText.Text = pack.Offer;
            offerCommand.Text = $"{pack.Id} is not installed. Install runs dotnet new install {pack.Id}, which takes it from the NuGet feeds this directory uses.";
            installOutput.IsVisible = false;
        }
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
        create.IsEnabled = !busy && template is { Pack: null };
        install.IsEnabled = !busy;
        list.IsEnabled = !busy;
        foreach (Control field in (Control[])[solutionName, projectName, directory, browse, sameDirectory, format, framework, language, options])
        {
            field.IsEnabled = !busy;
        }
        if (!busy && template is not null)
        {
            Enable();
        }
    }

    /// <summary>A template's row: its name, whole on hover where it is cut short, and for one not installed Rider's word
    /// for it.</summary>
    private static Control Item(ProjectTemplate row)
    {
        TextBlock name = new() { Text = row.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Border item = new() { Padding = new Thickness(10, 6) };
        ToolTip.SetTip(item, row.Pack is null ? row.Name : $"{row.Name}, from {row.Pack.Id}, which is not installed");
        if (row.Pack is null)
        {
            item.Child = name;
            return item;
        }
        TextBlock missing = new() { Text = "Not installed", Classes = { "muted" }, FontSize = 11, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel both = new();
        DockPanel.SetDock(missing, Dock.Right);
        both.Children.Add(missing);
        both.Children.Add(name);
        item.Child = both;
        return item;
    }

    private Control Rule()
    {
        Border rule = new() { Height = 1, Margin = new Thickness(0, 6) };
        rule.Bind(Border.BackgroundProperty, rule.GetResourceObservable("Passenger.Divider"));
        return rule;
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
