using System;

namespace Passenger.Debugger.Interop.Extensions;

public static class CorDebugModuleManualExtensions
{
    public static T GetMetaDataInterface<T>(this ICorDebugModule module) where T : class
    {
        int num = module.TryGetMetaDataInterface<T>(out T? metadata);
        if (num < 0 || metadata == null)
        {
            throw new InvalidOperationException($"Failed to get metadata interface of type {typeof(T).FullName} from module {module.Name}. HRESULT: 0x{num:X8}");
        }
        return metadata;
    }

    public static int TryGetMetaDataInterface<T>(this ICorDebugModule module, out T? metadata) where T : class
    {
        Guid riid = typeof(T).GUID;
        int num = module.TryGetMetaDataInterface(ref riid, out object? ppObj);
        metadata = ((num >= 0) ? (T?)ppObj : null);
        return num;
    }
}
