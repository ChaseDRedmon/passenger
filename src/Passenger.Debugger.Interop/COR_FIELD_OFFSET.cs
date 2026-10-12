namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>Stores the offset, within a class, of the specified field.</para>
/// </summary>
/// <remarks>
/// <para><see cref="M:Passenger.Debugger.Interop.IMetaDataImport.TryGetClassLayout(Passenger.Debugger.Interop.mdTypeDef,System.UInt32@,Passenger.Debugger.Interop.COR_FIELD_OFFSET@,System.UInt32,System.UInt32@,System.UInt32@)">IMetaDataImport::GetClassLayout</see> and <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/interfaces/imetadataemit-setclasslayout-method">IMetaDataEmit::SetClassLayout</see> methods take a parameter of type <c>COR_FIELD_OFFSET</c>.</para>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/structures/cor-field-offset-structure">Microsoft Learn</see>.</para>
/// </remarks>
public struct COR_FIELD_OFFSET
{
    /// <summary>
    /// <para>An <c>mdFieldDef</c> metadata token that represents the field.</para>
    /// </summary>
    public uint ridOfField;

    /// <summary>
    /// <para>The field's offset within its class.</para>
    /// </summary>
    public uint ulOffset;
}
