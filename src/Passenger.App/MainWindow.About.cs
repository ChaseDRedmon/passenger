using Avalonia.Controls;
using Avalonia.Input;

namespace Passenger.App;

/// <summary>About Passenger, where Rider has it: the application menu's first item on macOS, Help's last elsewhere.</summary>
public partial class MainWindow
{
    private void AddAbout() =>
        actions!.Add(new IdeAction(AppActions.About, "About", "Help", _ => ShowAbout())
        {
            Menus = OperatingSystem.IsMacOS() ? [] : [new MenuPlace("Help", 3, 0)],
        });

    /// <summary>About, over this window until it is closed; its links close it and open the notices or the licence in
    /// an editor here, read-only.</summary>
    public AboutDialog ShowAbout()
    {
        AboutDialog dialog = new(path => OpenFile(path));
        _ = dialog.ShowDialog(this);
        return dialog;
    }

    /// <summary>macOS's application menu, as Rider's: About Passenger, then Settings (Cmd+,); the system adds Services,
    /// Hide and Quit after them.</summary>
    public NativeMenu ApplicationMenu()
    {
        NativeMenuItem about = new("About Passenger") { Command = new MenuCommand(() => Request(AppActions.About)) };
        NativeMenuItem settings = new("Settings...")
        {
            Gesture = new KeyGesture(Key.OemComma, KeyModifiers.Meta),
            Command = new MenuCommand(() => Request(AppActions.ShowSettings)),
        };
        return new NativeMenu { Items = { about, new NativeMenuItemSeparator(), settings } };
    }
}
