using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NuGet.Frameworks;
using Passenger.App.Templates;
using Passenger.App.Workspace;
using Passenger.Core;
using Passenger.Editor;

namespace Passenger.App.Aspire;

public sealed record AspireOrchestrationChoice(IReadOnlyList<ProjectModel> Services, ProjectModel? AppHost, ProjectModel? ServiceDefaults);

/// <summary>Prepares the complete change away from the workspace. Nothing in the solution is written until every
/// template, reference and source edit has succeeded.</summary>
public static class AspireOrchestration
{
    public sealed record Write(string Path, FileText Text, byte[]? Before);

    public static async Task<IReadOnlyList<Write>> PrepareAsync(string solution, string staging,
        AspireOrchestrationChoice choice, CancellationToken cancel)
    {
        string root = Path.GetDirectoryName(solution)!;
        string name = Path.GetFileNameWithoutExtension(solution);
        string host = choice.AppHost?.Path ?? Path.Combine(root, name + ".AppHost", name + ".AppHost.csproj");
        string defaults = choice.ServiceDefaults?.Path ?? Path.Combine(root, name + ".ServiceDefaults", name + ".ServiceDefaults.csproj");
        Dictionary<string, (string Staged, byte[]? Before)> files = new(PathNames.Comparer);
        List<string> newFolders = [];
        foreach (string project in new[] { choice.AppHost is null ? host : null, choice.ServiceDefaults is null ? defaults : null }.OfType<string>())
        {
            string folder = Path.GetDirectoryName(project)!;
            if (Directory.Exists(folder) || File.Exists(folder))
            {
                throw new InvalidOperationException($"{folder} already exists. Choose an existing project or move the conflicting folder first.");
            }
            newFolders.Add(folder);
        }
        if (choice.Services.Count == 0)
        {
            throw new InvalidOperationException("Choose at least one service project.");
        }
        if (choice.Services.Any(p => p.TargetFrameworks.Count == 0))
        {
            throw new InvalidOperationException("Wait until the selected service projects have loaded their target frameworks.");
        }
        string Staged(string path)
        {
            if (!PathNames.Within(path, root))
            {
                throw new InvalidOperationException($"{path} is outside the solution folder. Add orchestration for this project manually.");
            }
            return Path.Combine(staging, Path.GetRelativePath(root, path));
        }
        void Copy(string path)
        {
            if (files.ContainsKey(path))
            {
                return;
            }
            byte[] before = File.ReadAllBytes(path);
            FileText text = FileText.FromBytes(before) ?? throw new InvalidOperationException($"{path} is not a text file.");
            string to = Staged(path);
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            FileText.Save(to, text.Text, text.Encoding, text.HasBom, text.CrBreaks);
            files.Add(path, (to, before));
        }
        Copy(solution);
        foreach (ProjectModel service in choice.Services)
        {
            Copy(service.Path);
            Copy(Path.Combine(service.Folder, "Program.cs"));
        }
        if (choice.AppHost is not null)
        {
            Copy(host);
            Copy(HostSourcePath(host));
            if (choice.Services.Any(service => !CanReference(choice.AppHost.TargetFrameworks, service.TargetFrameworks)))
            {
                throw new InvalidOperationException("The selected AppHost framework cannot reference every selected service. Choose a newer AppHost.");
            }
        }
        if (choice.ServiceDefaults is not null)
        {
            Copy(defaults);
            ValidateDefaults(choice.ServiceDefaults, choice.Services);
        }
        cancel.ThrowIfCancellationRequested();
        if (newFolders.Count > 0)
        {
            TemplateCatalog catalog = await ProjectTemplates.LoadAsync(root);
            cancel.ThrowIfCancellationRequested();
            if (choice.AppHost is null)
            {
                await Generate(host, "aspire-apphost", hostFramework: true);
            }
            if (choice.ServiceDefaults is null)
            {
                await Generate(defaults, "aspire-servicedefaults", hostFramework: false);
            }

            async Task Generate(string project, string shortName, bool hostFramework)
            {
                ProjectTemplate template = catalog.Templates.FirstOrDefault(t => t.Pack is null && t.ShortNames.Contains(shortName))
                    ?? throw new InvalidOperationException("Install Aspire.ProjectTemplates through Add > New Project > Aspire > Install, then try again.");
                List<(TemplateVariant Variant, string Framework, Version Version)> available = [.. template.Variants
                    .Where(v => v.Language is null or "C#")
                    .SelectMany(v => v.Frameworks.Select(f => (v, f, Framework(f).Version)))];
                IEnumerable<(TemplateVariant Variant, string Framework, Version Version)> compatible = available
                    .Where(a => choice.Services.All(service => hostFramework
                        ? CanReference([a.Framework], service.TargetFrameworks)
                        : CanReference(service.TargetFrameworks, [a.Framework])));
                (TemplateVariant Variant, string Framework, Version Version) picked =
                    (hostFramework ? compatible.OrderBy(a => a.Version) : compatible.OrderByDescending(a => a.Version)).FirstOrDefault();
                if (picked.Variant is null || picked.Variant.SkipRestoreOption is null)
                {
                    throw new InvalidOperationException($"The installed {shortName} template has no compatible framework with skip-restore support for the selected services.");
                }
                List<string> arguments = [template.ShortName, "--name", Path.GetFileNameWithoutExtension(project),
                    "--output", Path.GetDirectoryName(Staged(project))!, "--no-update-check", $"--{picked.Variant.SkipRestoreOption}", "true"];
                if (picked.Variant.Language is { } language)
                {
                    arguments.AddRange(["--language", language]);
                }
                if (picked.Variant.FrameworkOption is { } option)
                {
                    arguments.AddRange([$"--{option}", picked.Framework]);
                }
                Task<ChildResult> running = ChildProcesses.RunAsync(ProjectTemplates.NewCommand(arguments, root),
                    limit: TimeSpan.FromMinutes(10), cancel: cancel);
                BackgroundWork.Track(running);
                ChildResult result = await running;
                cancel.ThrowIfCancellationRequested();
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException($"Could not create {shortName}: {ProjectTemplates.Said(result)}");
                }
                string generated = Path.GetDirectoryName(Staged(project))!;
                foreach (string file in Directory.EnumerateFiles(generated, "*", SearchOption.AllDirectories))
                {
                    string destination = Path.Combine(Path.GetDirectoryName(project)!, Path.GetRelativePath(generated, file));
                    if (File.Exists(destination) || Directory.Exists(destination))
                    {
                        throw new InvalidOperationException($"{destination} already exists.");
                    }
                    _ = FileText.Load(file) ?? throw new InvalidOperationException($"The template produced a binary file: {file}");
                    files.Add(destination, (file, null));
                }
                if (!files.ContainsKey(project))
                {
                    throw new InvalidOperationException($"The template did not produce {Path.GetFileName(project)}.");
                }
            }
        }
        foreach (ProjectModel service in choice.Services)
        {
            if (!service.ProjectReferences.Any(p => PathNames.Same(p, defaults)))
            {
                Check(ProjectEdits.AddReference(Staged(service.Path), Staged(defaults)));
            }
            if (!(choice.AppHost?.ProjectReferences.Any(p => PathNames.Same(p, service.Path)) ?? false))
            {
                Check(ProjectEdits.AddReference(Staged(host), Staged(service.Path)));
            }
            string program = Path.Combine(service.Folder, "Program.cs");
            Save(Staged(program), ServiceSource(Read(Staged(program)).Text, program));
        }
        string hostProgram = HostSourcePath(Staged(host));
        Save(hostProgram, HostSource(Read(hostProgram).Text, host, choice.Services));
        foreach (string project in new[] { choice.AppHost is null ? host : null, choice.ServiceDefaults is null ? defaults : null }.OfType<string>())
        {
            Check(SolutionEdits.AddProject(Staged(solution), null, Staged(project)));
        }
        cancel.ThrowIfCancellationRequested();
        return [.. files.Select(f => new Write(f.Key, Read(f.Value.Staged), f.Value.Before))
            .Where(w => w.Before is null || FileText.FromBytes(w.Before)?.Text != w.Text.Text)];
    }

    public static void CheckUnchanged(IReadOnlyList<Write> writes)
    {
        foreach (Write write in writes)
        {
            if (write.Before is null ? File.Exists(write.Path) || Directory.Exists(write.Path)
                : !File.Exists(write.Path) || !File.ReadAllBytes(write.Path).AsSpan().SequenceEqual(write.Before))
            {
                throw new InvalidOperationException($"{write.Path} changed while orchestration was being prepared. Try again.");
            }
        }
    }

    private static void ValidateDefaults(ProjectModel defaults, IReadOnlyList<ProjectModel> services)
    {
        string path = defaults.Path;
        if (services.Any(service => !CanReference(service.TargetFrameworks, defaults.TargetFrameworks)))
        {
            throw new InvalidOperationException("The selected ServiceDefaults target framework is not compatible with every service.");
        }
        List<MethodDeclarationSyntax> methods = [.. Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.cs", SearchOption.AllDirectories)
            .Where(p => !Path.GetRelativePath(Path.GetDirectoryName(path)!, p).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .SelectMany(p => CSharpSyntaxTree.ParseText(Read(p).Text).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>())];
        if (!methods.Any(m => m.Identifier.ValueText == "AddServiceDefaults" && m.ParameterList.Parameters.FirstOrDefault()?.Modifiers.Any(SyntaxKind.ThisKeyword) == true))
        {
            throw new InvalidOperationException("The selected ServiceDefaults project does not expose AddServiceDefaults. Choose an Aspire ServiceDefaults project.");
        }
        bool web = services.Any(p => CSharpSyntaxTree.ParseText(Read(Path.Combine(p.Folder, "Program.cs")).Text).GetRoot()
            .DescendantNodes().OfType<InvocationExpressionSyntax>().Any(i => i.Expression is MemberAccessExpressionSyntax
            { Expression: IdentifierNameSyntax { Identifier.ValueText: "WebApplication" }, Name.Identifier.ValueText: "CreateBuilder" }));
        if (web && !methods.Any(m => m.Identifier.ValueText == "MapDefaultEndpoints" && m.ParameterList.Parameters.FirstOrDefault()?.Modifiers.Any(SyntaxKind.ThisKeyword) == true))
        {
            throw new InvalidOperationException("The selected ServiceDefaults project does not expose MapDefaultEndpoints for web services.");
        }
    }

    public static IReadOnlyList<string> AppHostSources(string project) =>
        [.. Directory.EnumerateFiles(Path.GetDirectoryName(project)!, "*.cs", SearchOption.TopDirectoryOnly)];

    private static string HostSourcePath(string project)
    {
        List<string> sources = [.. AppHostSources(project).Where(path => CSharpSyntaxTree.ParseText(Read(path).Text).GetRoot()
            .DescendantNodes().OfType<InvocationExpressionSyntax>().Any(i => i.Expression is MemberAccessExpressionSyntax
                { Expression: IdentifierNameSyntax { Identifier.ValueText: "DistributedApplication" }, Name.Identifier.ValueText: "CreateBuilder" }))];
        return sources.Count == 1 ? sources[0]
            : throw new InvalidOperationException($"{project}: expected one AppHost source file with DistributedApplication.CreateBuilder. No files were changed.");
    }

    private static bool CanReference(IReadOnlyList<string> consumers, IReadOnlyList<string> providers)
    {
        List<NuGetFramework> targets = [.. consumers.Select(Framework)];
        List<NuGetFramework> references = [.. providers.Select(Framework)];
        return targets.Count > 0 && references.Count > 0
            && targets.All(target => references.Any(reference => DefaultCompatibilityProvider.Instance.IsCompatible(target, reference)));
    }

    private static NuGetFramework Framework(string framework)
    {
        NuGetFramework parsed = NuGetFramework.ParseFolder(framework);
        if (parsed.IsUnsupported || parsed.Framework != FrameworkConstants.FrameworkIdentifiers.NetCoreApp || parsed.Version.Major < 8)
        {
            throw new InvalidOperationException($"Aspire orchestration requires .NET 8 or later: {framework}");
        }
        return parsed;
    }

    private static FileText Read(string path) => FileText.Load(path) ?? throw new InvalidOperationException($"{path} is not text.");

    private static void Save(string path, string text)
    {
        FileText file = Read(path);
        FileText.Save(path, text, file.Encoding, file.HasBom, file.CrBreaks);
    }

    private static void Check(string? problem)
    {
        if (problem is not null)
        {
            throw new InvalidOperationException(problem);
        }
    }

    private static string ServiceSource(string text, string path)
    {
        CompilationUnitSyntax root = Parse(text, path);
        (VariableDeclaratorSyntax builder, InvocationExpressionSyntax create) = Builder(root, ["WebApplication", "Host"], path);
        string variable = builder.Identifier.Text;
        InvocationExpressionSyntax build = Build(root, builder, path);
        List<(int At, string Text)> insertions = [];
        if (!Calls(root, variable, "AddServiceDefaults").Any())
        {
            StatementSyntax statement = builder.Ancestors().OfType<StatementSyntax>().First();
            insertions.Add((statement.Span.End, NewLine(text) + Indent(text, statement.SpanStart) + variable + ".AddServiceDefaults();"));
        }
        if (create.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "WebApplication" } })
        {
            VariableDeclaratorSyntax app = build.Ancestors().OfType<VariableDeclaratorSyntax>().FirstOrDefault()
                ?? throw new InvalidOperationException($"{path}: assign the built WebApplication to a local variable before adding orchestration.");
            if (!Calls(root, app.Identifier.Text, "MapDefaultEndpoints").Any())
            {
                StatementSyntax statement = app.Ancestors().OfType<StatementSyntax>().First();
                insertions.Add((statement.Span.End, NewLine(text) + Indent(text, statement.SpanStart) + app.Identifier.Text + ".MapDefaultEndpoints();"));
            }
        }
        return Insert(text, insertions);
    }

    private static string HostSource(string text, string host, IReadOnlyList<ProjectModel> services)
    {
        CompilationUnitSyntax root = Parse(text, host);
        (VariableDeclaratorSyntax builder, _) = Builder(root, ["DistributedApplication"], host);
        InvocationExpressionSyntax build = Build(root, builder, host);
        StatementSyntax statement = build.Ancestors().OfType<StatementSyntax>().First();
        List<string> additions = [];
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        List<(InvocationExpressionSyntax Call, string Name, string? ProjectPath, string? ProjectType)> projects =
            [.. Calls(root, builder.Identifier.Text, "AddProject").Select(BindProjectArguments)];
        foreach (ProjectModel service in services)
        {
            string name = Path.GetFileNameWithoutExtension(service.Path).ToLowerInvariant().Replace('.', '-').Replace('_', '-');
            if (name.Length == 0 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-') || !char.IsAsciiLetter(name[0]) || !names.Add(name))
            {
                throw new InvalidOperationException($"{service.Name} does not have a unique Aspire resource name. Rename the project or add its resource manually.");
            }
            string relative = Path.GetRelativePath(Path.GetDirectoryName(host)!, service.Path).Replace('\\', '/');
            string generatedType = Path.GetFileNameWithoutExtension(service.Path).Replace('.', '_').Replace('-', '_');
            bool SameProject((InvocationExpressionSyntax Call, string Name, string? ProjectPath, string? ProjectType) project)
            {
                try
                {
                    return project.ProjectPath is { } path
                        ? PathNames.Same(PathNames.Canonical(Path.GetFullPath(path.Replace('\\', '/'), Path.GetDirectoryName(host)!)), service.Path)
                        : project.ProjectType == "Projects." + generatedType;
                }
                catch (ArgumentException error)
                {
                    throw new InvalidOperationException($"Cannot resolve AppHost projectPath in {project.Call}: {error.Message}", error);
                }
            }
            bool exists = projects.Any(SameProject);
            bool occupied = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(call =>
                call.Expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax receiver } member
                && receiver.Identifier.Text == builder.Identifier.Text && member.Name.Identifier.ValueText.StartsWith("Add", StringComparison.Ordinal)
                && (call.ArgumentList.Arguments.FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == "name")
                    ?? call.ArgumentList.Arguments.FirstOrDefault(a => a.NameColon is null))?.Expression is LiteralExpressionSyntax literal
                && literal.Token.ValueText == name
                && !(member.Name.Identifier.ValueText == "AddProject" && projects.Any(p => p.Call == call && SameProject(p))));
            if (occupied)
            {
                throw new InvalidOperationException($"The AppHost already uses resource name {name} for another resource. Add {service.Name} manually with a unique name.");
            }
            if (!exists)
            {
                additions.Add($"{builder.Identifier.Text}.AddProject({SymbolDisplay.FormatLiteral(name, true)}, {SymbolDisplay.FormatLiteral(relative, true)});");
            }
        }
        return additions.Count == 0 ? text : text.Insert(statement.SpanStart,
            string.Join(NewLine(text) + Indent(text, statement.SpanStart), additions) + NewLine(text) + Indent(text, statement.SpanStart));
    }

    private static (InvocationExpressionSyntax Call, string Name, string? ProjectPath, string? ProjectType) BindProjectArguments(InvocationExpressionSyntax call)
    {
        GenericNameSyntax? generic = ((MemberAccessExpressionSyntax)call.Expression).Name as GenericNameSyntax;
        int required = generic is null ? 2 : 1;
        Dictionary<int, ExpressionSyntax> arguments = [];
        bool reordered = false;
        for (int position = 0; position < call.ArgumentList.Arguments.Count; position++)
        {
            ArgumentSyntax argument = call.ArgumentList.Arguments[position];
            int parameter = argument.NameColon?.Name.Identifier.ValueText switch
            {
                null => position,
                "name" => 0,
                "projectPath" when generic is null => 1,
                "launchProfileName" or "configure" => required,
                _ => -1,
            };
            if (parameter < 0 || parameter > required || argument.RefKindKeyword.RawKind != 0
                || argument.NameColon is null && reordered || !arguments.TryAdd(parameter, argument.Expression))
            {
                throw Unresolved();
            }
            reordered |= parameter != position;
        }
        if (generic is not null && generic.TypeArgumentList.Arguments.Count != 1
            || !arguments.TryGetValue(0, out ExpressionSyntax? name) || name is not LiteralExpressionSyntax { Token.Value: string resourceName }
            || string.IsNullOrWhiteSpace(resourceName)
            || generic is null && (!arguments.TryGetValue(1, out ExpressionSyntax? path)
                || path is not LiteralExpressionSyntax { Token.Value: string projectPath } || string.IsNullOrWhiteSpace(projectPath)))
        {
            throw Unresolved();
        }
        return (call, ((LiteralExpressionSyntax)arguments[0]).Token.ValueText,
            generic is null ? ((LiteralExpressionSyntax)arguments[1]).Token.ValueText : null,
            generic?.TypeArgumentList.Arguments[0].ToString());

        InvalidOperationException Unresolved() => new($"Cannot resolve AppHost AddProject arguments: {call}. Use literal name/projectPath arguments and a supported Aspire overload before adding orchestration.");
    }

    private static CompilationUnitSyntax Parse(string text, string path)
    {
        CompilationUnitSyntax root = CSharpSyntaxTree.ParseText(text).GetCompilationUnitRoot();
        if (root.ContainsDiagnostics || root.ContainsDirectives)
        {
            throw new InvalidOperationException($"{path}: source contains syntax errors or directives. Add orchestration manually for this layout.");
        }
        return root;
    }

    private static (VariableDeclaratorSyntax Builder, InvocationExpressionSyntax Create) Builder(CompilationUnitSyntax root, string[] types, string path)
    {
        List<(VariableDeclaratorSyntax Builder, InvocationExpressionSyntax Create)> builders = [.. root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Where(v => v.Initializer?.Value is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax
                { Expression: IdentifierNameSyntax type, Name.Identifier.ValueText: "CreateBuilder" or "CreateApplicationBuilder" } } && types.Contains(type.Identifier.ValueText))
            .Select(v => (v, (InvocationExpressionSyntax)v.Initializer!.Value))];
        if (builders.Count != 1 || builders[0].Builder.Ancestors().OfType<StatementSyntax>().FirstOrDefault()
            is not LocalDeclarationStatementSyntax { Declaration.Variables.Count: 1 })
        {
            throw new InvalidOperationException($"{path}: expected one local builder created with {string.Join(" or ", types)}.CreateBuilder. No files were changed.");
        }
        return builders[0];
    }

    private static InvocationExpressionSyntax Build(CompilationUnitSyntax root, VariableDeclaratorSyntax builder, string path)
    {
        List<InvocationExpressionSyntax> builds = [.. Calls(root, builder.Identifier.Text, "Build")];
        StatementSyntax declaration = builder.Ancestors().OfType<StatementSyntax>().First();
        if (builds.Count != 1 || builds[0].SpanStart <= builder.Span.End
            || Scope(builds[0].Ancestors().OfType<StatementSyntax>().First()) != Scope(declaration))
        {
            throw new InvalidOperationException($"{path}: expected one builder.Build() in the builder's scope. No files were changed.");
        }
        return builds[0];
    }

    private static SyntaxNode? Scope(StatementSyntax statement) => statement.Parent is GlobalStatementSyntax global ? global.Parent : statement.Parent;

    private static IEnumerable<InvocationExpressionSyntax> Calls(CompilationUnitSyntax root, string receiver, string method) =>
        root.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(c => c.Expression is MemberAccessExpressionSyntax
        { Expression: IdentifierNameSyntax identifier } member && identifier.Identifier.Text == receiver && member.Name.Identifier.ValueText == method);

    private static string NewLine(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static string Indent(string text, int at)
    {
        int start = text.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
        return new string(text[start..at].TakeWhile(c => c is ' ' or '\t').ToArray());
    }

    private static string Insert(string text, IEnumerable<(int At, string Text)> insertions)
    {
        foreach ((int at, string addition) in insertions.OrderByDescending(i => i.At))
        {
            text = text.Insert(at, addition);
        }
        return text;
    }
}
