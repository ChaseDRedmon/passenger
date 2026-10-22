using Avalonia.Controls;

namespace BindingErrorsApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Flood.Fill(this);
    }
}
