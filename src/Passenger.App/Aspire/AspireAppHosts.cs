using Passenger.App.Workspace;
using Passenger.Core;

namespace Passenger.App.Aspire;

/// <summary>Finds single-file AppHosts without treating their C# source as an MSBuild project.</summary>
public static class AspireAppHosts
{
    private const int FolderLimit = 5_000;
    private const int AppHostLimit = 200;

    internal static string? SingleFileWhyNot(string path, IEnumerable<string> projectPaths)
    {
        if (!Path.GetFileName(path).Equals("apphost.cs", StringComparison.OrdinalIgnoreCase))
        {
            return $"Aspire CLI {AspireClient.SupportedVersion} requires apphost.cs for a single-file AppHost";
        }
        string? folder = Path.GetDirectoryName(path);
        return projectPaths.Any(project => Path.GetExtension(project).Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            && Path.GetDirectoryName(project) is { } projectFolder && folder is not null && PathNames.Same(folder, projectFolder))
            ? $"Aspire CLI {AspireClient.SupportedVersion} requires apphost.cs to have no sibling .csproj project" : null;
    }

    public static IReadOnlyList<string> DiscoverFiles(string folder, CancellationToken cancel)
    {
        List<string> found = [];
        foreach (string path in PathNames.FoldersUnder(folder, Enters).Take(FolderLimit))
        {
            cancel.ThrowIfCancellationRequested();
            try
            {
                foreach (FileInfo file in new DirectoryInfo(path).EnumerateFiles("*.cs").OrderBy(f => f.Name, StringComparer.Ordinal))
                {
                    cancel.ThrowIfCancellationRequested();
                    if (IsSingleFile(file.FullName))
                    {
                        found.Add(PathNames.Canonical(file.FullName));
                        if (found.Count == AppHostLimit)
                        {
                            return found;
                        }
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
        return found;

        bool Enters(string path)
        {
            cancel.ThrowIfCancellationRequested();
            DirectoryInfo directory = new(path);
            return !directory.Name.StartsWith('.') && !IgnoredFolders.Contains(directory.Name) && directory.LinkTarget is null;
        }
    }

    public static bool IsSingleFile(string path)
    {
        if (!Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        bool comment = false;
        foreach (string line in SharedRead.Lines(path))
        {
            string text = line.Trim();
            if (comment)
            {
                int end = text.IndexOf("*/", StringComparison.Ordinal);
                if (end < 0)
                {
                    continue;
                }
                comment = false;
                text = text[(end + 2)..].TrimStart();
            }
            while (text.StartsWith("/*", StringComparison.Ordinal))
            {
                int end = text.IndexOf("*/", 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    comment = true;
                    break;
                }
                text = text[(end + 2)..].TrimStart();
            }
            if (comment)
            {
                continue;
            }
            if (text.StartsWith("#:sdk", StringComparison.Ordinal) && text.Length > 5 && char.IsWhiteSpace(text[5])
                && text[6..].TrimStart().Split('@')[0].Equals("Aspire.AppHost.Sdk", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (text.Length > 0 && !text.StartsWith('#') && !text.StartsWith("//", StringComparison.Ordinal))
            {
                return false;
            }
        }
        return false;
    }
}
