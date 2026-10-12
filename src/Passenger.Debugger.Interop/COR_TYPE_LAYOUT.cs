namespace ICorDebugSharp;

/// <summary>
/// <para>Provides information about the layout of an object in memory.</para>
/// </summary>
/// <remarks>
/// <para>If <c>numFields</c> is greater than zero, you can call the <see cref="M:ICorDebugSharp.ICorDebugProcess5.TryGetTypeFields(ICorDebugSharp.COR_TYPEID,System.UInt32,ICorDebugSharp.COR_FIELD[],System.UInt32@)">ICorDebugProcess5::GetTypeFields</see> method to obtain information about the fields in this type. If <c>type</c> is <c>ELEMENT_TYPE_STRING</c>, <c>ELEMENT_TYPE_ARRAY</c>, or <c>ELEMENT_TYPE_SZARRAY</c>, the size of objects of this type is variable, and you can pass the <see cref="T:ICorDebugSharp.COR_TYPEID">COR_TYPEID</see> structure to the <see cref="M:ICorDebugSharp.ICorDebugProcess5.TryGetArrayLayout(ICorDebugSharp.COR_TYPEID,ICorDebugSharp.COR_ARRAY_LAYOUT@)">ICorDebugProcess5::GetArrayLayout</see> method.</para>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/debugging/icordebug/cor-type-layout-structure">Microsoft Learn</see>.</para>
/// </remarks>
public struct COR_TYPE_LAYOUT
{
	/// <summary>
	/// <para>The identifier of the parent type to this type. This will be the NULL type id (token1= 0, token2 = 0) if the type id corresponds to .</para>
	/// </summary>
	public COR_TYPEID parentID;

	/// <summary>
	/// <para>The base size of an object of this type. This is the total size for non-variable sized objects.</para>
	/// </summary>
	public uint objectSize;

	/// <summary>
	/// <para>The number of fields included in objects of this type.</para>
	/// </summary>
	public uint numFields;

	/// <summary>
	/// <para>If this type is boxed, the beginning offset of an object's fields. This field is valid only for value types such as primitives and structures.</para>
	/// </summary>
	public uint boxOffset;

	/// <summary>
	/// <para>The CorElementType to which this type belongs.</para>
	/// </summary>
	public CorElementType type;
}
