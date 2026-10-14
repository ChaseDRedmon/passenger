using Passenger.App.Workspace;
using Passenger.Editor;
using Passenger.Text;
using Passenger.Xaml;
using Passenger.Xaml.Features;
using Passenger.Xaml.Projects;
using Passenger.Xaml.Syntax;

namespace Passenger.App.Xaml;

/// <summary>One open XAML file's share of its project's XAML: its text read (off the UI thread, once per version) against
/// the project as it stands, for what is asked of it as it is typed. The project's load may still be under way as the
/// file opens; what is asked meanwhile waits for it, and a file of a project whose XAML Passenger does not read (no
/// Avalonia) answers nothing.</summary>
public sealed class XamlDocument : IDisposable
{
    private readonly XamlService service;
    private (int Version, XamlProject Project, XamlFile File)? last;

    internal XamlDocument(XamlService service, OpenDocument document, XamlLoading loading)
    {
        this.service = service;
        Document = document;
        Loading = loading;
    }

    public OpenDocument Document { get; }

    internal XamlLoading Loading { get; private set; }

    /// <summary>The project's first read, which a test or a screenshot waits for; its answer null for a project whose
    /// XAML Passenger does not read.</summary>
    public Task<XamlProject?> Loaded => Loading.Loaded;

    /// <summary>Whether the project's types have been read and are XAML Passenger reads.</summary>
    public bool IsLoaded => Loading.Loaded.IsCompletedSuccessfully && Loading.Loaded.Result is not null;

    /// <summary>The file read as it stands, against the project as it stands: the last read when neither changed since;
    /// null for a project whose XAML Passenger does not read.</summary>
    public async Task<XamlFile?> FileAsync()
    {
        TextSnapshot text = Document.Text.Snapshot;
        int version = Document.Text.Version;
        string path = Document.Path;
        if (await Loading.Loaded is null || await Loading.Builder is not { } builder)
        {
            return null;
        }
        XamlProject project = builder.Current;
        if (last is { } kept && kept.Version == version && kept.Project == project)
        {
            return kept.File;
        }
        XamlFile file = await BackgroundWork.Run(() => Read(path, text.ToString(), project));
        if (Document.Text.Version == version)
        {
            last = (version, project, file);
        }
        return file;
    }

    /// <summary>A parse made a file of the project, the project's summary of it taken from this text.</summary>
    private static XamlFile Read(string path, string text, XamlProject project)
    {
        XmlTree tree = XmlTree.Parse(text);
        return new XamlFile(path, tree, project.With(XamlFileSummary.Of(path, tree, project.ResolveSource)));
    }

    /// <summary>What completion offers at a caret, for the text as it stands.</summary>
    public async Task<XamlCompletionList?> CompleteAsync(int caret, CancellationToken cancel)
    {
        if (!Loading.Loaded.IsCompleted)
        {
            service.Say($"Reading the types {Path.GetFileNameWithoutExtension(Loading.ProjectPath)}'s XAML can use…");
        }
        XamlFile? file = await FileAsync();
        cancel.ThrowIfCancellationRequested();
        return file is null ? null : await BackgroundWork.Run(() => XamlCompletion.At(file, caret), cancel);
    }

    /// <summary>The project was read again (its C# changed, another XAML file did): the next question reads against it.</summary>
    internal void ProjectChanged() => last = null;

    /// <summary>The project is being loaded again (its project file or restore changed).</summary>
    internal void Follow(XamlLoading reloading)
    {
        Loading = reloading;
        last = null;
    }

    public void Dispose() => last = null;
}
