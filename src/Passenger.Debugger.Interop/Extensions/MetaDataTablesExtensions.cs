using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Passenger.Debugger.Interop.Extensions;

public static class MetaDataTablesExtensions
{
    extension(IMetaDataTables instance)
    {
        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataTables.TryGetBlob(System.UInt32,System.UInt32@,System.IntPtr@)" />
        public (int pcbData, nint ppData) GetBlob(int ixBlob)
        {
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetBlob((uint)ixBlob, out var pcbData, out var ppData));
                return (pcbData: (int)pcbData, ppData: ppData);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataTables.TryGetBlobHeapSize(System.UInt32@)" />
        public int BlobHeapSize
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetBlobHeapSize(out var pcbBlobs));
                return checked((int)pcbBlobs);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataTables.TryGetCodedTokenInfo(System.UInt32,System.UInt32@,System.UInt32*@,System.SByte*@)" />
        public unsafe (int pcTokens, nint ppTokens, nint ppName) GetCodedTokenInfo(int ixCdTkn)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetCodedTokenInfo(checked((uint)ixCdTkn), out var pcTokens, out var ppTokens, out var ppName));
            return (pcTokens: checked((int)pcTokens), ppTokens: (nint)ppTokens, ppName: (nint)ppName);
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataTables.TryGetColumn(System.UInt32,System.UInt32,System.UInt32,System.UInt32@)" />
        public int GetColumn(int ixTbl, int ixCol, int rid)
        {
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetColumn((uint)ixTbl, (uint)ixCol, (uint)rid, out var pVal));
                return (int)pVal;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataTables.TryGetColumnInfo(System.UInt32,System.UInt32,System.UInt32@,System.UInt32@,System.UInt32@,System.SByte*@)" />
        public unsafe (int poCol, int pcbCol, int pType, nint ppName) GetColumnInfo(int ixTbl, int ixCol)
        {
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetColumnInfo((uint)ixTbl, (uint)ixCol, out var poCol, out var pcbCol, out var pType, out var ppName));
                return (poCol: (int)poCol, pcbCol: (int)pcbCol, pType: (int)pType, ppName: unchecked((nint)ppName));
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataTables.TryGetGuid(System.UInt32,System.Guid*@)" />
        public unsafe Guid* GetGuid(int ixGuid)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetGuid(checked((uint)ixGuid), out var ppGUID));
            return ppGUID;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataTables.TryGetGuidHeapSize(System.UInt32@)" />
        public int GuidHeapSize
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetGuidHeapSize(out var pcbGuids));
                return checked((int)pcbGuids);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataTables.TryGetNextBlob(System.UInt32,System.UInt32@)" />
        public int GetNextBlob(int ixBlob)
        {
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetNextBlob((uint)ixBlob, out var pNext));
                return (int)pNext;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataTables.TryGetNextGuid(System.UInt32,System.UInt32@)" />
        public int GetNextGuid(int ixGuid)
        {
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetNextGuid((uint)ixGuid, out var pNext));
                return (int)pNext;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataTables.TryGetNextString(System.UInt32,System.UInt32@)" />
        public int GetNextString(int ixString)
        {
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetNextString((uint)ixString, out var pNext));
                return (int)pNext;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataTables.TryGetNextUserString(System.UInt32,System.UInt32@)" />
        public int GetNextUserString(int ixUserString)
        {
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetNextUserString((uint)ixUserString, out var pNext));
                return (int)pNext;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataTables.TryGetNumTables(System.UInt32@)" />
        public int NumTables
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetNumTables(out var pcTables));
                return checked((int)pcTables);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataTables.TryGetRow(System.UInt32,System.UInt32,System.IntPtr@)" />
        public nint GetRow(int ixTbl, int rid)
        {
            Marshal.ThrowExceptionForHR(checked(instance.TryGetRow((uint)ixTbl, (uint)rid, out var ppRow)));
            return ppRow;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataTables.TryGetString(System.UInt32,System.SByte*@)" />
        public unsafe sbyte* GetString(int ixString)
        {
            Marshal.ThrowExceptionForHR(instance.TryGetString(checked((uint)ixString), out var ppString));
            return ppString;
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataTables.TryGetStringHeapSize(System.UInt32@)" />
        public int StringHeapSize
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetStringHeapSize(out var pcbStrings));
                return checked((int)pcbStrings);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataTables.TryGetTableIndex(System.UInt32,System.UInt32@)" />
        public int GetTableIndex(int token)
        {
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetTableIndex((uint)token, out var pixTbl));
                return (int)pixTbl;
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataTables.TryGetTableInfo(System.UInt32,System.UInt32@,System.UInt32@,System.UInt32@,System.UInt32@,System.SByte*@)" />
        public unsafe (int pcbRow, int pcRows, int pcCols, int piKey, nint ppName) GetTableInfo(int ixTbl)
        {
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetTableInfo((uint)ixTbl, out var pcbRow, out var pcRows, out var pcCols, out var piKey, out var ppName));
                return (pcbRow: (int)pcbRow, pcRows: (int)pcRows, pcCols: (int)pcCols, piKey: (int)piKey, ppName: unchecked((nint)ppName));
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataTables.TryGetUserString(System.UInt32,System.UInt32@,System.IntPtr@)" />
        public (int pcbData, nint ppData) GetUserString(int ixUserString)
        {
            checked
            {
                Marshal.ThrowExceptionForHR(instance.TryGetUserString((uint)ixUserString, out var pcbData, out var ppData));
                return (pcbData: (int)pcbData, ppData: ppData);
            }
        }

        /// <inheritdoc cref="M:Passenger.Debugger.Interop.IMetaDataTables.TryGetUserStringHeapSize(System.UInt32@)" />
        public int UserStringHeapSize
        {
            get
            {
                Marshal.ThrowExceptionForHR(instance.TryGetUserStringHeapSize(out var pcbBlobs));
                return checked((int)pcbBlobs);
            }
        }
    }
}
