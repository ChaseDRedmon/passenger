using Passenger.App.Workspace;
using Passenger.Core;

namespace Passenger.App.Run;

/// <summary>Whether a running program's project changed since the build it runs began, read before anything is built, so
/// Rerun stops only what it builds again and never builds over a program's running files (on Windows they are locked while
/// it runs).</summary>
public static class BuiltInputs
{
    /// <summary>Whether a file was written after <paramref name="since"/> under the project's folder (another project's folder
    /// under it not entered, nor bin, obj or a hidden folder) or, with <paramref name="references"/>, under the folders of the
    /// projects it references, in turn. On any thread.</summary>
    public static bool ChangedSince(string project, DateTime since, IReadOnlyList<ProjectModel> projects, bool references = true)
    {
        HashSet<string> others = new(projects.Select(p => PathNames.Canonical(p.Folder)), PathNames.Comparer);
        HashSet<string> seen = new(PathNames.Comparer);
        Stack<string> next = new([PathNames.Canonical(project)]);
        while (next.TryPop(out string? current))
        {
            if (!seen.Add(current))
            {
                continue;
            }
            string folder = PathNames.Canonical(Path.GetDirectoryName(current)!);
            if (FolderWalk.Files(folder, built: true, enters: inner => PathNames.Same(inner, folder) || !others.Contains(inner))
                .Any(file => IsInput(file) && File.GetLastWriteTimeUtc(file) > since))
            {
                return true;
            }
            if (references)
            {
                foreach (string reference in projects.FirstOrDefault(p => PathNames.Same(p.Path, current))?.ProjectReferences ?? [])
                {
                    next.Push(PathNames.Canonical(reference));
                }
            }
        }
        return false;
    }

    /// <summary>What a build reads that its program could not take in while it runs: code, project files, resources, XAML and
    /// settings. Not what a running program writes beside its project (DCP's <c>.dcp.json</c>, a SQLite database), nor
    /// static web assets, served from where they are.</summary>
    private static bool IsInput(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".json" => !file.EndsWith(".dcp.json", StringComparison.OrdinalIgnoreCase),
        ".cs" or ".vb" or ".fs" or ".fsi" or ".csproj" or ".vbproj" or ".fsproj" or ".props" or ".targets" or ".razor" or ".cshtml"
            or ".vbhtml" or ".resx" or ".xaml" or ".axaml" or ".config" or ".proto" => true,
        _ => false,
    };
}
