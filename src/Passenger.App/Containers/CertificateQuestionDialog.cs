using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Passenger.App.Containers;

/// <summary>The question a container run asks when services of it serve HTTPS and this machine's ASP.NET Core development
/// certificate is missing or not trusted: Create and Trust (the default), which has <c>dotnet dev-certs https --trust</c>
/// make and trust one (the system may ask too), or Run over HTTP, this run's services serving HTTP where they would have
/// served HTTPS. Escape and closing answer Run over HTTP.</summary>
public sealed class CertificateQuestionDialog : Window
{
    public const string CreateAndTrust = "Create and Trust";
    public const string RunOverHttp = "Run over HTTP";

    public CertificateQuestionDialog(string configuration, string services, bool missing)
    {
        Title = missing ? "No HTTPS development certificate" : "The HTTPS development certificate is not trusted";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        CreateButton = new Button { Content = CreateAndTrust, IsDefault = true, Classes = { "primary" } };
        HttpButton = new Button { Content = RunOverHttp, IsCancel = true };
        CreateButton.Click += (_, _) => Close(true);
        HttpButton.Click += (_, _) => Close(false);

        Content = DialogFooter.Page(new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = Title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock
                {
                    Text = $"{services} of {configuration} {(services.Contains(',', StringComparison.Ordinal) ? "serve" : "serves")} HTTPS, and this computer's "
                        + $"ASP.NET Core development certificate {(missing ? "is not there" : "is not trusted")}, so a browser would refuse it.",
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = "Create and Trust has dotnet dev-certs make the certificate and trust it, which the system may ask you to allow. "
                        + "Run over HTTP serves these services over HTTP on the same ports for this run.",
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        }, DialogFooter.Bar(DialogFooter.Answers(CreateButton, HttpButton)));
    }

    public Button CreateButton { get; }

    public Button HttpButton { get; }
}
