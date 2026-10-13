using System.Reflection;
using Passenger.App;
using Xunit.v3;

[assembly: Passenger.Tests.FailOnIdeErrors]

namespace Passenger.Tests;

/// <summary>A window test fails when the IDE caught an error while it ran. The IDE's catcher keeps the app running
/// through an exception (IdeErrors), which would otherwise let a test pass over a command that threw after drawing
/// what the test looks for. Only window tests are checked: they run one at a time, so what was caught is theirs.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class FailOnIdeErrorsAttribute : BeforeAfterTestAttribute
{
    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (IsWindowTest(methodUnderTest))
        {
            IdeErrors.Clear();
        }
    }

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (!IsWindowTest(methodUnderTest) || IdeErrors.Errors is not { Count: > 0 } errors)
        {
            return;
        }
        IdeErrors.Clear();
        throw new InvalidOperationException($"The IDE caught {errors.Count} error{(errors.Count == 1 ? "" : "s")} during the test:\n\n"
            + string.Join("\n\n", errors.Select(e => $"{e.Kind}: {e.Message}{(e.Count > 1 ? $" (occurred {e.Count} times)" : "")}\n{FirstFrames(e.Details)}")));
    }

    private static bool IsWindowTest(MethodInfo method) => TestsLeaveNothingBehindAttribute.IsWindowTest(method);

    private static string FirstFrames(string details) =>
        string.Join('\n', details.Split('\n').Where(l => l.TrimStart().StartsWith("at ", StringComparison.Ordinal)).Take(6));
}
