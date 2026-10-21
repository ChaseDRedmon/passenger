using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Passenger.App.ToolWindows;
using Passenger.Editor;

namespace Passenger.App.Endpoints;

/// <summary>The Endpoints window: every request of the open folder's .http and .rest files, under its file, with its
/// method; Test sends one as written and Interactive Test opens it in a dialog to change before sending. The answer of
/// the last test shows under the list. The environment chosen gives the requests' <c>{{variables}}</c> their values.</summary>
public sealed class EndpointsView : DockPanel
{
    /// <summary>A file's heading in the list.</summary>
    public sealed record FileRow(string Path, string Name);

    private readonly ListBox list;
    private readonly TextBlock placeholder;
    private readonly ComboBox environments;
    private readonly Dictionary<(string File, int Line), bool> outcomes = [];
    private IReadOnlyList<HttpFile> files = [];
    private string? root;
    private int reads;
    private bool shown;
    private bool stale;
    private CancellationTokenSource? sending;

    public EndpointsView()
    {
        Response = new ResponseView("Test a request to see its response here");
        list = new ListBox
        {
            Classes = { "tree" },
            ItemTemplate = new FuncDataTemplate<object>((row, _) => row switch
            {
                FileRow file => FileHeading(file),
                EndpointRequest request => Row(request),
                _ => new TextBlock(),
            }, supportsRecycling: false),
        };
        list.DoubleTapped += (_, _) => GoToChosen();
        list.KeyDown += (_, e) =>
        {
            if (list.SelectedItem is not EndpointRequest chosen)
            {
                return;
            }
            if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
            {
                _ = TestAsync(chosen);
                e.Handled = true;
            }
            else if (e.Key == Key.F4)
            {
                GoToChosen();
                e.Handled = true;
            }
        };
        ListMenus.Attach(list, () => list.SelectedItem is not EndpointRequest chosen ? [] :
        [
            ListMenus.Item("Test", "Enter", () => _ = TestAsync(chosen)),
            ListMenus.Item("Interactive Test…", null, () => InteractiveRequested?.Invoke(this, chosen)),
            ListMenus.Item("Go to Request", "F4", GoToChosen),
        ]);
        placeholder = new TextBlock { Text = "No requests: add an .http file (GET https://…)", Classes = { "emptyText" } };

        environments = new ComboBox { MinWidth = 140, VerticalAlignment = VerticalAlignment.Center };
        environments.SelectionChanged += (_, _) => EnvironmentChanged?.Invoke(this, EventArgs.Empty);
        ToolTip.SetTip(environments, $"The environment of {HttpVariables.PublicFile} whose variables the requests use");
        Button refresh = Tool("Icon.Rerun", "Read the .http files again", () => Reload(root));
        TextBlock label = new() { Text = "Environment", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        StackPanel bar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 4), Spacing = 4, Children = { label, environments, refresh } };
        DockPanel.SetDock(bar, Dock.Top);
        Children.Add(bar);

        Grid panes = new() { RowDefinitions = new RowDefinitions("*,1,*") };
        Panel top = new() { Children = { list, placeholder } };
        panes.Children.Add(top);
        HairlineDivider divider = new(GridResizeDirection.Rows) { [Grid.RowProperty] = 1 };
        panes.Children.Add(divider);
        Response[Grid.RowProperty] = 2;
        panes.Children.Add(Response);
        Children.Add(panes);
        Refresh();
    }

    /// <summary>The last test's answer.</summary>
    public ResponseView Response { get; }

    /// <summary>A request to open in the editor at its request line.</summary>
    public event EventHandler<EndpointRequest>? GoToRequested;

    /// <summary>A request to test in the Interactive Test dialog.</summary>
    public event EventHandler<EndpointRequest>? InteractiveRequested;

    public event EventHandler? EnvironmentChanged;

    /// <summary>The requests listed, in the files' order.</summary>
    public IReadOnlyList<EndpointRequest> Requests => [.. files.SelectMany(f => f.Requests)];

    /// <summary>The environment chosen, null with none.</summary>
    public string? Environment => environments.SelectedItem as string is { } chosen && chosen != NoEnvironment ? chosen : null;

    private const string NoEnvironment = "No environment";

    /// <summary>Whether a request's last test passed, failed (false) or has not run (null).</summary>
    public bool? Outcome(EndpointRequest request) => outcomes.TryGetValue((request.File, request.Line), out bool passed) ? passed : null;

    /// <summary>Chooses an environment by name, as the person would.</summary>
    public void ChooseEnvironment(string name) => environments.SelectedItem = name;

    /// <summary>The variables a request sees in the environment chosen.</summary>
    public IReadOnlyDictionary<string, string> VariablesFor(EndpointRequest request)
    {
        HttpFile file = files.FirstOrDefault(f => f.Path == request.File) ?? new HttpFile(request.File, new Dictionary<string, string>(), []);
        return HttpVariables.For(file, Environment, root);
    }

    /// <summary>Reads the folder's .http and .rest files again off the UI thread, or as the window is next shown while it
    /// is hidden, so a folder opened with it closed is walked once, by its file index alone; another folder's list goes
    /// at once.</summary>
    public void Reload(string? folder)
    {
        UiThread.Check();
        if (folder != root)
        {
            files = [];
            outcomes.Clear();
            Response.Clear();
            Refresh();
        }
        root = folder;
        int read = ++reads;
        stale = folder is not null && !shown;
        if (folder is null || !shown)
        {
            return;
        }
        _ = ReadAsync(folder, read);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        shown = true;
        if (stale)
        {
            Reload(root);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        shown = false;
    }

    private async Task ReadAsync(string folder, int read)
    {
        (List<HttpFile> found, List<string> names) = await BackgroundWork.Run(() =>
        {
            List<string> paths = [.. Workspace.FileIndex.Walk(folder).Select(p => Path.Combine(folder, p))];
            List<HttpFile> read = [.. paths.Where(HttpFile.Is).Order(StringComparer.OrdinalIgnoreCase).Select(TryRead).OfType<HttpFile>()];
            List<string> envFiles = [.. paths.Where(p => Path.GetFileName(p) is HttpVariables.PublicFile or HttpVariables.PrivateFile)];
            return (read, HttpVariables.Environments(envFiles).ToList());
        });
        if (read != reads)
        {
            return;
        }
        files = found;
        string? chosen = environments.SelectedItem as string;
        environments.ItemsSource = (string[])[NoEnvironment, .. names];
        environments.SelectedItem = chosen is not null && names.Contains(chosen) ? chosen : names.Count > 0 ? names[0] : NoEnvironment;
        Refresh();
    }

    private static HttpFile? TryRead(string path)
    {
        try
        {
            return HttpFile.Read(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Refresh()
    {
        object? selected = list.SelectedItem;
        List<object> rows = [];
        foreach (HttpFile file in files.Where(f => f.Requests.Count > 0))
        {
            rows.Add(new FileRow(file.Path, root is null ? Path.GetFileName(file.Path) : Path.GetRelativePath(root, file.Path)));
            rows.AddRange(file.Requests);
        }
        list.ItemsSource = rows;
        list.SelectedItem = selected is EndpointRequest was ? rows.OfType<EndpointRequest>().FirstOrDefault(r => r.File == was.File && r.Line == was.Line) : null;
        placeholder.IsVisible = rows.Count == 0;
    }

    /// <summary>Test: the request sent as written, on a client of its own, its answer held to its criteria (an answer
    /// below 400 when it has none). A test started while another is under way stops that one.</summary>
    public async Task TestAsync(EndpointRequest request)
    {
        UiThread.Check();
        list.SelectedItem = request;
        (EndpointCall? call, string? problem) = EndpointClient.Prepare(request, request.Method, request.Url, request.Headers, request.Body, VariablesFor(request));
        if (call is null)
        {
            Response.Refused(problem!);
            Record(request, false);
            return;
        }
        IReadOnlyList<Expectation> criteria = request.Expectations.Count > 0 ? request.Expectations : [Expectation.Answered];
        (EndpointResponse? response, IReadOnlyList<ExpectationResult> results) = await SendAsync(call, criteria, new SendOptions());
        if (response is not null)
        {
            Response.Show(response, results);
            Record(request, Response.Passed);
        }
    }

    /// <summary>Sends a call and checks its answer, stopping the one under way; null when another took its place.</summary>
    private async Task<(EndpointResponse?, IReadOnlyList<ExpectationResult>)> SendAsync(EndpointCall call, IReadOnlyList<Expectation> criteria, SendOptions options)
    {
        sending?.Cancel();
        using CancellationTokenSource mine = new();
        sending = mine;
        Response.Sending($"Sending {call.Method} {call.Url}…");
        try
        {
            EndpointResponse response = await BackgroundWork.Run(() => EndpointClient.SendAsync(call, options, mine.Token), mine.Token);
            return (response, [.. criteria.Select(c => c.Check(response))]);
        }
        catch (OperationCanceledException)
        {
            return (null, []);
        }
        finally
        {
            if (sending == mine)
            {
                sending = null;
            }
        }
    }

    /// <summary>Stops a test under way, as the folder closes.</summary>
    public void Stop() => sending?.Cancel();

    private void Record(EndpointRequest request, bool passed)
    {
        outcomes[(request.File, request.Line)] = passed;
        Refresh();
    }

    private void GoToChosen()
    {
        if (list.SelectedItem is EndpointRequest chosen)
        {
            GoToRequested?.Invoke(this, chosen);
        }
    }

    private static Control FileHeading(FileRow file)
    {
        PathIcon icon = new() { Width = 14, Height = 14, Margin = new Thickness(0, 0, 6, 0) };
        icon.Bind(PathIcon.DataProperty, icon.GetResourceObservable("Icon.DocumentText"));
        TextBlock name = new() { Text = file.Name, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(name, file.Path);
        return new StackPanel { Orientation = Orientation.Horizontal, Height = ListRow.Height, Margin = new Thickness(8, 0), IsHitTestVisible = false, Children = { icon, name } };
    }

    private Control Row(EndpointRequest request)
    {
        Button test = Tool("Icon.Run", "Test", () => _ = TestAsync(request));
        Button interactive = Tool("Icon.Edit", "Interactive Test…", () => InteractiveRequested?.Invoke(this, request));
        test.Name = "endpointTest";
        ((PathIcon)test.Content!).Bind(PathIcon.ForegroundProperty, test.GetResourceObservable("Passenger.Run"));
        interactive.Name = "endpointInteractive";
        StackPanel buttons = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { test, interactive } };
        DockPanel.SetDock(buttons, Dock.Right);

        PathIcon outcome = new() { Width = 12, Height = 12, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        if (Outcome(request) is { } passed)
        {
            outcome.Bind(PathIcon.DataProperty, outcome.GetResourceObservable(passed ? "Icon.TestPassed" : "Icon.TestFailed"));
            outcome.Bind(PathIcon.ForegroundProperty, outcome.GetResourceObservable(passed ? "Passenger.Test.Passed" : "Passenger.Test.Failed"));
        }
        DockPanel.SetDock(outcome, Dock.Left);
        Border method = MethodTag(request.Method);
        DockPanel.SetDock(method, Dock.Left);
        TextBlock title = new() { Text = request.Title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        DockPanel row = new() { Height = ListRow.Height, Margin = new Thickness(22, 0, 4, 0), Children = { buttons, outcome, method } };
        ToolTip.SetTip(row, $"{request.Method} {request.Url}");
        if (request.Name.Length > 0)
        {
            // A named request shows its URL after the name, muted, ending in an ellipsis first.
            TextBlock url = new() { Text = request.Url, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12, Margin = new Thickness(8, 0, 0, 0) };
            url.Bind(TextBlock.ForegroundProperty, url.GetResourceObservable("Passenger.TextMuted"));
            title.MaxWidth = 220;
            DockPanel.SetDock(title, Dock.Left);
            row.Children.Add(title);
            row.Children.Add(url);
        }
        else
        {
            row.Children.Add(title);
        }
        // The title takes what the row leaves, so a narrow window ends it in an ellipsis and keeps the buttons.
        return row;
    }

    /// <summary>A method's tag, in the colour Rider's HTTP Client gives it.</summary>
    internal static Border MethodTag(string method)
    {
        TextBlock text = new() { Text = method, FontFamily = CSharp.Fonts.Mono, FontSize = 11, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable(MethodColour(method)));
        return new Border { Classes = { "tag" }, MinWidth = 52, Padding = new Thickness(4, 1), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Child = text };
    }

    private static string MethodColour(string method) => method switch
    {
        "GET" => "Passenger.Test.Passed",
        "POST" => "Passenger.Link",
        "PUT" => "Passenger.Warning",
        "PATCH" => "Passenger.Syntax.Number",
        "DELETE" => "Passenger.Error",
        _ => "Passenger.TextMuted",
    };

    private static Button Tool(string icon, string tip, Action run)
    {
        Button button = new() { Classes = { "tool" }, Content = new PathIcon(), Focusable = false };
        ToolTip.SetTip(button, tip);
        ((PathIcon)button.Content).Bind(PathIcon.DataProperty, button.GetResourceObservable(icon));
        button.Click += (_, _) => run();
        return button;
    }
}
