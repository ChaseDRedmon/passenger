using System.ComponentModel;

namespace XamlBreakpointsApp;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private string caption = "Hello";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Caption
    {
        get => caption;
        set
        {
            caption = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Caption)));
        }
    }

    public IReadOnlyList<string> Items { get; } = ["One", "Two", "Three"];
}
