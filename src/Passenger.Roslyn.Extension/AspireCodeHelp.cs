using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Extensions;
using Microsoft.CodeAnalysis.Text;

namespace Passenger.Roslyn.Extension;

/// <summary>Reads a deliberately bounded, straight-line AppHost through Roslyn, never through resource-name heuristics.</summary>
public sealed class AspireCodeHelp : IExtensionDocumentMessageHandler<AspireHelpRequest, AspireHelp>
{
    public Task<AspireHelp> ExecuteAsync(AspireHelpRequest message, ExtensionMessageContext context, Document document, CancellationToken cancellationToken) =>
        AspireSource.HelpAsync(document, message.At, message.Explain, cancellationToken);
}

/// <summary>Recomputes a missing edge before applying it, so a previously offered fix cannot add a duplicate reference.</summary>
public sealed class AspireReferenceFix : IExtensionDocumentMessageHandler<AspireFixRequest, Refactored>
{
    public async Task<Refactored> ExecuteAsync(AspireFixRequest message, ExtensionMessageContext context, Document document, CancellationToken cancellationToken)
    {
        AspireHelp help = await AspireSource.HelpAsync(document, null, false, cancellationToken).ConfigureAwait(false);
        return help.Missing.FirstOrDefault(m => m.Service == message.Service && m.Resource == message.Resource
            && Compare(m.Start, message.At) <= 0 && Compare(message.At, m.End) <= 0)?.Fix
            ?? new Refactored("The Aspire reference was not added: the source changed since the fix was offered, and the reference is no longer missing there; request the fix again", []);
    }

    private static int Compare(Place left, Place right) => left.Line == right.Line ? left.Character.CompareTo(right.Character) : left.Line.CompareTo(right.Line);
}

/// <summary>Shares semantic AppHost resource identities with configurations and gutter actions.</summary>
public sealed class AspireRunResources : IExtensionWorkspaceMessageHandler<AspireResourcesRequest, AspireRunResourcesResult>
{
    public Task<AspireRunResourcesResult> ExecuteAsync(AspireResourcesRequest message, ExtensionMessageContext context, CancellationToken cancellationToken) =>
        AspireSource.RunResourcesAsync(context.Solution, message.AppHost, cancellationToken);
}

internal static class AspireSource
{
    private const string Builder = "Aspire.Hosting.ApplicationModel.IResourceBuilder<T>";
    private const string ConnectionResource = "Aspire.Hosting.ApplicationModel.IResourceWithConnectionString";

    private sealed record Resource(ILocalSymbol Symbol, string Name, string? ProjectPath, bool Connection, bool Discovery, VariableDeclaratorSyntax Declaration);
    private sealed record Graph(Document Document, SourceText Text, IReadOnlyList<Resource> Resources, HashSet<(string Service, string Target)> Edges);
    private sealed record Use(ExpressionSyntax Expression, string Name, bool Connection);

    public static async Task<AspireRunResourcesResult> RunResourcesAsync(Solution solution, string appHost, CancellationToken cancel)
    {
        Project? project = solution.Projects.FirstOrDefault(candidate => SamePath(candidate.FilePath, appHost));
        if (project is null)
        {
            return new([], "The AppHost is not loaded in the C# language server");
        }
        List<AspireRunResource> resources = [];
        foreach (Document document in project.Documents)
        {
            cancel.ThrowIfCancellationRequested();
            SyntaxNode? root = await document.GetSyntaxRootAsync(cancel).ConfigureAwait(false);
            SemanticModel? model = await document.GetSemanticModelAsync(cancel).ConfigureAwait(false);
            if (root is null || model is null || document.FilePath is null)
            {
                continue;
            }
            SourceText text = await document.GetTextAsync(cancel).ConfigureAwait(false);
            string hash = TextHash.Of(text.ToString());
            foreach (InvocationExpressionSyntax added in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(added, cancel).Symbol is not IMethodSymbol method || method.Name != "AddProject"
                    || method.ContainingType.ToDisplayString() != "Aspire.Hosting.ProjectResourceBuilderExtensions"
                    || method.ContainingAssembly.Name != "Aspire.Hosting")
                {
                    continue;
                }
                string? name = null;
                string? projectPath = null;
                foreach (ArgumentSyntax argument in added.ArgumentList.Arguments)
                {
                    string? parameter = AspireProjectDeclaration.Parameter(method, added, argument)?.Name;
                    if (model.GetConstantValue(argument.Expression, cancel) is not { HasValue: true, Value: string value })
                    {
                        continue;
                    }
                    if (parameter == "name")
                    {
                        name = value;
                    }
                    else if (parameter == "projectPath")
                    {
                        projectPath = ResolvePath(value, AppHostDirectory(model.Compilation) ?? Path.GetDirectoryName(project.FilePath));
                    }
                }
                if (method.TypeArguments is [INamedTypeSymbol projectType])
                {
                    projectPath = await AspireProjectDeclaration.MetadataPathAsync(projectType, model.Compilation, cancel).ConfigureAwait(false);
                }
                if (!string.IsNullOrWhiteSpace(name) && projectPath is not null)
                {
                    resources.Add(new(name, projectPath, document.FilePath, PlaceAt(text, added.SpanStart), hash));
                }
            }
        }
        if (resources.GroupBy(resource => resource.Name, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            return new([], "The AppHost declares multiple project resources with the same name");
        }
        return new(resources);
    }

    public static async Task<AspireHelp> HelpAsync(Document document, Place? at, bool explain, CancellationToken cancel)
    {
        List<string>? evidence = explain ? [] : null;
        AspireHelp help = await ReadHelpAsync(document, at, evidence, cancel).ConfigureAwait(false);
        evidence?.Add($"Result: {help.Missing.Count} missing references");
        return help with { Evidence = evidence is null ? null : string.Join("\n", evidence.Take(30)) };
    }

    private static async Task<AspireHelp> ReadHelpAsync(Document document, Place? at, List<string>? evidence, CancellationToken cancel)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancel).ConfigureAwait(false);
        SemanticModel? model = await document.GetSemanticModelAsync(cancel).ConfigureAwait(false);
        evidence?.Add($"Document: {document.FilePath}; project: {document.Project.FilePath}; syntax errors: {root?.ContainsDiagnostics}");
        if (root is null || model is null || document.Project.FilePath is null)
        {
            return new AspireHelp(null, null, [], []);
        }
        if (evidence is not null)
        {
            evidence.AddRange(model.GetDiagnostics(cancellationToken: cancel).Where(d => d.Severity == DiagnosticSeverity.Error).Take(5).Select(d => d.ToString()));
        }
        List<(Graph Graph, Resource? Service)> matches = [];
        foreach (Project project in document.Project.Solution.Projects)
        {
            cancel.ThrowIfCancellationRequested();
            if (!project.MetadataReferences.OfType<PortableExecutableReference>().Any(r => Path.GetFileName(r.FilePath) == "Aspire.Hosting.dll")
                && !project.ProjectReferences.Any(r => project.Solution.GetProject(r.ProjectId)?.AssemblyName == "Aspire.Hosting"))
            {
                continue;
            }
            Compilation? compilation = await project.GetCompilationAsync(cancel).ConfigureAwait(false);
            if (compilation?.GetTypeByMetadataName("Aspire.Hosting.IDistributedApplicationBuilder") is null)
            {
                continue;
            }
            List<Graph> graphs = [];
            bool unsupported = false;
            foreach (Document source in project.Documents)
            {
                SyntaxNode? syntax = await source.GetSyntaxRootAsync(cancel).ConfigureAwait(false);
                SemanticModel? semantic = await source.GetSemanticModelAsync(cancel).ConfigureAwait(false);
                if (syntax is null || semantic is null || !syntax.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Any(i => semantic.GetSymbolInfo(i, cancel).Symbol is IMethodSymbol method && IsCreateBuilder(method)))
                {
                    if (syntax?.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(i => i.Expression switch
                        {
                            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText == "CreateBuilder",
                            IdentifierNameSyntax name => name.Identifier.ValueText == "CreateBuilder",
                            _ => false,
                        }) == true)
                    {
                        unsupported = true;
                    }
                    continue;
                }
                Graph? graph = await ReadGraphAsync(source, syntax, semantic, cancel).ConfigureAwait(false);
                evidence?.Add($"AppHost: {source.FilePath}; graph proven: {graph is not null}");
                if (graph is null)
                {
                    unsupported = true;
                }
                else
                {
                    graphs.Add(graph);
                }
            }
            if (unsupported || graphs.Count > 1)
            {
                return new AspireHelp(null, null, [], []);
            }
            if (graphs.Count == 0)
            {
                continue;
            }
            Graph selected = graphs[0];
            Resource[] services = [.. selected.Resources.Where(r => SamePath(r.ProjectPath, document.Project.FilePath))];
            evidence?.Add($"Service matches in {selected.Document.FilePath}: {services.Length}");
            if (evidence is not null)
            {
                Compilation? hostCompilation = await selected.Document.Project.GetCompilationAsync(cancel).ConfigureAwait(false);
                INamedTypeSymbol? discovery = hostCompilation?.GetTypeByMetadataName("Aspire.Hosting.IResourceWithServiceDiscovery");
                evidence.Add($"Discovery capability: {discovery?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}; assembly: {discovery?.ContainingAssembly.Identity}");
                evidence.AddRange(selected.Resources.Take(10).Select(r => $"Resource: {r.Name}; project: {r.ProjectPath}; connection: {r.Connection}; discovery: {r.Discovery}"));
                foreach (Resource resource in selected.Resources.Where(r => r.ProjectPath is not null).Take(3))
                {
                    INamedTypeSymbol? type = ResourceType(resource.Symbol.Type);
                    evidence.Add($"Resource type for {resource.Name}: {type?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}; assembly: {type?.ContainingAssembly.Identity}; nullable: {type?.NullableAnnotation}; interfaces: {string.Join(", ", type?.AllInterfaces.Take(16).Select(i => i.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)) ?? [])}");
                }
                evidence.AddRange(selected.Edges.Take(10).Select(e => $"Reference: {e.Service} -> {e.Target}"));
            }
            if (services.Length > 1)
            {
                return new AspireHelp(null, null, [], []);
            }
            if (services.Length == 1)
            {
                matches.Add((selected, services[0]));
            }
            else if (document.Project.Id == project.Id)
            {
                matches.Add((selected, null));
            }
        }
        evidence?.Add($"Unambiguous AppHost/service matches: {matches.Count}");
        if (matches.Count != 1)
        {
            return new AspireHelp(null, null, [], []);
        }
        (Graph host, Resource? service) = matches[0];
        SourceText text = await document.GetTextAsync(cancel).ConfigureAwait(false);
        List<AspireMissingReference> missing = [];
        if (service is not null && !root.ContainsDiagnostics)
        {
            foreach (Use use in Uses(root, model, evidence, cancel))
            {
                evidence?.Add($"Semantic resource use: {use.Name}; connection: {use.Connection}");
                Resource? target = host.Resources.SingleOrDefault(r => string.Equals(r.Name, use.Name, StringComparison.OrdinalIgnoreCase)
                    && (use.Connection ? r.Connection : r.Discovery && !r.Connection));
                if (target is null || target == service || host.Edges.Contains((service.Name, target.Name)))
                {
                    continue;
                }
                Refactored? fix = ReferenceFix(host, service, target);
                evidence?.Add($"Missing edge: {service.Name} -> {target.Name}; fix available: {fix is not null}");
                if (fix is not null)
                {
                    missing.Add(new AspireMissingReference(PlaceAt(text, use.Expression.SpanStart), PlaceAt(text, use.Expression.Span.End), service.Name, target.Name, fix));
                }
            }
        }
        if (at is null)
        {
            return new AspireHelp(null, null, [], missing);
        }
        int offset = await Changes.OffsetAsync(document, at, cancel).ConfigureAwait(false);
        LiteralExpressionSyntax? literal = root.FindToken(Math.Clamp(offset, 0, Math.Max(0, root.FullSpan.End - 1))).Parent?.AncestorsAndSelf()
            .OfType<LiteralExpressionSyntax>().FirstOrDefault();
        if (literal is null || !literal.IsKind(SyntaxKind.StringLiteralExpression) || !literal.Token.Text.StartsWith('"')
            || literal.Token.Text.StartsWith("\"\"\"", StringComparison.Ordinal) || offset <= literal.SpanStart
            || literal.Parent is not ArgumentSyntax argument || argument.Parent?.Parent is not InvocationExpressionSyntax call
            || model.GetSymbolInfo(call, cancel).Symbol is not IMethodSymbol called)
        {
            return new AspireHelp(null, null, [], missing);
        }
        int contentEnd = literal.Token.Text.Length > 1 && literal.Token.Text.EndsWith('"') ? literal.Span.End - 1 : literal.Span.End;
        if (offset > contentEnd)
        {
            return new AspireHelp(null, null, [], missing);
        }
        bool connection = IsConnectionString(called) && AspireProjectDeclaration.Parameter(called, call, argument)?.Name == "name";
        bool resourceName = called.ContainingType.ToDisplayString() == "Aspire.Hosting.ApplicationModel.ResourceNotificationService"
            && called.ContainingAssembly.Name == "Aspire.Hosting" && called.Name == "WaitForResourceAsync"
            && AspireProjectDeclaration.Parameter(called, call, argument)?.Name == "resourceName";
        string[] names = connection && service is not null
            ? [.. host.Resources.Where(r => r.Connection && host.Edges.Contains((service.Name, r.Name))).Select(r => r.Name).Order(StringComparer.Ordinal)]
            : resourceName ? [.. host.Resources.Select(r => r.Name).Order(StringComparer.Ordinal)] : [];
        return new AspireHelp(PlaceAt(text, literal.SpanStart + 1), PlaceAt(text, contentEnd), names, missing);
    }

    private static async Task<Graph?> ReadGraphAsync(Document document, SyntaxNode root, SemanticModel model, CancellationToken cancel)
    {
        if (root is not CompilationUnitSyntax unit || document.FilePath is null || unit.ContainsDiagnostics || unit.ContainsDirectives
            || model.GetDiagnostics(cancellationToken: cancel).Any(d => d.Severity == DiagnosticSeverity.Error)
            || unit.Members.Any(m => m is not GlobalStatementSyntax)
            || unit.Members.OfType<GlobalStatementSyntax>().Any(g => g.Statement is not LocalDeclarationStatementSyntax and not ExpressionStatementSyntax)
            || root.DescendantNodes().Any(n => n is AnonymousFunctionExpressionSyntax or AssignmentExpressionSyntax
                or ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax))
        {
            return null;
        }
        InvocationExpressionSyntax[] calls = [.. root.DescendantNodes().OfType<InvocationExpressionSyntax>()];
        if (calls.Count(i => model.GetSymbolInfo(i, cancel).Symbol is IMethodSymbol method && IsCreateBuilder(method)) != 1)
        {
            return null;
        }
        foreach (InvocationExpressionSyntax call in calls)
        {
            if (model.GetSymbolInfo(call, cancel).Symbol is not IMethodSymbol method || !Official(method)
                || call.ArgumentList.Arguments.Any(a => model.GetTypeInfo(a.Expression, cancel).Type?.TypeKind == TypeKind.Delegate)
                || IsCreateBuilder(method) && call.ArgumentList.Arguments.Any(a => a.Expression is not IdentifierNameSyntax
                    || AspireProjectDeclaration.Parameter(method, call, a)?.Name != "args"
                    || model.GetTypeInfo(a.Expression, cancel).Type is not IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_String }))
            {
                return null;
            }
        }
        List<Resource> resources = [];
        foreach (VariableDeclaratorSyntax declaration in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            if (model.GetDeclaredSymbol(declaration, cancel) is not ILocalSymbol local || ResourceType(local.Type) is not { } resourceType)
            {
                continue;
            }
            if (declaration.Initializer?.Value is not InvocationExpressionSyntax initializer)
            {
                return null;
            }
            InvocationExpressionSyntax? add = Chain(initializer).FirstOrDefault(i => model.GetSymbolInfo(i, cancel).Symbol is IMethodSymbol m
                && m.Name.StartsWith("Add", StringComparison.Ordinal) && m.Parameters.Any(p => p.GetAttributes().Any(a =>
                    a.AttributeClass?.ToDisplayString() == "Aspire.Hosting.ApplicationModel.ResourceNameAttribute")));
            if (add is null || model.GetSymbolInfo(add, cancel).Symbol is not IMethodSymbol method || !Official(method))
            {
                return null;
            }
            ArgumentSyntax? nameArgument = add.ArgumentList.Arguments.FirstOrDefault(a => AspireProjectDeclaration.Parameter(method, add, a)
                ?.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "Aspire.Hosting.ApplicationModel.ResourceNameAttribute") == true);
            if (nameArgument is null || model.GetConstantValue(nameArgument.Expression, cancel) is not { HasValue: true, Value: string name })
            {
                return null;
            }
            string? projectPath = null;
            if (method.ContainingType.ToDisplayString() == "Aspire.Hosting.ProjectResourceBuilderExtensions" && method.Name == "AddProject")
            {
                if (method.TypeArguments is [INamedTypeSymbol metadata])
                {
                    projectPath = await AspireProjectDeclaration.MetadataPathAsync(metadata, model.Compilation, cancel).ConfigureAwait(false);
                }
                else
                {
                    ArgumentSyntax? pathArgument = add.ArgumentList.Arguments.FirstOrDefault(a => AspireProjectDeclaration.Parameter(method, add, a)?.Name == "projectPath");
                    if (pathArgument is not null && model.GetConstantValue(pathArgument.Expression, cancel) is { HasValue: true, Value: string path })
                    {
                        projectPath = ResolvePath(path, AppHostDirectory(model.Compilation));
                    }
                }
                if (projectPath is null)
                {
                    return null;
                }
            }
            resources.Add(new Resource(local, name, projectPath, Implements(resourceType, model.Compilation.GetTypeByMetadataName(ConnectionResource)),
                Implements(resourceType, model.Compilation.GetTypeByMetadataName("Aspire.Hosting.IResourceWithServiceDiscovery")), declaration));
        }
        if (resources.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() != 1))
        {
            return null;
        }
        HashSet<(string Service, string Target)> edges = [];
        HashSet<string> declaredNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (InvocationExpressionSyntax call in calls)
        {
            IMethodSymbol method = (IMethodSymbol)model.GetSymbolInfo(call, cancel).Symbol!;
            ArgumentSyntax? resourceName = call.ArgumentList.Arguments.FirstOrDefault(a => AspireProjectDeclaration.Parameter(method, call, a)
                ?.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "Aspire.Hosting.ApplicationModel.ResourceNameAttribute") == true);
            if (method.Name.StartsWith("Add", StringComparison.Ordinal) && resourceName is not null)
            {
                if (model.GetConstantValue(resourceName.Expression, cancel) is not { HasValue: true, Value: string name }
                    || !declaredNames.Add(name) || !resources.Any(r => r.Declaration.Span.Contains(call.Span)))
                {
                    return null;
                }
            }
            if (method.Name == "Build" && resources.Any(r => r.Declaration.SpanStart > call.SpanStart))
            {
                return null;
            }
            if (method.Name == "WithReference")
            {
                if (method.ContainingType.ToDisplayString() != "Aspire.Hosting.ResourceBuilderExtensions"
                    || call.ArgumentList.Arguments.Count != 1 || call.Expression is not MemberAccessExpressionSyntax member)
                {
                    return null;
                }
                Resource? source = Receiver(member.Expression, call, resources, model, cancel);
                Resource? target = resources.FirstOrDefault(r => SymbolEqualityComparer.Default.Equals(r.Symbol,
                    model.GetSymbolInfo(call.ArgumentList.Arguments[0].Expression, cancel).Symbol));
                if (source is null || target is null || !SymbolEqualityComparer.Default.Equals(ResourceType(model.GetTypeInfo(member.Expression, cancel).Type), ResourceType(source.Symbol.Type)))
                {
                    return null;
                }
                edges.Add((source.Name, target.Name));
            }
            else if (call.ArgumentList.Arguments.Any(a => ResourceType(model.GetTypeInfo(a.Expression, cancel).Type) is not null))
            {
                return null;
            }
        }
        return new Graph(document, await document.GetTextAsync(cancel).ConfigureAwait(false), resources, edges);
    }

    private static Resource? Receiver(ExpressionSyntax expression, InvocationExpressionSyntax call, IReadOnlyList<Resource> resources, SemanticModel model, CancellationToken cancel)
    {
        while (expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member })
        {
            expression = member.Expression;
        }
        if (expression is IdentifierNameSyntax)
        {
            Resource? direct = resources.FirstOrDefault(r => SymbolEqualityComparer.Default.Equals(r.Symbol, model.GetSymbolInfo(expression, cancel).Symbol));
            if (direct is not null)
            {
                return direct;
            }
        }
        return call.Ancestors().OfType<VariableDeclaratorSyntax>().FirstOrDefault() is { } declaration
            ? resources.FirstOrDefault(r => r.Declaration == declaration) : null;
    }

    private static Refactored? ReferenceFix(Graph graph, Resource service, Resource target)
    {
        GlobalStatementSyntax? serviceStatement = service.Declaration.Ancestors().OfType<GlobalStatementSyntax>().FirstOrDefault();
        GlobalStatementSyntax? targetStatement = target.Declaration.Ancestors().OfType<GlobalStatementSyntax>().FirstOrDefault();
        if (serviceStatement is null || targetStatement is null || serviceStatement.Parent != targetStatement.Parent)
        {
            return null;
        }
        GlobalStatementSyntax later = serviceStatement.Span.End >= targetStatement.Span.End ? serviceStatement : targetStatement;
        if (service.Symbol.DeclaringSyntaxReferences.Length != 1 || target.Symbol.DeclaringSyntaxReferences.Length != 1
            || service.Declaration.Parent is not VariableDeclarationSyntax { Variables.Count: 1 }
            || target.Declaration.Parent is not VariableDeclarationSyntax { Variables.Count: 1 })
        {
            return null;
        }
        string newline = graph.Text.ToString().Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        Place insertion = PlaceAt(graph.Text, later.FullSpan.End);
        string prefix = later.FullSpan.End > 0 && graph.Text[later.FullSpan.End - 1] is not ('\r' or '\n') ? newline : string.Empty;
        string change = prefix + service.Declaration.Identifier.Text + ".WithReference(" + target.Declaration.Identifier.Text + ");" + newline;
        return new Refactored(null, [new FileChange(graph.Document.FilePath!, false, [new Edit(insertion, insertion, change)])]);
    }

    private static IEnumerable<Use> Uses(SyntaxNode root, SemanticModel model, List<string>? evidence, CancellationToken cancel)
    {
        foreach (InvocationExpressionSyntax call in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (model.GetSymbolInfo(call, cancel).Symbol is not IMethodSymbol method || !IsConnectionString(method))
            {
                continue;
            }
            ArgumentSyntax? argument = call.ArgumentList.Arguments.FirstOrDefault(a => AspireProjectDeclaration.Parameter(method, call, a)?.Name == "name");
            if (argument is not null && model.GetConstantValue(argument.Expression, cancel) is { HasValue: true, Value: string name })
            {
                yield return new Use(argument.Expression, name, true);
            }
        }
        foreach (AssignmentExpressionSyntax assignment in root.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (evidence is not null)
            {
                ISymbol? assigned = model.GetSymbolInfo(assignment.Left, cancel).Symbol;
                evidence.Add($"Assignment symbol: {assigned?.ToDisplayString()}; assembly: {assigned?.ContainingAssembly?.Name}; value type: {model.GetTypeInfo(assignment.Right, cancel).Type?.ToDisplayString()}");
            }
            if (model.GetSymbolInfo(assignment.Left, cancel).Symbol is not IPropertySymbol property || property.Name != "BaseAddress"
                || property.ContainingType.ToDisplayString() != "System.Net.Http.HttpClient" || property.ContainingAssembly.Name != "System.Net.Http")
            {
                continue;
            }
            ArgumentListSyntax? arguments = assignment.Right switch
            {
                ObjectCreationExpressionSyntax creation when model.GetTypeInfo(creation, cancel).Type?.ToDisplayString() == "System.Uri" => creation.ArgumentList,
                ImplicitObjectCreationExpressionSyntax creation when model.GetTypeInfo(creation, cancel).Type?.ToDisplayString() == "System.Uri" => creation.ArgumentList,
                _ => null,
            };
            if (arguments?.Arguments.FirstOrDefault()?.Expression is not { } uriExpression
                || model.GetConstantValue(uriExpression, cancel) is not { HasValue: true, Value: string uri })
            {
                continue;
            }
            int separator = uri.IndexOf("://", StringComparison.Ordinal);
            if (separator <= 0 || uri[..separator].ToLowerInvariant() is not ("http" or "https" or "http+https" or "https+http"))
            {
                continue;
            }
            string authority = uri[(separator + 3)..].Split('/', '?', '#')[0];
            if (authority.Length > 0 && !authority.Contains(':') && !authority.Contains('@'))
            {
                yield return new Use(uriExpression, authority, false);
            }
        }
    }

    private static bool IsConnectionString(IMethodSymbol method) => method.Name == "GetConnectionString"
        && method.ContainingType.ToDisplayString() == "Microsoft.Extensions.Configuration.ConfigurationExtensions"
        && method.ContainingAssembly.Name == "Microsoft.Extensions.Configuration.Abstractions";

    private static bool IsCreateBuilder(IMethodSymbol method) => method.Name == "CreateBuilder"
        && method.ContainingType.ToDisplayString() == "Aspire.Hosting.DistributedApplication" && method.ContainingAssembly.Name == "Aspire.Hosting";

    private static bool Official(IMethodSymbol method) => method.ContainingNamespace.ToDisplayString().StartsWith("Aspire.Hosting", StringComparison.Ordinal)
        && (method.ContainingAssembly.Name == "Aspire.Hosting" || method.ContainingAssembly.Name.StartsWith("Aspire.Hosting.", StringComparison.Ordinal));

    private static INamedTypeSymbol? ResourceType(ITypeSymbol? type) => type is INamedTypeSymbol named
        ? new[] { named }.Concat(named.AllInterfaces).FirstOrDefault(t => t.OriginalDefinition.ToDisplayString() == Builder)?.TypeArguments.FirstOrDefault() as INamedTypeSymbol
        : null;

    private static bool Implements(INamedTypeSymbol type, INamedTypeSymbol? capability) => capability is not null
        && (SymbolEqualityComparer.Default.Equals(type, capability) || type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, capability)));

    private static IEnumerable<InvocationExpressionSyntax> Chain(InvocationExpressionSyntax invocation)
    {
        yield return invocation;
        if (invocation.Expression is MemberAccessExpressionSyntax { Expression: InvocationExpressionSyntax previous })
        {
            foreach (InvocationExpressionSyntax call in Chain(previous))
            {
                yield return call;
            }
        }
    }

    private static Place PlaceAt(SourceText text, int offset)
    {
        LinePosition position = text.Lines.GetLinePosition(offset);
        return new Place(position.Line, position.Character);
    }

    private static bool SamePath(string? left, string? right) => left is not null && right is not null
        && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string? ResolvePath(string path, string? directory)
    {
        try
        {
            return Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : directory is null ? null : Path.GetFullPath(path, directory);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static string? AppHostDirectory(Compilation compilation)
    {
        string[] directories = [.. compilation.Assembly.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute")
            .Where(a => a.ConstructorArguments is [{ Value: "AppHostProjectPath" }, { Value: string }])
            .Select(a => (string)a.ConstructorArguments[1].Value!)];
        return directories.Length == 1 ? directories[0] : null;
    }

    internal static async Task<string?> ProjectPathAsync(Document document, InvocationExpressionSyntax call, SemanticModel model, CancellationToken cancel)
    {
        SyntaxNode? root = await document.GetSyntaxRootAsync(cancel).ConfigureAwait(false);
        Graph? graph = root is null ? null : await ReadGraphAsync(document, root, model, cancel).ConfigureAwait(false);
        return graph?.Resources.SingleOrDefault(r => r.Declaration.Span.Contains(call.Span))?.ProjectPath;
    }
}
