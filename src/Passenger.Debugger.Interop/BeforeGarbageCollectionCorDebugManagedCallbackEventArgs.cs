namespace ICorDebugSharp;

public sealed class BeforeGarbageCollectionCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs
{
	public ICorDebugProcess Process { get; }

	public BeforeGarbageCollectionCorDebugManagedCallbackEventArgs(ICorDebugProcess pProcess)
	{
		Process = pProcess;
	}
}
