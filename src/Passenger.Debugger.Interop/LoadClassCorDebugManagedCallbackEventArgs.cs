namespace ICorDebugSharp;

public sealed class LoadClassCorDebugManagedCallbackEventArgs : CorDebugManagedCallbackEventArgs
{
	public ICorDebugAppDomain AppDomain { get; }

	public ICorDebugClass C { get; }

	public LoadClassCorDebugManagedCallbackEventArgs(ICorDebugAppDomain pAppDomain, ICorDebugClass c)
	{
		AppDomain = pAppDomain;
		C = c;
	}
}
