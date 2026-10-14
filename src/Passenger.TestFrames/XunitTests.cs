using System.Reflection;

namespace Passenger.TestFrames;

/// <summary>xunit v3's test context (TestContext.Current), read by reflection, as the test program's own xunit is the one
/// running: which test is executing on this thread, and its attachments and output.</summary>
internal sealed class XunitTests
{
    /// <summary>The assembly of xunit v3's runner and test context.</summary>
    public const string Core = "xunit.v3.core";

    /// <summary>The message an exception of xunit's dynamic skip (Assert.Skip) carries first: a test skipped, not failed.</summary>
    private const string SkipMarker = "$XunitDynamicSkip$";

    private readonly PropertyInfo current;
    private readonly PropertyInfo stage;
    private readonly PropertyInfo test;
    private readonly PropertyInfo state;
    private readonly PropertyInfo output;
    private readonly MethodInfo attach;

    private XunitTests(PropertyInfo current, PropertyInfo stage, PropertyInfo test, PropertyInfo state, PropertyInfo output, MethodInfo attach)
    {
        this.current = current;
        this.stage = stage;
        this.test = test;
        this.state = state;
        this.output = output;
        this.attach = attach;
    }

    /// <summary>xunit v3's test context in an assembly, if it is xunit's and has what the hook reads.</summary>
    public static XunitTests? In(Assembly assembly)
    {
        if (assembly.GetName().Name != Core || assembly.GetType("Xunit.ITestContext") is not { } context)
        {
            return null;
        }
        PropertyInfo? current = assembly.GetType("Xunit.TestContext")?.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
        PropertyInfo? stage = context.GetProperty("PipelineStage");
        PropertyInfo? test = context.GetProperty("Test");
        PropertyInfo? state = context.GetProperty("TestState");
        PropertyInfo? output = context.GetProperty("TestOutputHelper");
        MethodInfo? attach = context.GetMethod("AddAttachment", [typeof(string), typeof(byte[]), typeof(string)]);
        return current is null || stage is null || test is null || state is null || output is null || attach is null
            ? null
            : new XunitTests(current, stage, test, state, output, attach);
    }

    /// <summary>The test whose code is running on this thread: its method (or its class's constructor) under way, before xunit
    /// has a result for it; null outside a test, or once its method is done (its cleanup).</summary>
    public XunitTest? Running()
    {
        object? context = current.GetValue(null);
        if (context is null || stage.GetValue(context)?.ToString() != "TestExecution" || test.GetValue(context) is not { } running || state.GetValue(context) is not null)
        {
            return null;
        }
        return new XunitTest(this, context, running);
    }

    /// <summary>Whether an exception is xunit's dynamic skip, or one made of it by a call through reflection.</summary>
    public static bool IsSkip(Exception exception) =>
        (exception is TargetInvocationException { InnerException: { } inner } ? inner : exception).Message.StartsWith(SkipMarker, StringComparison.Ordinal);

    /// <summary>A PNG added to the test's attachments under a name; false when the test has one by that name already.</summary>
    public bool Attach(object context, string name, byte[] png)
    {
        try
        {
            attach.Invoke(context, BindingFlags.DoNotWrapExceptions, null, [name, png, "image/png"], null);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>A line in the test's output.</summary>
    public void Say(object context, string line)
    {
        if (output.GetValue(context) is { } helper)
        {
            helper.GetType().GetMethod("WriteLine", [typeof(string)])?.Invoke(helper, BindingFlags.DoNotWrapExceptions, null, [line], null);
        }
    }
}

/// <summary>One test xunit is running, and its context.</summary>
internal sealed record XunitTest(XunitTests Xunit, object Context, object Test)
{
    public bool Attach(string name, byte[] png) => Xunit.Attach(Context, name, png);

    public void Say(string line) => Xunit.Say(Context, line);
}
