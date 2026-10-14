#nullable enable

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Passenger.Debugger.Interop;

/// <summary>
/// <para>The metadata emitter, of which the debugger uses only the saving of a module's metadata as the runtime holds it now:
/// after Edit and Continue, the debugger's own copy has each update applied, which IMetaDataTables2's storage (the image's
/// bytes) does not. The methods before those are named in cor.h's order and never called.</para>
/// </summary>
/// <remarks>
/// <para>Documentation adapted from <see href="https://learn.microsoft.com/dotnet/core/unmanaged-api/metadata/interfaces/imetadataemit-interface">Microsoft Learn</see>.</para>
/// </remarks>
[GeneratedComInterface]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("BA3FEE4C-ECB9-4e41-83B7-183FA41CD859")]
public partial interface IMetaDataEmit
{
    [PreserveSig]
    int SetModuleProps();

    [PreserveSig]
    int Save();

    [PreserveSig]
    int SaveToStream();

    /// <summary>Gets the size the metadata takes saved; <paramref name="fSave"/> is CorSaveSize's cssAccurate (0) or cssQuick (1).</summary>
    [PreserveSig]
    int TryGetSaveSize(int fSave, out uint pdwSaveSize);

    [PreserveSig]
    int DefineTypeDef();

    [PreserveSig]
    int DefineNestedType();

    [PreserveSig]
    int SetHandler();

    [PreserveSig]
    int DefineMethod();

    [PreserveSig]
    int DefineMethodImpl();

    [PreserveSig]
    int DefineTypeRefByName();

    [PreserveSig]
    int DefineImportType();

    [PreserveSig]
    int DefineMemberRef();

    [PreserveSig]
    int DefineImportMember();

    [PreserveSig]
    int DefineEvent();

    [PreserveSig]
    int SetClassLayout();

    [PreserveSig]
    int DeleteClassLayout();

    [PreserveSig]
    int SetFieldMarshal();

    [PreserveSig]
    int DeleteFieldMarshal();

    [PreserveSig]
    int DefinePermissionSet();

    [PreserveSig]
    int SetRVA();

    [PreserveSig]
    int GetTokenFromSig();

    [PreserveSig]
    int DefineModuleRef();

    [PreserveSig]
    int SetParent();

    [PreserveSig]
    int GetTokenFromTypeSpec();

    /// <summary>Saves the metadata, its size from <see cref="TryGetSaveSize"/>, into a buffer of that size.</summary>
    [PreserveSig]
    int TrySaveToMemory([Out][MarshalUsing(CountElementName = nameof(cbData))] byte[] pbData, uint cbData);
}
