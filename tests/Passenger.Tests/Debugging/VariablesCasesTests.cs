using Passenger.Dap;
using Passenger.Tests.Debugging.Helpers;

namespace Passenger.Tests.Debugging;

/// <summary>What the Locals scope lists in the shapes Passenger's tests add (PassengerCases).</summary>
[Collection(DebugSession.Collection)]
public class VariablesCasesTests(ITestOutputHelper testOutputHelper)
{
    [Fact]
    public async Task A_parameter_a_lambda_captures_is_listed_once_with_its_live_value()
    {
        using DebugSession host = await DebugSession.LaunchToBreakpoint(testOutputHelper, "PassengerCases", 40, []);

        StoppedEvent stop = await host.WaitForStoppedEvent();
        Assert.Equal(40, stop.ReadStopInfo().line);
        List<DapVariable> locals = host.Locals(stop);
        Assert.Equal("41", Assert.Single(locals, v => v.Name == "captured").Value);
        Assert.Single(locals, v => v.Name == "f");
    }
}
