using Passenger.Dap;
using Passenger.Tests.Debugging.Helpers;

namespace Passenger.Tests.Debugging;

[Collection(DebugSession.Collection)]
public class ProtocolTests(ITestOutputHelper testOutputHelper)
{
    [Fact]
    public void UnsupportedRequests_ReturnErrorsWithoutStoppingDispatcher()
    {
        using DebugSession host = DebugSession.ForLaunch(testOutputHelper);

        DapException unimplementedError = Assert.Throws<DapException>(() => host.Request("setVariable", new { variablesReference = 0, name = "value", value = "1" }));
        Assert.Equal("Request 'setVariable' is not supported.", unimplementedError.Reason);

        DapException unknownError = Assert.Throws<DapException>(() => host.Request("unknownRequest"));
        Assert.Equal("Request 'unknownRequest' is not supported.", unknownError.Reason);

        DapCapabilities capabilities = host.Request<DapCapabilities>("initialize", DebugSession.InitializeArguments);
        Assert.True(capabilities.SupportsConfigurationDoneRequest);
    }
}
