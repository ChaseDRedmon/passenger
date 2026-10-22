using Avalonia.Controls;
using Passenger.App.ToolWindows;

namespace Passenger.App.Aspire;

/// <summary>The native distributed app view and its selected AppHost's own console.</summary>
public sealed class AspirePane : DockPanel, IDisposable
{
    public AspirePane()
    {
        Children.Add(View);
        PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty && !IsVisible)
            {
                View.SetDisplayVisible(false);
            }
        };
    }

    public AspireView View { get; } = new();

    public void SetConsole(OutputView? output) => View.SetAppHostConsole(output);
    public void SetContainerStatus(string text) => View.SetContainerStatus(text);
    public void SetDisplayVisible(bool visible) => View.SetDisplayVisible(visible);
    public void ShowConsole(bool show) => View.ShowPage(show ? "AppHost console" : "Resources");

    public void Dispose() => View.Dispose();
}
