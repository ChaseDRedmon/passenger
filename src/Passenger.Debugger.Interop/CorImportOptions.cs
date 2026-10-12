namespace ICorDebugSharp;

/// <summary>
/// <para>Contains flag values that control the behavior during importation of an assembly outside the current scope.</para>
/// </summary>
/// <remarks>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/corimportoptions-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorImportOptions
{
	/// <summary>
	/// <para>Indicates the default behavior, which is to skip deleted records.</para>
	/// </summary>
	MDImportOptionDefault = 0,
	/// <summary>
	/// <para>Indicates that all metadata should be enumerated.</para>
	/// </summary>
	MDImportOptionAll = -1,
	/// <summary>
	/// <para>Indicates that all TypeDefs, including deleted ones, should be enumerated.</para>
	/// </summary>
	MDImportOptionAllTypeDefs = 1,
	/// <summary>
	/// <para>Indicates that all MethodDefs, including deleted ones, should be enumerated.</para>
	/// </summary>
	MDImportOptionAllMethodDefs = 2,
	/// <summary>
	/// <para>Indicates that all FieldDefs, including deleted ones, should be enumerated.</para>
	/// </summary>
	MDImportOptionAllFieldDefs = 4,
	/// <summary>
	/// <para>Indicates that all PropertyDefs, including deleted ones, should be enumerated.</para>
	/// </summary>
	MDImportOptionAllProperties = 8,
	/// <summary>
	/// <para>Indicates that all EventDefs, including deleted ones, should be enumerated.</para>
	/// </summary>
	MDImportOptionAllEvents = 16,
	/// <summary>
	/// <para>Indicates that all custom attributes, including deleted ones, should be enumerated.</para>
	/// </summary>
	MDImportOptionAllCustomAttributes = 32,
	/// <summary>
	/// <para>Indicates that all exported types, including deleted ones, should be enumerated.</para>
	/// </summary>
	MDImportOptionAllExportedTypes = 64
}
