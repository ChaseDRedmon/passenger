using Avalonia.Interactivity;

namespace Passenger.App;

/// <summary>The window's share of IDE Internal Errors: the status bar's red mark counting what was caught, its popup,
/// and the C# server's restarts said where the person looks.</summary>
public partial class MainWindow
{
    private void AddErrors()
    {
        IdeErrors.Install();
        IdeErrors.Raised += OnIdeError;
        Closed += (_, _) => IdeErrors.Raised -= OnIdeError;
        ShowErrorCount();
    }

    private void OnIdeError(object? sender, IdeError error)
    {
        ShowErrorCount();
        StatusText.Text = $"IDE error: {error.Message}";
    }

    /// <summary>The different errors listed, a repeat counting once; more past the list's limit, which only the log has.</summary>
    private void ShowErrorCount()
    {
        int count = IdeErrors.Errors.Count;
        ErrorsButton.IsVisible = count > 0;
        ErrorsCount.Text = IdeErrors.Unlisted > 0 ? $"{count}+" : count.ToString();
    }

    private void OnErrorsClicked(object? sender, RoutedEventArgs e) => ShowErrors();

    /// <summary>The errors caught since the last Clear All, the latest chosen.</summary>
    public ErrorsPopup ShowErrors()
    {
        ErrorsPopup popup = new(IdeErrors.Errors, IdeErrors.Unlisted);
        popup.Done += (_, _) =>
        {
            ClosePopup();
            ShowErrorCount();
        };
        ShowPopup(popup);
        return popup;
    }
}
