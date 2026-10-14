using Avalonia.Controls;

namespace BindingValuesApp;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    /// <summary>"rename": the customer's name changes, Ada's to Grace's and back, which its binding shows.</summary>
    public void Rename()
    {
        Customer customer = ((MainViewModel)DataContext!).Customer;
        customer.Name = customer.Name == "Ada Lovelace" ? "Grace Hopper" : "Ada Lovelace";
        Console.WriteLine($"Renamed {customer.Name}");
    }
}
