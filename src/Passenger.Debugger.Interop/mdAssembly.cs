namespace ICorDebugSharp;

public readonly record struct mdAssembly
{
	/// <summary>Gets the Row ID of the metadata token.</summary>
	public int Rid => (int)(Value & 0xFFFFFF);

	public CorTokenType Type => (CorTokenType)((int)Value & -16777216);

	/// <summary>Gets the raw numeric value of the metadata token.</summary>
	public uint Value { get; }

	public bool IsNil => Rid == 0;

	public static readonly mdAssembly Nil = new mdAssembly(536870912u);

	public mdAssembly(uint value)
	{
		Value = value;
	}

	public static implicit operator mdAssembly(int value)
	{
		return new mdAssembly((uint)value);
	}

	public static implicit operator int(mdAssembly value)
	{
		return (int)value.Value;
	}

	public static implicit operator uint(mdAssembly value)
	{
		return value.Value;
	}

	public static implicit operator mdToken(mdAssembly value)
	{
		return new mdToken(value.Value);
	}
}
