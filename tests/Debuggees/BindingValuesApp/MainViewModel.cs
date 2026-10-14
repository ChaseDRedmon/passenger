using System.ComponentModel;

namespace BindingValuesApp;

/// <summary>What the window binds to: its title, a customer whose name and orders it shows, and tags a list makes a row of each.</summary>
public sealed class MainViewModel
{
    public string Title => "Binding Values";

    public Customer Customer { get; } = new();

    public IReadOnlyList<string> Tags { get; } = ["One", "Two", "Three"];
}

public sealed class Customer : INotifyPropertyChanged
{
    private string name = "Ada Lovelace";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => name;
        set
        {
            name = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        }
    }

    public int Orders { get; } = 3;
}
