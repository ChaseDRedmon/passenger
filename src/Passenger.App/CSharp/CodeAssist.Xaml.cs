using System.Text.Json;
using System.Text.RegularExpressions;
using Passenger.App.Workspace;
using Passenger.App.Xaml;
using Passenger.Editor;
using Passenger.Lsp;
using Passenger.Text;
using Passenger.Xaml;
using Passenger.Xaml.Features;
using Passenger.Xaml.Syntax;

namespace Passenger.App.CSharp;

/// <summary>The same popups and commands over a XAML file, answered by Passenger's own XAML (<see cref="XamlService"/>)
/// rather than a language server: completion as it is typed, after a tag's <c>&lt;</c>, a name's <c>.</c> or
/// <c>:</c>, a value's quotes and a markup extension's brace.</summary>
public sealed partial class CodeAssist
{
    private readonly Dictionary<CompletionItem, XamlItem> xamlOffered = [];

    /// <summary>The characters beyond a name's that narrow the open XAML list rather than close it.</summary>
    private string xamlWordCharacters = string.Empty;

    /// <summary>The window's XAML, for a XAML file shown.</summary>
    public Func<XamlService?>? Xaml { get; set; }

    /// <summary>The shown file's XAML, null for a file that is not XAML of a project Passenger reads.</summary>
    private XamlDocument? XamlCurrent => editors.Active is { } active ? XamlOf(active) : null;

    private XamlDocument? XamlOf(OpenDocument? document) => document is null ? null : Xaml?.Invoke()?.For(document);

    /// <summary>Whether what was just typed in a XAML file opens completion: a tag's '&lt;' (or its end tag's '/'), a
    /// name's '.' or ':', a value's opening quote or a markup extension's brace or comma.</summary>
    private bool XamlTrigger(string typed)
    {
        if (typed.Length != 1)
        {
            return false;
        }
        TextSnapshot text = Editor.Document.Snapshot;
        int caret = Editor.Selections.Primary.Caret;
        char before = caret >= 2 ? text[caret - 2] : '\0';
        return typed[0] switch
        {
            '<' or '.' or ':' or '{' or '#' => true,
            '/' => before == '<',
            '"' or '\'' => before == '=',
            ' ' or ',' => InsideBraces(text, caret),
            _ => false,
        };
    }

    /// <summary>Whether the caret is between a value's markup extension braces, where a space or comma starts its next
    /// argument.</summary>
    private static bool InsideBraces(TextSnapshot text, int caret)
    {
        int open = 0;
        for (int at = caret - 1; at >= 0 && at > caret - 400; at--)
        {
            char c = text[at];
            if (c is '"' or '\'' or '\n' or '>' or '<')
            {
                return false;
            }
            if (c == '}')
            {
                open--;
            }
            else if (c == '{' && ++open > 0)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The XAML list for the caret, in place of the server's.</summary>
    private async Task StartXamlCompletion(XamlDocument xaml)
    {
        completing?.Cancel();
        CancellationTokenSource cancel = new();
        completing = cancel;
        int caret = Editor.Selections.Primary.Caret;
        TextDocument asked = Editor.Document;
        try
        {
            XamlCompletionList? list = await xaml.CompleteAsync(caret, cancel.Token);
            if (cancel.IsCancellationRequested || Editor.Document != asked)
            {
                return;
            }
            if (list is null || list.Items.Count == 0)
            {
                CloseCompletion();
                return;
            }
            xamlOffered.Clear();
            List<CompletionItem> items = [];
            foreach (XamlItem offered in list.Items)
            {
                CompletionItem item = new() { Label = offered.Label, Kind = KindOf(offered.Kind), Detail = offered.Detail };
                xamlOffered[item] = offered;
                items.Add(item);
            }
            xamlWordCharacters = list.WordCharacters;
            open = new Completion(items, Math.Min(list.Start, Editor.Selections.Primary.Caret), [], false);
            Filter();
        }
        catch (OperationCanceledException)
        {
            // Typed on: the list is asked for again.
        }
    }

    /// <summary>A XAML item put in place of what was typed from <paramref name="start"/>: a snippet's caret where its
    /// <c>$0</c> is, and completion opened again when the item says (a property's values once its quotes are in).</summary>
    private void CommitXaml(XamlItem item, int start)
    {
        int caret = Editor.Selections.Primary.Caret;
        TextRange replaced = TextRange.FromBounds(start, Math.Max(start, caret));
        if (item.IsSnippet)
        {
            Editor.InsertSnippet(replaced, Snippet.FromProtocol(item.Insert));
        }
        else
        {
            Editor.ApplyEdits([new TextEdit(replaced.Start, replaced.Length, item.Insert)]);
        }
        if (item.Reopen)
        {
            _ = StartCompletion(null);
        }
    }

    /// <summary>The name at the caret and its file, read as the file stands; null where there is none, said in the status
    /// bar for a command (<paramref name="none"/>).</summary>
    private async Task<(XamlFile File, XamlSymbol Symbol)?> XamlSymbolAt(XamlDocument xaml, int offset, string? none)
    {
        XamlFile? file = await xaml.FileAsync();
        XamlSymbol? symbol = file is null ? null : await BackgroundWork.Run(() => XamlNavigation.At(file, offset));
        if (file is null || symbol is null || symbol.Targets.Count == 0)
        {
            if (none is not null)
            {
                Message?.Invoke(this, none);
            }
            return null;
        }
        return (file, symbol);
    }

    /// <summary>Go to Declaration in XAML: straight to the one place a name leads, or the places to choose from.</summary>
    private async Task XamlGoTo(XamlDocument xaml, int offset)
    {
        if (await XamlSymbolAt(xaml, offset, "Cannot find a declaration to go to") is not { } found)
        {
            return;
        }
        IReadOnlyList<XamlTarget> targets = found.Symbol.Targets;
        if (targets.Count == 1)
        {
            await Arrive(targets[0], found.File);
            return;
        }
        List<Choice> choices = [.. targets.Select(t => new Choice(t.Name, t.Path is { } path ? Path.GetFileName(path)
            : t.Kind == XamlTargetKind.Theme ? "Theme" : t.Type?.Origin.AssemblyName ?? string.Empty, t))];
        ShowModal(new ChoicePopup($"Declarations of {found.Symbol.Name}", choices), offset, "Go to Declaration",
            choice => choice?.Value is XamlTarget target ? Arrive(target, found.File) : Task.CompletedTask);
    }

    /// <summary>Goes into the theme library's own XAML (Fluent's): a control's theme, a part of its template, or a resource
    /// the theme defines, its file had from the library's Source Link and opened read-only where it is written. The files
    /// are those compiled into the theme, and those its control list merges in (Fluent's Button.xaml, in
    /// FluentControls.xaml), whose own text says them.</summary>
    private async Task ArriveInTheme(XamlTarget target, XamlFile file)
    {
        if (Xaml?.Invoke() is not { } service)
        {
            return;
        }
        Passenger.Xaml.Metadata.AssemblyTypes? theme = file.Types.Assemblies.Where(a => a.Name.Contains(".Themes.", StringComparison.Ordinal) && a.XamlFiles.Count > 0)
            .OrderBy(a => a.Name.EndsWith(".Fluent", StringComparison.Ordinal) ? 0 : 1).FirstOrDefault();
        if (theme is null)
        {
            Message?.Invoke(this, "The project references no theme whose XAML can be read");
            return;
        }
        Said said = new();
        string assembly = LibrarySources.Implementation(theme.Path);
        List<string> known = [.. theme.XamlFiles];
        Task<string?> Fetch(string path)
        {
            Message?.Invoke(this, $"Reading {theme.Name}'s {path.TrimStart('/')} from its Source Link…");
            return service.LibraryFiles.FileAsync(assembly, theme.Name, path, said, CancellationToken.None);
        }
        IReadOnlyList<string> names = target.Type is { } type ? [.. file.Types.SelfAndBases(type).Select(t => t.Name)] : [];
        string? Named() => names.Select(n => known.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f) == n)).FirstOrDefault(f => f is not null);
        bool Prefixed() => target.Part is { } key && known.Any(f => key.StartsWith(Path.GetFileNameWithoutExtension(f), StringComparison.Ordinal));
        if (target.Type is not null ? Named() is null : !Prefixed())
        {
            foreach (string hub in theme.XamlFiles.Where(f => f.EndsWith("Controls.xaml", StringComparison.Ordinal)))
            {
                if (await Fetch(hub) is { } local)
                {
                    known.AddRange(Included(ReadText(local), hub, theme.Name).Where(f => !known.Contains(f)));
                }
            }
        }
        List<string> candidates = [];
        if (target.Type is not null)
        {
            candidates.AddRange(Named() is { } named ? [named] : []);
        }
        else if (target.Part is { } key)
        {
            candidates.AddRange(known.Where(f => key.StartsWith(Path.GetFileNameWithoutExtension(f), StringComparison.Ordinal))
                .OrderByDescending(f => Path.GetFileNameWithoutExtension(f).Length));
            candidates.AddRange(known.Where(f => f.Contains("/Accents/", StringComparison.Ordinal) && !candidates.Contains(f)));
        }
        Regex place = ThemePlace(target);
        foreach (string candidate in candidates.Take(12))
        {
            if (await Fetch(candidate) is not { } local)
            {
                continue;
            }
            string text = ReadText(local);
            Match where = place.Match(text);
            if (where.Success)
            {
                Navigate?.Invoke(this, (local, TextSnapshot.From(text).GetPosition(where.Groups["at"].Index)));
                Message?.Invoke(this, $"{theme.Name}'s {candidate.TrimStart('/')}, from its Source Link");
                return;
            }
        }
        Message?.Invoke(this, said.Last ?? $"{target.Name} is not in {theme.Name}'s XAML");
    }

    /// <summary>The files a theme's XAML merges in, as paths in its project: avares:// ones of its own assembly, and those
    /// beside the file.</summary>
    private static IEnumerable<string> Included(string text, string from, string assembly)
    {
        string folder = from[..(from.LastIndexOf('/') + 1)];
        foreach (Match include in IncludeSource().Matches(text))
        {
            string source = include.Groups["source"].Value;
            string own = $"avares://{assembly}/";
            if (source.StartsWith(own, StringComparison.Ordinal))
            {
                yield return "/" + source[own.Length..];
            }
            else if (!source.Contains("://", StringComparison.Ordinal))
            {
                yield return source.StartsWith('/') ? source : folder + source;
            }
        }
    }

    [GeneratedRegex(@"<(?:Merge)?ResourceInclude\b[^>]*\bSource=""(?<source>[^""]+)""")]
    private static partial Regex IncludeSource();

    /// <summary>The last reason a library file could not be had, kept as it is said.</summary>
    private sealed class Said : IProgress<string>
    {
        public string? Last { get; private set; }

        public void Report(string value) => Last = value;
    }

    /// <summary>Where a theme's XAML writes what a target names: a template part's name, a control's theme, a resource's key.</summary>
    private static Regex ThemePlace(XamlTarget target) => target switch
    {
        { Part: { } part, Type: not null } => new($@"\b(?:x:)?Name=""(?<at>{Regex.Escape(part)})"""),
        { Type: { } shown } => new($@"<(?<at>ControlTheme)\b[^>]*(?:\{{x:Type\s+{Regex.Escape(shown.Name)}\}}|TargetType=""{Regex.Escape(shown.Name)}"")"),
        { Part: { } key } => new($@"\bx:Key=""(?<at>{Regex.Escape(key)})"""),
        _ => new("(?!)"),
    };

    /// <summary>Goes where a XAML name leads: a place in the project's source, a range of a file, or a library's type
    /// decompiled.</summary>
    private async Task Arrive(XamlTarget target, XamlFile? file = null)
    {
        if (target.Kind == XamlTargetKind.Theme && file is not null)
        {
            await ArriveInTheme(target, file);
            return;
        }
        if (target.Place is { } place)
        {
            Navigate?.Invoke(this, (place.Path, new TextPosition(place.Line, place.Column)));
            return;
        }
        if (target.Path is { } path)
        {
            Navigate?.Invoke(this, (path, target.Range is { } range ? PositionIn(path, range.Start) : new TextPosition(0, 0)));
            return;
        }
        if (target.Type is { } type)
        {
            (string Path, TextPosition Position)? opened = await BackgroundWork.Run(() => LibrarySources.Decompile(type, target.Member));
            if (opened is { } decompiled)
            {
                Navigate?.Invoke(this, decompiled);
            }
            else
            {
                Message?.Invoke(this, $"{type.FullName} could not be decompiled from {type.Origin.AssemblyName}");
            }
        }
    }

    /// <summary>Where an offset of a file is: in its open text, else its text on disk.</summary>
    private TextPosition PositionIn(string path, int offset)
    {
        TextSnapshot text = editors.Documents.FirstOrDefault(d => d.Path == path)?.Text.Snapshot ?? TextSnapshot.From(ReadText(path));
        return text.GetPosition(Math.Min(offset, text.Length));
    }

    private static string ReadText(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    /// <summary>A range of a file as a place for the Find window.</summary>
    private LspLocation Location(string path, int start, int end)
    {
        TextSnapshot text = editors.Documents.FirstOrDefault(d => d.Path == path)?.Text.Snapshot ?? TextSnapshot.From(ReadText(path));
        TextPosition from = text.GetPosition(Math.Min(start, text.Length));
        TextPosition to = text.GetPosition(Math.Min(end, text.Length));
        return new LspLocation(DocumentUris.FromPath(path), new LspRange(new LspPosition(from.Line, from.Column), new LspPosition(to.Line, to.Column)));
    }

    /// <summary>Find Usages in XAML: an element's name where the file names it and where its code-behind uses the field
    /// Avalonia makes for it; a resource key where any of the project's XAML uses it.</summary>
    private async Task XamlUsages(XamlDocument xaml, int offset)
    {
        if (await XamlSymbolAt(xaml, offset, "No usages found") is not { } found)
        {
            return;
        }
        (XamlFile file, XamlSymbol symbol) = found;
        List<LspLocation> usages = [];
        if (symbol.Named is { } named)
        {
            usages.AddRange(XamlNavigation.NameUses(file, named.Value).Select(r => Location(file.Path, r.Start, r.End)));
            usages.AddRange(await CodeBehindUses(file, named.Value));
        }
        else if (symbol.Targets[0].Kind == XamlTargetKind.Resource)
        {
            string key = symbol.Name;
            foreach (string path in file.Project.Files.Keys.Order(StringComparer.Ordinal))
            {
                string text = editors.Documents.FirstOrDefault(d => d.Path == path)?.Text.Snapshot.ToString() ?? ReadText(path);
                if (!text.Contains(key, StringComparison.Ordinal))
                {
                    continue;
                }
                XmlTree tree = await BackgroundWork.Run(() => XmlTree.Parse(text));
                usages.AddRange(XamlNavigation.ResourceUses(tree, key).Select(r => Location(path, r.Start, r.End)));
            }
            usages.AddRange(symbol.Targets.Where(t => t.Path is not null && t.Range is not null).Select(t => Location(t.Path!, t.Range!.Value.Start, t.Range.Value.End)));
        }
        else
        {
            Message?.Invoke(this, "Find Usages in XAML finds an element's name or a resource's key");
            return;
        }
        if (usages.Count == 0)
        {
            Message?.Invoke(this, "No usages found");
            return;
        }
        UsagesFound?.Invoke(this, ($"Usages of {symbol.Name}", usages));
    }

    /// <summary>The code-behind's uses of the field Avalonia's generator makes for a named element: the server's references
    /// from a mention of the name in a file of the <c>x:Class</c> whose definition is that generated field, its generated
    /// declaration and uses left out.</summary>
    private async Task<IReadOnlyList<LspLocation>> CodeBehindUses(XamlFile file, string name)
    {
        if (service()?.Session is not { IsLoaded: true } session || file.ClassName is not { } className)
        {
            return [];
        }
        IEnumerable<string> files = file.Types.PlacesOf(className).Select(p => p.Path).Append(file.Path + ".cs").Where(File.Exists).Distinct(StringComparer.Ordinal);
        Regex word = new($@"(?<![\w.]){Regex.Escape(name)}\b", RegexOptions.CultureInvariant);
        foreach (string path in files)
        {
            OpenDocument? open = editors.Documents.FirstOrDefault(d => d.Path == path);
            string text = open?.Text.Snapshot.ToString() ?? ReadText(path);
            foreach (Match mention in word.Matches(text).Take(5))
            {
                LspDocument? shown = null;
                try
                {
                    LspDocument server = open is not null && service()?.For(open)?.Lsp is { } attached ? attached : shown = session.Open(path, new TextDocument(text));
                    IReadOnlyList<LspLocation> definitions = await server.DefinitionAsync(mention.Index);
                    if (!definitions.Any(d => IsGenerated(d, className)))
                    {
                        continue;
                    }
                    return [.. (await server.ReferencesAsync(mention.Index)).Where(r => !IsGenerated(r, className))];
                }
                catch (LanguageServerFailed)
                {
                    return [];
                }
                finally
                {
                    shown?.Dispose();
                }
            }
        }
        return [];
    }

    /// <summary>Whether a place is in the C# Avalonia's generator wrote for a class's XAML (its <c>Class.g.cs</c>).</summary>
    private static bool IsGenerated(LspLocation place, string className) =>
        !place.Uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase) && place.Uri.Contains(className + ".g.cs", StringComparison.Ordinal);

    /// <summary>Rename in XAML: an element's name, where the file names it and where its code-behind uses its field, as one
    /// command undone whole.</summary>
    private async Task XamlRename(XamlDocument xaml, OpenDocument document)
    {
        int caret = Editor.Selections.Primary.Caret;
        if (await XamlSymbolAt(xaml, caret, "Rename in XAML renames an element's name") is not { Symbol.Named: { } named } found)
        {
            return;
        }
        string old = named.Value;
        RenameBox box = new(old);
        ChangeTracker opened = new(document.Text);
        ShowModal(box, named.ValueStart, async name =>
        {
            opened.Dispose();
            if (name is null || name == old)
            {
                return;
            }
            if (!Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(name))
            {
                Message?.Invoke(this, $"'{name}' cannot name a field in the code-behind");
                return;
            }
            using Asked asked = WorkspaceEdits.Track(editors);
            XamlFile? file = await xaml.FileAsync();
            if (file is null)
            {
                return;
            }
            Dictionary<string, List<LspTextEdit>> changes = new(StringComparer.Ordinal);
            void Add(LspLocation place)
            {
                if (!changes.TryGetValue(place.Uri, out List<LspTextEdit>? edits))
                {
                    changes[place.Uri] = edits = [];
                }
                edits.Add(new LspTextEdit(place.Range, name));
            }
            foreach (TextRange use in XamlNavigation.NameUses(file, old))
            {
                Add(Location(file.Path, use.Start, use.End));
            }
            foreach (LspLocation use in await CodeBehindUses(file, old))
            {
                Add(use);
            }
            JsonElement edit = JsonSerializer.SerializeToElement(new { changes }, Json.Options);
            List<string> dropped = [];
            int files = TypeRename.Apply(editors, edit, [], $"Rename {old} to {name}", asked, dropped);
            Message?.Invoke(this, dropped.Count > 0 ? $"Renamed, but not in {string.Join(", ", dropped)}: it was edited meanwhile"
                : files > 1 ? $"Renamed in {files} files" : "Renamed");
        });
    }

    /// <summary>Show Context Actions in XAML: the fixes of the problems on the caret's line, the file checked as it stands.</summary>
    private async Task XamlFixes(XamlDocument xaml, IReadOnlyList<EditorFix> own)
    {
        int caret = Editor.Selections.Primary.Caret;
        List<XamlFix> fixes = [];
        // A file whose project could not be read still has the running app's fixes.
        if (await xaml.FileAsync() is { } file)
        {
            IReadOnlyList<XamlDiagnostic> found = await BackgroundWork.Run(() => XamlDiagnostics.Of(file));
            TextSnapshot text = TextSnapshot.From(file.Text);
            int line = text.GetLineFromOffset(Math.Min(caret, text.Length));
            fixes.AddRange(found.Where(d => text.GetLineFromOffset(Math.Min(d.Range.Start, text.Length)) == line).SelectMany(d => d.Fixes));
        }
        if (fixes.Count == 0 && own.Count == 0)
        {
            Message?.Invoke(this, "No fixes here");
            return;
        }
        // A failed binding's fixes (the running app's word) come first, then the file's own problems'.
        List<Choice> choices = [.. own.Select(f => new Choice(f.Title, f.Detail, f)), .. fixes.Select(f => new Choice(f.Title, Path.GetFileName(f.Path), f))];
        OpenDocument? document = editors.Active;
        ShowModal(new ChoicePopup("Fixes", choices), caret, "Show Context Actions", choice =>
        {
            if (choice?.Value is XamlFix fix)
            {
                ApplyFix(fix);
            }
            else if (choice?.Value is EditorFix chosen && editors.Active == document)
            {
                Editor.ApplyEdits([new TextEdit(chosen.Range.Start, chosen.Range.Length, chosen.Text)], SelectionSet.Single(chosen.Range.Start + chosen.Text.Length));
            }
            return Task.CompletedTask;
        });
    }

    /// <summary>A XAML fix made against its file's text as it is now (open, or on disk) and applied as one command.</summary>
    private void ApplyFix(XamlFix fix)
    {
        string text = ReadOpenOrDisk(fix.Path);
        IReadOnlyList<TextEdit> edits = fix.Edits(text);
        if (edits.Count == 0)
        {
            Message?.Invoke(this, $"{fix.Title}: nothing to change");
            return;
        }
        TextSnapshot snapshot = TextSnapshot.From(text);
        List<LspTextEdit> changes = [.. edits.Select(e => new LspTextEdit(new LspRange(Lsp(snapshot.GetPosition(e.Start)), Lsp(snapshot.GetPosition(e.End))), e.Text))];
        JsonElement edit = JsonSerializer.SerializeToElement(new { changes = new Dictionary<string, List<LspTextEdit>> { [DocumentUris.FromPath(fix.Path)] = changes } }, Json.Options);
        List<string> dropped = [];
        TypeRename.Apply(editors, edit, [], fix.Title, null, dropped);
        Message?.Invoke(this, dropped.Count > 0 ? $"{fix.Title} was not applied: {string.Join(", ", dropped)} was edited meanwhile" : fix.Title);

        static LspPosition Lsp(TextPosition position) => new(position.Line, position.Column);
    }

    /// <summary>The hover card's words for a XAML name.</summary>
    private async Task<string?> XamlDescription(XamlDocument xaml, int offset)
    {
        if (await XamlSymbolAt(xaml, offset, null) is not { } found)
        {
            return null;
        }
        return XamlNavigation.Describe(found.File, found.Symbol);
    }

    /// <summary>A C# definition that lands in the field Avalonia's generator made for a named element goes to the element's
    /// name in its XAML instead, as Rider's does; null for any other.</summary>
    private XamlTarget? NamedElementOf(IReadOnlyList<LspLocation> definitions, int offset)
    {
        foreach (LspLocation place in definitions)
        {
            if (place.Uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            Match generated = GeneratedFile().Match(Uri.UnescapeDataString(place.Uri));
            if (!generated.Success || editors.Active is not { } active)
            {
                continue;
            }
            string className = generated.Groups["class"].Value;
            string name = NameAt(offset);
            if (Xaml?.Invoke()?.ClassFile(className, active.Path) is { } xaml
                && Regex.Match(ReadOpenOrDisk(xaml), $@"\b(?:x:)?Name\s*=\s*""(?<name>{Regex.Escape(name)})""") is { Success: true } found)
            {
                int start = found.Groups["name"].Index;
                return new XamlTarget(XamlTargetKind.Element, name) { Path = xaml, Range = new TextRange(start, name.Length) };
            }
        }
        return null;
    }

    private string ReadOpenOrDisk(string path) => editors.Documents.FirstOrDefault(d => d.Path == path)?.Text.Snapshot.ToString() ?? ReadText(path);

    [GeneratedRegex(@"(?<class>[\w.]+)\.g\.cs")]
    private static partial Regex GeneratedFile();

    /// <summary>The protocol's kind for a XAML item, which gives its badge the letter and colour Rider's has.</summary>
    private static int KindOf(XamlItemKind kind) => kind switch
    {
        XamlItemKind.Element or XamlItemKind.Type or XamlItemKind.ClosingTag => 7,
        XamlItemKind.Property or XamlItemKind.AttachedProperty or XamlItemKind.Member => 10,
        XamlItemKind.Event => 23,
        XamlItemKind.Directive or XamlItemKind.Value => 14,
        XamlItemKind.Namespace => 9,
        XamlItemKind.EnumValue => 20,
        XamlItemKind.Colour => 16,
        XamlItemKind.Resource => 21,
        XamlItemKind.Extension => 7,
        XamlItemKind.Asset => 17,
        XamlItemKind.StyleClass or XamlItemKind.PseudoClass or XamlItemKind.ElementName => 18,
        _ => 1,
    };
}
