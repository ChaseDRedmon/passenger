namespace BindingErrorsApp;

/// <summary>What the window binds to; its caption's binding misspells Name.</summary>
public sealed class MainViewModel
{
    public string Greeting => "Hello";

    public string Name { get; set; } = "Ada";

    public int Age { get; set; } = 36;
}
