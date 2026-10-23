using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;

namespace Passenger.App;

/// <summary>The one piece of MVVM the shell needs: a property that tells its bindings it changed.</summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Has a control that shows this hear its changes while it is on screen, and once more each time it comes back
    /// (a change of every property, a null name): a list's row made by a template that does not recycle is made anew each
    /// time it scrolls into view, and one heard all along was kept, and told of every change, by an item that outlives it.</summary>
    public void FollowWhileShown(Visual shown, PropertyChangedEventHandler changed)
    {
        shown.AttachedToVisualTree += (_, _) =>
        {
            PropertyChanged += changed;
            changed(this, new PropertyChangedEventArgs(null));
        };
        shown.DetachedFromVisualTree += (_, _) => PropertyChanged -= changed;
    }

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
