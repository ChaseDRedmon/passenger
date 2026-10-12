namespace ICorDebugSharp;

public readonly record struct mdModuleRef
{
	/// <summary>Gets the Row ID of the metadata token.</summary>
	public int Rid => (int)(Value & 0xFFFFFF);

	public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

	/// <summary>Gets the raw numeric value of the metadata token.</summary>
	public uint Value { get; }

	public bool IsNil => Rid == 0;

	public static readonly mdModuleRef Nil = new mdModuleRef(436207616u);

	public mdModuleRef(uint value)
	{
		Value = value;
	}

	public static implicit operator mdModuleRef(int value)
	{
		return new mdModuleRef((uint)value);
	}

	public static implicit operator int(mdModuleRef value)
	{
		return (int)value.Value;
	}

	public static implicit operator uint(mdModuleRef value)
	{
		return value.Value;
	}

	public static implicit operator mdToken(mdModuleRef value)
	{
		return new mdToken(value.Value);
	}
}
