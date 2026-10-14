using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Passenger.App.Explorer;

/// <summary>The icon and tint a file or folder is drawn with: a folder, a code document or a plain
/// one, tinted by kind from the theme's <c>Passenger.File.*</c> tokens so C# reads apart from XML
/// at a glance, as Rider's file-type icons do. A solution folder has its own, as Rider's does, so it
/// never passes for a folder on disk.</summary>
public static class FileIcons
{
    public static Geometry For(ExplorerNode node) => Find<Geometry>(node.Kind switch
    {
        NodeKind.Folder => node.IsExpanded ? "Icon.FolderOpen" : "Icon.Explorer",
        NodeKind.SolutionFolder => node.IsExpanded ? "Icon.SolutionFolderOpen" : "Icon.SolutionFolder",
        NodeKind.Solution => "Icon.Solution",
        NodeKind.Project or NodeKind.ProjectReference => "Icon.Project",
        NodeKind.Dependencies or NodeKind.Package => "Icon.Package",
        NodeKind.Framework => "Icon.Reference",
        _ => Kind(node.Name) is "CSharp" or "Code" ? "Icon.Code" : "Icon.Document",
    });

    public static IBrush BrushFor(ExplorerNode node) => Find<IBrush>(node.Kind switch
    {
        NodeKind.Folder => "Passenger.File.Folder",
        NodeKind.SolutionFolder => "Passenger.File.SolutionFolder",
        NodeKind.Solution => "Passenger.File.Project",
        NodeKind.Project => "Passenger.File.CSharp",
        NodeKind.Dependencies or NodeKind.Package or NodeKind.Framework or NodeKind.ProjectReference => "Passenger.File.Plain",
        _ => $"Passenger.File.{Kind(node.Name)}",
    });

    public static Geometry ForFile(string path) => Find<Geometry>(Kind(Path.GetFileName(path)) is "CSharp" or "Code" ? "Icon.Code" : "Icon.Document");

    public static IBrush BrushForFile(string path) => Find<IBrush>($"Passenger.File.{Kind(Path.GetFileName(path))}");

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
