using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AgentApp;

public partial class MainWindow : Window
{
    private int clicks;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new Person();
        for (int i = 1; i <= 30; i++)
        {
            Items.Children.Add(new TextBlock { Text = $"Item {i}" });
        }
    }

    private void OnCount(object? sender, RoutedEventArgs e)
    {
        clicks++;
        Counter.Text = clicks == 1 ? "Clicked once" : $"Clicked {clicks} times";
    }

    private void OnNameKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Greeting.Text = $"Hello, {NameBox.Text}";
            e.Handled = true;
        }
    }
}

/// <summary>What the window binds to; its caption's binding misspells Name.</summary>
public sealed class Person
{
    public string Name { get; set; } = "Ada";
}
