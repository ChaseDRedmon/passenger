using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Passenger.App;

/// <summary>Rider's autosave: an edit is never lost and closing never asks. Every changed file is saved before the
/// window closes (quitting included) or another folder opens, and, each as Settings says, when the focus goes to
/// another program or to the terminal and once the IDE has been idle a while.</summary>
public partial class MainWindow
{
    private readonly DispatcherTimer idleSave = new();

    /// <summary>Whether changed files are saved without being asked; off for a screenshot, which only looks.</summary>
    public bool Autosaves { get; set; } = true;

    private void AddAutosave()
    {
        idleSave.Tick += (_, _) =>
        {
            idleSave.Stop();
            if (settings.SaveWhenIdle)
            {
                Autosave();
            }
        };
        // Leaving Passenger saves; going to one of its own windows (a dialog, Settings, a diff) does not, as in Rider.
        Deactivated += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (!OwnWindowActive())
            {
                SaveOnLeaving();
            }
        }, DispatcherPriority.Background);
        terminal!.View.GotFocus += (_, _) => SaveOnLeaving();
        // Any key or click is activity, which the idle wait starts again from.
        AddHandler(KeyDownEvent, (_, _) => RestartIdleSave(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerPressedEvent, (_, _) => RestartIdleSave(), RoutingStrategies.Tunnel, handledEventsToo: true);
        Editors.Opened += (_, document) => document.Text.Changed += (_, _) => RestartIdleSave();
    }

    private void SaveOnLeaving()
    {
        if (settings.SaveOnDeactivation)
        {
            Autosave();
        }
    }

    private void RestartIdleSave()
    {
        idleSave.Stop();
        if (settings.SaveWhenIdle && Editors.Documents.Any(d => d.IsModified))
        {
            idleSave.Interval = TimeSpan.FromSeconds(Math.Max(1, settings.IdleSeconds));
            idleSave.Start();
        }
    }

    /// <summary>Saves before anything else hears of the close, so the language server and the rest go only once the
    /// edits are on disk; a file that cannot be written keeps the window open, its edits with it.</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!Autosave())
        {
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }

    /// <summary>Saves every changed file but those awaiting a File Cache Conflict answer, unless this window does not
    /// autosave; whether nothing was left unsaved.</summary>
    private bool Autosave() => !Autosaves || (Editors.SaveAll(conflicted) && conflicted.Count == 0);

    /// <summary>Whether one of Passenger's own windows has the focus (this one, a dialog over it, another window).</summary>
    private bool OwnWindowActive() =>
        IsActive || OwnedWindows.Any(w => w.IsActive)
        || (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.Windows.Any(w => w.IsActive));
}
