namespace ICorDebugSharp;

public sealed class ControlCTrapCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs
{
	public ICorDebugProcess Process { get; }

	public ControlCTrapCorDebugManagedCallbackEventArgs(ICorDebugProcess pProcess)
	{
		Process = pProcess;
	}
}
