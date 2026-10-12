using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;

namespace Passenger.Debugger.ExpressionEvaluator.Compiler;

internal sealed record DelegateMaterializerAssembly(string AssemblyName, Guid ModuleVersionId, string TypeName, string MethodName, byte[] Assembly);

// 🤖
internal static class DelegateMaterializerCompiler
{
    public static DelegateMaterializerAssembly Compile(IEnumerable<MetadataReference> references)
    {
        string id = Guid.NewGuid().ToString("N");
        string assemblyName = $"Passenger.DelegateMaterializer.{id}";
        string typeName = $"PassengerDelegateMaterializer_{id}";
        string source = $$"""
			public static class {{typeName}}
			{
				private sealed class DelegateAssemblyLoadContext : System.Runtime.Loader.AssemblyLoadContext
				{
					private readonly System.Runtime.Loader.AssemblyLoadContext? _context;

					public DelegateAssemblyLoadContext(System.Reflection.Assembly contextAssembly)
						: base("Passenger.Evaluation." + System.Guid.NewGuid().ToString("N"), isCollectible: true)
					{
						_context = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(contextAssembly);
					}

					protected override System.Reflection.Assembly? Load(System.Reflection.AssemblyName requested)
					{
						if (_context != null)
						{
							foreach (var candidate in _context.Assemblies)
							{
								if (System.Reflection.AssemblyName.ReferenceMatchesDefinition(candidate.GetName(), requested)) return candidate;
							}
						}
						foreach (var candidate in System.Runtime.Loader.AssemblyLoadContext.Default.Assemblies)
						{
							if (System.Reflection.AssemblyName.ReferenceMatchesDefinition(candidate.GetName(), requested)) return candidate;
						}
						return null;
					}
				}

				private sealed record DelegateAssemblySession(DelegateAssemblyLoadContext LoadContext, System.Reflection.Assembly Assembly);
				private static readonly object DelegateAssemblySessionLock = new();
				private static readonly System.Collections.Generic.Dictionary<int, DelegateAssemblySession> DelegateAssemblySessions = new();
				private static int NextSessionId;

				private static System.Reflection.Module FindModule(string moduleVersionId)
				{
					var expected = new System.Guid(moduleVersionId);
					foreach (var candidate in System.AppDomain.CurrentDomain.GetAssemblies())
					{
						foreach (var module in candidate.GetModules())
						{
							if (module.ModuleVersionId == expected) return module;
						}
					}
					throw new System.InvalidOperationException("The requested module is not loaded");
				}

				private static System.Reflection.Assembly ResolveAssembly(
					System.Reflection.AssemblyName requested,
					System.Reflection.Assembly contextAssembly)
				{
					var loadContext = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(contextAssembly);
					if (loadContext != null)
					{
						foreach (var candidate in loadContext.Assemblies)
						{
							if (System.Reflection.AssemblyName.ReferenceMatchesDefinition(candidate.GetName(), requested)) return candidate;
						}
					}
					foreach (var candidate in System.AppDomain.CurrentDomain.GetAssemblies())
					{
						if (System.Reflection.AssemblyName.ReferenceMatchesDefinition(candidate.GetName(), requested)) return candidate;
					}
					throw new System.InvalidOperationException("The requested type assembly is not loaded");
				}

				private static System.Type ResolveType(string typeName, string contextModuleVersionId)
				{
					var contextAssembly = FindModule(contextModuleVersionId).Assembly;
					return System.Type.GetType(
						typeName,
						requested => ResolveAssembly(requested, contextAssembly),
						(assembly, name, ignoreCase) => (assembly ?? contextAssembly).GetType(name, throwOnError: true, ignoreCase),
						throwOnError: true)!;
				}

				private static DelegateAssemblySession GetSession(int sessionId)
				{
					lock (DelegateAssemblySessionLock) return DelegateAssemblySessions[sessionId];
				}

				public static int Begin(byte[] assembly, string contextModuleVersionId)
				{
					var loadContext = new DelegateAssemblyLoadContext(FindModule(contextModuleVersionId).Assembly);
					System.Reflection.Assembly loaded;
					using (var stream = new System.IO.MemoryStream(assembly, writable: false)) loaded = loadContext.LoadFromStream(stream);
					lock (DelegateAssemblySessionLock)
					{
						var sessionId = ++NextSessionId;
						DelegateAssemblySessions.Add(sessionId, new DelegateAssemblySession(loadContext, loaded));
						return sessionId;
					}
				}

				public static void End(int sessionId)
				{
					DelegateAssemblySession? session;
					lock (DelegateAssemblySessionLock)
					{
						if (!DelegateAssemblySessions.Remove(sessionId, out session)) return;
					}
					session.LoadContext.Unload();
				}

				public static object CreateObject(int sessionId, int constructorToken)
				{
					var constructor = (System.Reflection.ConstructorInfo)GetSession(sessionId).Assembly.ManifestModule.ResolveMethod(constructorToken);
					return constructor.Invoke(null);
				}

				public static void SetField(int sessionId, int fieldToken, object target, object value)
				{
					var field = GetSession(sessionId).Assembly.ManifestModule.ResolveField(fieldToken);
					field.SetValue(target, value);
				}

				public static object Create(int sessionId, string methodModuleVersionId, int methodToken, string delegateTypeName, string contextModuleVersionId, object target)
				{
					var module = sessionId == 0 ? FindModule(methodModuleVersionId) : GetSession(sessionId).Assembly.ManifestModule;
					var method = (System.Reflection.MethodInfo)module.ResolveMethod(methodToken);
					var delegateType = ResolveType(delegateTypeName, contextModuleVersionId);
					return method.CreateDelegate(delegateType, target);
				}
			}
			""";
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            [SyntaxFactory.ParseSyntaxTree(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        using MemoryStream stream = new MemoryStream();
        EmitResult emit = compilation.Emit(stream);
        if (!emit.Success)
        {
            string errors = string.Join("; ", emit.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Select(diagnostic => diagnostic.GetMessage()));
            throw new InvalidOperationException($"Delegate materializer compilation failed: {errors}");
        }
        byte[] assembly = stream.ToArray();
        using PEReader peReader = new PEReader(new MemoryStream(assembly, writable: false));
        MetadataReader reader = peReader.GetMetadataReader();
        Guid moduleVersionId = reader.GetGuid(reader.GetModuleDefinition().Mvid);
        return new DelegateMaterializerAssembly(assemblyName, moduleVersionId, typeName, "Create", assembly);
    }
}
