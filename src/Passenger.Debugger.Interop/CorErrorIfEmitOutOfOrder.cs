namespace ICorDebugSharp;

/// <summary>
/// <para>Contains flag values that indicate the conditions under which an error message should be generated when metadata is emitted out of order.</para>
/// </summary>
/// <remarks>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corerrorifemitoutoforder-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorErrorIfEmitOutOfOrder
{
	/// <summary>
	/// <para>Indicates the default behavior, which does not generate error messages.</para>
	/// </summary>
	MDErrorOutOfOrderDefault = 0,
	/// <summary>
	/// <para>Indicates that the compiler should not generate error messages.</para>
	/// </summary>
	MDErrorOutOfOrderNone = MDErrorOutOfOrderDefault,
	/// <summary>
	/// <para>Indicates that the compiler should generate an error message when a field, property, event, method, or parameter is emitted out of order.</para>
	/// </summary>
	MDErrorOutOfOrderAll = -1,
	/// <summary>
	/// <para>Indicates that the compiler should generate an error message when a method is emitted out of order.</para>
	/// </summary>
	MDMethodOutOfOrder = 1,
	/// <summary>
	/// <para>Indicates that the compiler should generate an error message when a field is emitted out of order.</para>
	/// </summary>
	MDFieldOutOfOrder = 2,
	/// <summary>
	/// <para>Indicates that the compiler should generate an error message when a parameter is emitted out of order.</para>
	/// </summary>
	MDParamOutOfOrder = 4,
	/// <summary>
	/// <para>Indicates that the compiler should generate an error message when a property is emitted out of order.</para>
	/// </summary>
	MDPropertyOutOfOrder = 8,
	/// <summary>
	/// <para>Indicates that the compiler should generate an error message when an event is emitted out of order.</para>
	/// </summary>
	MDEventOutOfOrder = 16
}
