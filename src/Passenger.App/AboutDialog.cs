using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Passenger.App;

/// <summary>Rider's About: the version, the commit it was built from and the runtime it runs on beside Passenger's icon;
/// the open-source software it carries (its notices) and its own licence a click away; Copy and Close for a bug report.
/// A link closes it and hands its file to <c>open</c>.</summary>
public sealed class AboutDialog : Window
{
    public const string NoticesLink = "open-source software";
    public const string LicenseLink = "MIT License";

    public AboutDialog(Action<string> open)
    {
        Title = "About Passenger";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Bind(BackgroundProperty, this.GetResourceObservable("Passenger.Chrome"));

        string commit = Product.Commit is { } hash ? $"Commit {hash[..Math.Min(12, hash.Length)]}" : "Built outside a Git repository";
        Text = $"Passenger {Product.Version}\n{(Product.Commit is { } full ? $"Commit {full}" : commit)}\nRuntime version: {Product.Runtime}\n{RuntimeInformation.OSDescription}";

        StackPanel facts = new()
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = $"Passenger {Product.Version}", FontSize = 20, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) },
                new TextBlock { Text = commit },
                new TextBlock { Text = $"Runtime version: {Product.Runtime}" },
                Sentence("Powered by ", Link(NoticesLink, Product.Notices), new Thickness(0, 14, 0, 0)),
                Sentence("Licensed under the ", Link(LicenseLink, Product.License), default),
            },
        };
        if (Copyright() is { } copyright)
        {
            facts.Children.Add(new TextBlock { Classes = { "muted" }, Text = copyright, Margin = new Thickness(0, 14, 0, 0) });
        }

        Button copy = new() { Content = "Copy and Close" };
        Button close = new() { Content = "Close", IsDefault = true, IsCancel = true, Classes = { "primary" } };
        copy.Click += async (_, _) =>
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(Text);
            }
            Close();
        };
        close.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(24, 22, 20, 16),
            Spacing = 20,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 20,
                    Children =
                    {
                        // The icon's own rounded square (passenger.svg's, inset a sixteenth with a fifth's radius): QuickLook
                        // drew passenger.png onto white, which its corners keep.
                        new Border
                        {
                            Width = 56,
                            Height = 56,
                            Margin = new Thickness(4),
                            CornerRadius = new CornerRadius(12.5),
                            ClipToBounds = true,
                            VerticalAlignment = VerticalAlignment.Top,
                            Child = new Image
                            {
                                Source = new Bitmap(AssetLoader.Open(new Uri("avares://Passenger/Assets/passenger.png"))),
                                Width = 64,
                                Height = 64,
                                Margin = new Thickness(-4),
                            },
                        },
                        facts,
                    },
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { copy, close },
                },
            },
        };

        Button Link(string words, string path)
        {
            Button link = new() { Classes = { "link" }, Content = words, IsEnabled = File.Exists(path) };
            link.Click += (_, _) =>
            {
                Close();
                open(path);
            };
            return link;
        }
    }

    /// <summary>What Copy and Close copies, as Rider's does for a bug report: the version, the full commit, the runtime
    /// and the system.</summary>
    public string Text { get; }

    /// <summary>Running text with a link in it.</summary>
    private static StackPanel Sentence(string words, Button link, Thickness margin) => new()
    {
        Orientation = Orientation.Horizontal,
        Margin = margin,
        Children = { new TextBlock { Text = words, VerticalAlignment = VerticalAlignment.Center }, link },
    };

    /// <summary>Passenger's copyright as its licence states it, with the sign for "(c)".</summary>
    private static string? Copyright()
    {
        try
        {
            return File.ReadLines(Product.License).FirstOrDefault(l => l.StartsWith("Copyright", StringComparison.Ordinal))?.Replace("(c)", "©", StringComparison.Ordinal).Trim();
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
