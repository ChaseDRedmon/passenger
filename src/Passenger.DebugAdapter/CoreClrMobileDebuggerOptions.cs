namespace SharpDbg.Application;

public class CoreClrMobileDebuggerOptions
{
    public required string Address { get; set; }
    public required int Port { get; set; }
    public required string Platform { get; set; }
	public required bool IsServer { get; set; }
    public required string MscordbiPath { get; set; }
    public required string AssembliesPath { get; set; }
}
