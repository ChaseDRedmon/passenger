namespace Passenger.Debugger.Interop;

public readonly record struct CORDB_ADDRESS(ulong Value)
{
    public static implicit operator int(CORDB_ADDRESS value)
    {
        return (int)value.Value;
    }
}
