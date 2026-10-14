using System.Windows;
using System.Windows.Input;

namespace WpfTreeApp;

public partial class MainWindow : Window
{
    private int clicks;

    public MainWindow()
    {
        InitializeComponent();
        List.ItemsSource = Enumerable.Range(1, 40).Select(i => $"Item {i}").ToList();
        DataContext = new MainViewModel();
    }

    private void OnCount(object sender, RoutedEventArgs e)
    {
        clicks++;
        Counter.Text = clicks == 1 ? "Clicked once" : $"Clicked {clicks} times";
    }

    private void OnNameKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Hello.Text = $"Hello, {NameBox.Text}";
        }
    }
}
