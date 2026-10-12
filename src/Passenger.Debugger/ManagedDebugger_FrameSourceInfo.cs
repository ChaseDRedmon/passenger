using System.Reflection.PortableExecutable;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.Transforms;
using ICSharpCode.Decompiler.DebugInfo;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using Passenger.Debugger.Decompilation;
using Passenger.Debugger.Interop;

namespace Passenger.Debugger;

public readonly record struct SourceInfo(string FilePath, int StartLine, int EndLine, int StartColumn, int EndColumn, DecompiledSourceInfo? DecompiledSourceInfo);
public class DecompiledSourceInfo
{
    public required string TypeFullName { get; init; }
    public required AssemblyPathAndMvid Assembly { get; init; }
    public required string CallingUserCodeAssemblyPath { get; init; }
}
public record struct AssemblyPathAndMvid(string AssemblyPath, Guid Mvid);
public partial class ManagedDebugger
{
    /// This appears to be 1 based, ie requires no adjustment when returned to the user
    private SourceInfo? GetSourceInfoAtFrame(ICorDebugFrame frame, bool decompileIfNeeded)
    {
        if (frame is not ICorDebugILFrame ilFrame)
        {
            throw new InvalidOperationException("Active frame is not an IL frame");
        }

        ICorDebugFunction function = ilFrame.Function;
        ModuleInfo module = _modules[function.Module.BaseAddress];
        if (module.MetadataReader.HasSymbols is false && decompileIfNeeded)
        {
            // No PDB on disk — generate one via decompilation and update the module entry
            if (GetCachedOrGeneratePdb(module))
            {
                module.SymbolsFromDecompiled = true;
            }
        }

        if (module.MetadataReader.HasSymbols)
        {
            int ilOffset = ilFrame.IP.pnOffset;
            mdMethodDef methodToken = function.Token;
            (string sourceFilePath, int startLine, int endLine, int startColumn, int endColumn)? sourceInfo = module.MetadataReader.GetSourceLocationForOffset(methodToken, ilOffset);
            if (sourceInfo is not null)
            {
                DecompiledSourceInfo? decompiledSourceInfo = null;
                if (module.SymbolsFromDecompiled)
                {
                    string? callingUserCodeAssemblyPath = FindCallingUserCodeAssemblyPath(frame.Caller);
                    decompiledSourceInfo = CreateDecompiledSourceInfo(module, methodToken, callingUserCodeAssemblyPath);
                }

                return new SourceInfo(sourceInfo.Value.sourceFilePath, sourceInfo.Value.startLine, sourceInfo.Value.endLine, sourceInfo.Value.startColumn, sourceInfo.Value.endColumn, decompiledSourceInfo);
            }
        }

        return null;
    }

    /// Walks the physical caller chain looking for the closest frame from a user code assembly
    private string? FindCallingUserCodeAssemblyPath(ICorDebugFrame? startFrame)
    {
        for (ICorDebugFrame? frame = startFrame; frame is not null; frame = frame.Caller)
        {
            if (frame is not ICorDebugILFrame ilFrame)
            {
                continue;
            }

            ModuleInfo callerModule = _modules[ilFrame.Function.Module.BaseAddress];
            if (callerModule.IsUserCode)
            {
                return callerModule.ModulePath;
            }
        }
        return null;
    }

    private static DecompiledSourceInfo? CreateDecompiledSourceInfo(ModuleInfo module, int methodToken, string? callingUserCodeAssemblyPath)
    {
        if (callingUserCodeAssemblyPath is null)
        {
            return null;
        }

        IMetaDataImport metadataImport = module.Module.GetMetaDataInterface<IMetaDataImport>();
        Guid mvid = metadataImport.ScopeProps.pmvid;
        mdTypeDef containingTypeDef = metadataImport.GetMethodProps(methodToken).pClass;
        return new DecompiledSourceInfo
        {
            TypeFullName = GetFullMetadataTypeName(metadataImport, containingTypeDef),
            Assembly = new AssemblyPathAndMvid(module.ModulePath, mvid),
            CallingUserCodeAssemblyPath = callingUserCodeAssemblyPath
        };
    }

    private static string GetFullMetadataTypeName(IMetaDataImport metadataImport, mdTypeDef typeDef)
    {
        (string szTypeDef, CorTypeAttr pdwTypeDefFlags, mdToken ptkExtends) typeProps = metadataImport.GetTypeDefProps(typeDef);
        if (typeProps.pdwTypeDefFlags.IsTdNested() is false)
        {
            return typeProps.szTypeDef;
        }

        mdTypeDef declaringType = metadataImport.GetNestedClassProps(typeDef);
        return $"{GetFullMetadataTypeName(metadataImport, declaringType)}+{typeProps.szTypeDef}";
    }

    private bool GetCachedOrGeneratePdb(ModuleInfo moduleInfo)
    {
        string sharpIdeSymbolCachePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp", "SharpIdeSymbolCache");
        IMetaDataImport metadataImport = moduleInfo.Module.GetMetaDataInterface<IMetaDataImport>();
        Guid mvid = metadataImport.ScopeProps.pmvid;
        string assemblyName = Path.GetFileNameWithoutExtension(moduleInfo.ModuleName);
        string pdbPath = Path.Combine(sharpIdeSymbolCachePath, assemblyName, mvid.ToString(), $"{assemblyName}.decompiled.pdb");
        if (File.Exists(pdbPath))
        {
            if (!moduleInfo.MetadataReader.TryLoadSymbols(pdbPath))
            {
                _logger?.Invoke($"GetCachedOrGeneratePdb: could not load cached PDB '{pdbPath}'");
                return false;
            }
            return true;
        }
        return GeneratePdb(moduleInfo, pdbPath);
    }


    private bool GeneratePdb(ModuleInfo moduleInfo, string pdbPathToWriteTo)
    {
        string assemblyPath = moduleInfo.ModulePath;
        if (!File.Exists(assemblyPath))
        {
            return false;
        }

        List<string> allModulePaths = _modules.Values.Select(m => m.ModulePath).Where(p => !string.IsNullOrEmpty(p)).ToList();
        DebuggingAssemblyResolver resolver = new DebuggingAssemblyResolver(allModulePaths);

        PEFile file;
        try
        {
            file = new PEFile(assemblyPath, PEStreamOptions.PrefetchEntireImage);
        }
        catch (Exception ex)
        {
            _logger?.Invoke($"GeneratePdb: failed to open PE file '{assemblyPath}': {ex.Message}");
            return false;
        }

        using (file)
        {
            DecompilerSettings decompilerSettings = new DecompilerSettings();
            DecompilerTypeSystem decompilerTypeSystem = new DecompilerTypeSystem(file, resolver, decompilerSettings);

            _logger?.Invoke($"GeneratePdb: writing PDB to '{pdbPathToWriteTo}' for '{assemblyPath}'");
            try
            {
                string pdbDirectory = Path.GetDirectoryName(pdbPathToWriteTo)!;
                if (!Directory.Exists(pdbDirectory))
                {
                    Directory.CreateDirectory(pdbDirectory);
                }

                using FileStream pdbStream = File.Create(pdbPathToWriteTo);
                PortablePdbWriter2 portablePdbWriter2 = new PortablePdbWriter2 { NoLogo = true };
                portablePdbWriter2.WritePdb(file, decompilerTypeSystem, decompilerSettings, pdbStream);
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"GeneratePdb: exception writing PDB: {ex}");
                File.Delete(pdbPathToWriteTo);
                return false;
            }

            if (!moduleInfo.MetadataReader.TryLoadSymbols(pdbPathToWriteTo))
            {
                _logger?.Invoke($"GeneratePdb: could not load generated PDB '{pdbPathToWriteTo}'");
                return false;
            }

            _logger?.Invoke($"GeneratePdb: successfully loaded generated PDB for '{Path.GetFileName(assemblyPath)}'");
            return true;
        }
    }
}
