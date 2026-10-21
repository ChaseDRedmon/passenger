using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Passenger.App.Explorer;

/// <summary>Which icons files and folders are drawn with (Settings, Appearance, File icons).</summary>
public enum FileIconTheme
{
    /// <summary>Passenger's own: a folder, a code document or a plain one, tinted by kind.</summary>
    Default,

    /// <summary>The file-icons package's: a shape and colour for each language, tool and kind of file.</summary>
    FileIcons,
}

/// <summary>The icon and tint a file or folder is drawn with. The Default set is a folder, a code document or a plain
/// one, tinted by kind from the theme's <c>Passenger.File.*</c> tokens so C# reads apart from XML at a glance, as
/// Rider's file-type icons do; the File Icons set is <see cref="FileIconSet"/>'s. A solution folder has its own under
/// either, as Rider's does, so it never passes for a folder on disk.</summary>
public static class FileIcons
{
    /// <summary>The set in force for the process, put there by the settings (<c>SettingsStore.InForce</c>).</summary>
    public static FileIconTheme Theme { get; set; }

    public static Geometry For(ExplorerNode node)
    {
        if (Theme == FileIconTheme.FileIcons && SetLook(node) is { } look)
        {
            return Find<Geometry>($"FileIcon.{look.Shape}");
        }
        return Find<Geometry>(node.Kind switch
        {
            NodeKind.Folder => node.IsExpanded ? "Icon.FolderOpen" : "Icon.Explorer",
            NodeKind.SolutionFolder => node.IsExpanded ? "Icon.SolutionFolderOpen" : "Icon.SolutionFolder",
            NodeKind.Solution => "Icon.Solution",
            NodeKind.Project or NodeKind.ProjectReference => "Icon.Project",
            NodeKind.Dependencies or NodeKind.Package => "Icon.Package",
            NodeKind.Framework => "Icon.Reference",
            _ => Kind(node.Name) is "CSharp" or "Code" ? "Icon.Code" : "Icon.Document",
        });
    }

    public static IBrush BrushFor(ExplorerNode node)
    {
        if (Theme == FileIconTheme.FileIcons && SetLook(node) is { } look)
        {
            return Tint(look);
        }
        return Find<IBrush>(node.Kind switch
        {
            NodeKind.Folder => "Passenger.File.Folder",
            NodeKind.SolutionFolder => "Passenger.File.SolutionFolder",
            NodeKind.Solution => "Passenger.File.Project",
            NodeKind.Project => "Passenger.File.CSharp",
            NodeKind.Dependencies or NodeKind.Package or NodeKind.Framework or NodeKind.ProjectReference => "Passenger.File.Plain",
            _ => $"Passenger.File.{Kind(node.Name)}",
        });
    }

    public static Geometry ForFile(string path) => Theme == FileIconTheme.FileIcons
        ? Find<Geometry>($"FileIcon.{FileIconSet.ForFile(Path.GetFileName(path)).Shape}")
        : Find<Geometry>(Kind(Path.GetFileName(path)) is "CSharp" or "Code" ? "Icon.Code" : "Icon.Document");

    public static IBrush BrushForFile(string path) => Theme == FileIconTheme.FileIcons
        ? Tint(FileIconSet.ForFile(Path.GetFileName(path)))
        : Find<IBrush>($"Passenger.File.{Kind(Path.GetFileName(path))}");

    /// <summary>A row's look in the File Icons set, or null where the set draws as the Default does: a plain folder, a
    /// solution folder and a framework reference.</summary>
    private static FileIconLook? SetLook(ExplorerNode node) => node.Kind switch
    {
        NodeKind.Folder => FileIconSet.ForFolder(node.Name),
        NodeKind.Solution or NodeKind.Project or NodeKind.ProjectReference or NodeKind.File when node.Path is { } path => FileIconSet.ForFile(Path.GetFileName(path)),
        NodeKind.File => FileIconSet.ForFile(node.Name),
        NodeKind.Dependencies or NodeKind.Package => new FileIconLook("NuGet", "Blue"),
        _ => null,
    };

    private static IBrush Tint(FileIconLook look) => Find<IBrush>(look.Colour is { } colour ? $"Passenger.FileIcon.{colour}" : "Passenger.File.Plain");

    private static string Kind(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".cs" or ".csx" => "CSharp",
        ".csproj" or ".sln" or ".slnx" or ".props" or ".targets" => "Project",
        ".xml" or ".axaml" or ".xaml" or ".resx" or ".config" or ".manifest" => "Xml",
        ".json" => "Json",
        ".md" => "Markdown",
        ".js" or ".ts" or ".py" or ".sh" or ".rs" or ".go" or ".css" or ".html" or ".razor" or ".cshtml" => "Code",
        _ => "Plain",
    };

    private static T Find<T>(string key) where T : class =>
        Application.Current is { } app && app.TryFindResource(key, app.ActualThemeVariant, out object? value) && value is T found
            ? found
            : throw new InvalidOperationException($"The theme has no {key}.");
}
