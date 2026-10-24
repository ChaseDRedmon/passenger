using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Passenger.App.Explorer;

/// <summary>The icon and tint a file or folder is drawn with: <see cref="FileIconSet"/>'s shape and colour for its name, a
/// folder the set draws nothing apart for as a folder, and a solution folder as a filled one, so it never passes for a
/// folder on disk.</summary>
public static class FileIcons
{
    public static Geometry For(ExplorerNode node) => Look(node) is { } look
        ? Find<Geometry>($"FileIcon.{look.Shape}")
        : Find<Geometry>(node.Kind switch
        {
            NodeKind.SolutionFolder => node.IsExpanded ? "Icon.SolutionFolderOpen" : "Icon.SolutionFolder",
            NodeKind.Framework => "Icon.Reference",
            _ => node.IsExpanded ? "Icon.FolderOpen" : "Icon.Explorer",
        });

    public static IBrush BrushFor(ExplorerNode node) => Look(node) is { } look
        ? Tint(look)
        : Find<IBrush>(node.Kind switch
        {
            NodeKind.SolutionFolder => "Passenger.File.SolutionFolder",
            NodeKind.Framework => "Passenger.File.Plain",
            _ => "Passenger.File.Folder",
        });

    public static Geometry ForFile(string path) => Find<Geometry>($"FileIcon.{FileIconSet.ForFile(Path.GetFileName(path)).Shape}");

    public static IBrush BrushForFile(string path) => Tint(FileIconSet.ForFile(Path.GetFileName(path)));

    /// <summary>A row's look in the set, or null for what it draws as a folder or a link: a plain folder, a solution
    /// folder and a target framework.</summary>
    private static FileIconLook? Look(ExplorerNode node) => node.Kind switch
    {
        NodeKind.Folder => FileIconSet.ForFolder(node.Name),
        NodeKind.SolutionFolder or NodeKind.Framework => null,
        NodeKind.Dependencies or NodeKind.Package => FileIconSet.Packages,
        NodeKind.ProjectReference => FileIconSet.ForFile(Path.GetFileName(node.Referenced ?? node.Name)),
        _ => FileIconSet.ForFile(Path.GetFileName(node.Path ?? node.Name)),
    };

    private static IBrush Tint(FileIconLook look) => Find<IBrush>(look.Colour is { } colour ? $"Passenger.FileIcon.{colour}" : "Passenger.File.Plain");

    private static T Find<T>(string key) where T : class =>
        Application.Current is { } app && app.TryFindResource(key, app.ActualThemeVariant, out object? value) && value is T found
            ? found
            : throw new InvalidOperationException($"The theme has no {key}.");
}
