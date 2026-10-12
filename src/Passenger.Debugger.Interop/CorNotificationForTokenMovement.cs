namespace ICorDebugSharp;

/// <summary>
/// <para>Specifies the notifications that will be sent to the metadata API client when a token remap occurs.</para>
/// </summary>
/// <remarks>
/// <para>A token may be re-mapped (that is, moved) during a metadata merge.</para>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/enumerations/cornotificationfortokenmovement-enumeration">Microsoft Learn</see>.</para>
/// </remarks>
public enum CorNotificationForTokenMovement
{
	/// <summary>
	/// <para>Notify when <c>mdTypeRef</c>, <c>mdMethodDef</c>, <c>mdMemberRef</c>, or <c>mdFieldDef</c> tokens move.</para>
	/// </summary>
	MDNotifyDefault = 15,
	/// <summary>
	/// <para>Notify when any token moves.</para>
	/// </summary>
	MDNotifyAll = -1,
	/// <summary>
	/// <para>Do not notify when tokens move.</para>
	/// </summary>
	MDNotifyNone = 0,
	/// <summary>
	/// <para>Notify when an <c>mdMethodDef</c> token moves.</para>
	/// </summary>
	MDNotifyMethodDef = 1,
	/// <summary>
	/// <para>Notify when an <c>mdMemberRef</c> token moves.</para>
	/// </summary>
	MDNotifyMemberRef = 2,
	/// <summary>
	/// <para>Notify when an <c>mdFieldDef</c> token moves.</para>
	/// </summary>
	MDNotifyFieldDef = 4,
	/// <summary>
	/// <para>Notify when an <c>mdTypeRef</c> token moves.</para>
	/// </summary>
	MDNotifyTypeRef = 8,
	/// <summary>
	/// <para>Notify when an <c>mdTypeDef</c> token moves.</para>
	/// </summary>
	MDNotifyTypeDef = 16,
	/// <summary>
	/// <para>Notify when an <c>mdParamDef</c> token moves.</para>
	/// </summary>
	MDNotifyParamDef = 32,
	/// <summary>
	/// <para>Notify when an <c>mdInterfaceImpl</c> token moves.</para>
	/// </summary>
	MDNotifyInterfaceImpl = 64,
	/// <summary>
	/// <para>Notify when an <c>mdProperty</c> token moves.</para>
	/// </summary>
	MDNotifyProperty = 128,
	/// <summary>
	/// <para>Notify when an <c>mdEvent</c> token moves.</para>
	/// </summary>
	MDNotifyEvent = 256,
	/// <summary>
	/// <para>Notify when an <c>mdSignature</c> token moves.</para>
	/// </summary>
	MDNotifySignature = 512,
	/// <summary>
	/// <para>Notify when an <c>mdTypeSpec</c> token moves.</para>
	/// </summary>
	MDNotifyTypeSpec = 1024,
	/// <summary>
	/// <para>Notify when an <c>mdCustomAttribute</c> token moves.</para>
	/// </summary>
	MDNotifyCustomAttribute = 2048,
	/// <summary>
	/// <para>Notify when an <c>mdSecurityValue</c> token moves.</para>
	/// </summary>
	MDNotifySecurityValue = 4096,
	/// <summary>
	/// <para>Notify when an <c>mdPermission</c> token moves.</para>
	/// </summary>
	MDNotifyPermission = 8192,
	/// <summary>
	/// <para>Notify when an <c>mdModuleRef</c> token moves.</para>
	/// </summary>
	MDNotifyModuleRef = 16384,
	/// <summary>
	/// <para>Notify when an <c>mdNameSpace</c> token moves.</para>
	/// </summary>
	MDNotifyNameSpace = 32768,
	/// <summary>
	/// <para>Notify when an <c>mdAssemblyRef</c> token moves.</para>
	/// </summary>
	MDNotifyAssemblyRef = 16777216,
	/// <summary>
	/// <para>Notify when an <c>mdFile</c> token moves.</para>
	/// </summary>
	MDNotifyFile = 33554432,
	/// <summary>
	/// <para>Notify when an <c>mdExportedType</c> token moves.</para>
	/// </summary>
	MDNotifyExportedType = 67108864,
	/// <summary>
	/// <para>Notify when an <c>mdManifestResource</c> token moves.</para>
	/// </summary>
	MDNotifyResource = 134217728
}
