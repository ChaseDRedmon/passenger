using Avalonia.Interactivity;
using Avalonia.Threading;
using Passenger.Editor;

namespace Passenger.App;

/// <summary>A chord's first key as Rider holds it: the status bar says it was pressed and what its second key can run,
/// and it is forgotten after two seconds, on a click, when the focus moves and when the window is left, so a key pressed
/// later is a key of its own rather than the end of a chord begun before.</summary>
public partial class MainWindow
{
    /// <summary>How long a chord's first key waits for its second: Rider's actionSystem.secondKeystrokeTimeout.</summary>
    private static readonly TimeSpan ChordWait = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer chordWait = new() { Interval = ChordWait };
    private string? chordMessage;
    private string? beforeChord;

    private void AddChords()
    {
        chordWait.Tick += (_, _) => keys.Reset();
        keys.PendingChanged += (_, _) => ShowChord();
        AddHandler(PointerPressedEvent, (_, _) => keys.Reset(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(GotFocusEvent, (_, _) => keys.Reset(), RoutingStrategies.Bubble, handledEventsToo: true);
        Deactivated += (_, _) => keys.Reset();
    }

    /// <summary>The chord's message in the status bar while it waits, and the message it covered back once it ends.</summary>
    private void ShowChord()
    {
        chordWait.Stop();
        if (keys.Pending is { } first)
        {
            beforeChord = StatusText.Text;
            chordMessage = ChordMessage(first);
            StatusText.Text = chordMessage;
            chordWait.Start();
            return;
        }
        if (chordMessage is not null && StatusText.Text == chordMessage)
        {
            StatusText.Text = beforeChord;
        }
        chordMessage = null;
    }

    /// <summary>Rider's "Prefix key pressed: Comment (C), Uncomment (U)": each action the second key can run, with the
    /// first key it was given.</summary>
    private string ChordMessage(Keystroke first) => "Prefix key pressed: " + string.Join(", ", keys.Keymap.ChordsFrom(first)
        .DistinctBy(chord => chord.Action)
        .Select(chord => $"{actions![chord.Action]?.Title ?? chord.Action} ({chord.Second.Display()})"));
}
