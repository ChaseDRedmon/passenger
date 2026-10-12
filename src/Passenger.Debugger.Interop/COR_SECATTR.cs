namespace Passenger.Debugger.Interop;

public struct COR_SECATTR
{
    public uint tkCtor;

    public unsafe void* pCustomAttribute;

    public uint cbCustomAttribute;
}
