using Avalonia.Headless.XUnit;
using Passenger.App;
using Passenger.App.Workspace;

namespace Passenger.Tests;

/// <summary>The Explorer's file operations as Rider's: New Class writes a class in its folder's namespace and opens it;
/// a rename moves the file or folder and its open tabs with it; a delete goes to the Trash and closes its tab; a name
/// already there is refused with why.</summary>
public sealed class ExplorerFilesTests : IDisposable
{
    private readonly string folder = Directory.CreateTempSubdirectory("passenger-files-").FullName;
    private readonly string trash = Directory.CreateTempSubdirectory("passenger-trash-").FullName;

    public ExplorerFilesTests()
    {
        File.WriteAllText(Path.Combine(folder, "Shop.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>\n  </PropertyGroup>\n</Project>\n");
        Directory.CreateDirectory(Path.Combine(folder, "Models"));
        Trash.Folder = trash;
    }

    public void Dispose()
    {
        Trash.Folder = null;
        Directory.Delete(folder, recursive: true);
        Directory.Delete(trash, recursive: true);
    }

    [AvaloniaFact]
    public void New_class_rename_and_delete_change_the_disk_the_tabs_and_the_explorer()
    {
        MainWindow window = new() { Width = 1200, Height = 800 };
        window.Show();
        window.OpenFolder(folder);
        Ui.Settle();
        string models = PathNames.Canonical(Path.Combine(folder, "Models"));

        // New Class: the folder's namespace under the project's, and the file open.
        Assert.Null(window.CreateClass(models, "Order"));
        string order = Path.Combine(models, "Order.cs");
        Assert.Equal("namespace Shop.Models;\n\npublic class Order\n{\n}\n", File.ReadAllText(order));
        Assert.Equal("Order.cs", window.EditorArea.Active?.Name);
        Assert.Contains(window.ExplorerView.Tree.Rows, r => r.Name == "Order.cs");
        Assert.Equal("Order.cs is already there", window.CreateFile(models, "Order.cs", string.Empty));

        // A rename moves the file, and its tab shows the new one.
        Assert.Null(window.Rename(order, "Purchase.cs"));
        string purchase = Path.Combine(models, "Purchase.cs");
        Assert.True(File.Exists(purchase));
        Assert.False(File.Exists(order));
        Assert.Equal(["Purchase.cs"], window.EditorArea.Documents.Select(d => d.Name));

        // A folder's rename takes its open files along.
        Assert.Null(window.Rename(models, "Domain"));
        Assert.Equal(PathNames.Canonical(Path.Combine(folder, "Domain", "Purchase.cs")), Assert.Single(window.EditorArea.Documents).Path);

        // A delete goes to the Trash and closes the tab.
        window.Confirm = (_, _, _) => Task.FromResult(true);
        string moved = Path.Combine(folder, "Domain", "Purchase.cs");
        Task<bool> deleting = window.DeleteAsync(PathNames.Canonical(moved));
        Ui.WaitFor(() => deleting.IsCompleted, "the delete");
        Assert.True(deleting.Result);
        Assert.False(File.Exists(moved));
        Assert.True(File.Exists(Path.Combine(trash, "Purchase.cs")));
        Assert.Empty(window.EditorArea.Documents);
        Assert.DoesNotContain(window.ExplorerView.Tree.Rows, r => r.Name == "Purchase.cs");
        window.Close();
    }
}
