# CLAUDE.md

Guidance for Claude Code working in this repository.

## What this is

**Passenger** is an IDE for C# and modern .NET (CoreCLR only: no .NET Framework, Mono or Unity), written in C# on
.NET 11 with an Avalonia 12 UI, for Windows and macOS first. The MVP shipped as v0.1.0; the work still to do is the
issues of the Post-MVP 1 milestone (https://github.com/Zaneris/passenger/milestone/2), each saying what Passenger does
now, what it should do and when it is done (**Now:**, **The aim:**, **Done when:**). Read the issue before starting
anything larger than a fix, and close or tick it when it lands. A new feature is the best Passenger can make it from what
it already has, never a copy of another IDE's; only its keys follow Rider's keymaps (Rules), whose files are in the jars
of JetBrains Rider at `~/Applications/Rider.app`. What is built keeps the behaviour this file describes.

## Commands

```sh
just build                 # Debug build of the solution (Passenger.slnx); warnings are errors
just run [PATH] [args]     # run the IDE on a folder or file (--file F, --caret L:C, --theme dark|light)
just screenshot X.png ...  # the main window drawn off-screen to a PNG, after the steps given
just publish RID           # self-contained Release build, ReadyToRun (osx-arm64, win-x64, win-arm64, linux-x64, linux-arm64)
just package-mac [RID] [--dry-run]  # artifacts/package/Passenger-macos-arm64.dmg (Apple Silicon only), checked mounted; signed
                           # with PASSENGER_SIGN_IDENTITY and notarized with PASSENGER_NOTARY_PROFILE when set, else ad hoc;
                           # its window's background is tools/dmg-background.svg, drawn by sips at 1x and 2x into one TIFF
just icons                 # rewrite Theme/Icons.axaml from the list in tools/icons.py (icons from a fixed commit)
just file-icons            # rewrite Theme/FileIconSet.axaml, the file icons, from the list in tools/file-icons.py (SVGs
                           # from fixed commits, kept in artifacts/file-icons, solid badges outlined; Explorer/FileIconSet.cs
                           # picks each file's)
just themes                # rewrite the generated themes (Theme/Sorbet.axaml and the rest) from tools/themes.py's
                           # palettes, every token worked out and nudged until UiRulesTests' contrast holds
just app-icon              # Passenger.icns, Passenger.ico and passenger.png from Assets/passenger.svg, by sips; fails
                           # unless every size's corners are transparent (never write the three files any other way)
just package-linux [RID]   # artifacts/package/Passenger-linux-x64.tar.gz (or -arm64): one passenger/ folder (app/,
                           # the launcher, install.sh, the desktop entry, icon and file types from tools/linux), checked
just package-win [RID]     # artifacts/package/Passenger-windows-x64.exe (or -arm64), Velopack's per-user installer;
                           # signed on Windows by PASSENGER_SIGNTOOL_PARAMS or PASSENGER_ARTIFACT_SIGNING_METADATA
just container-tools [RID] [--archive]  # artifacts/container-tools/<rid>/, the container tools self-contained (linux-x64,
                           # linux-arm64, linux-musl-x64, linux-musl-arm64; none: the engine's processor, glibc), where a
                           # dev build finds them; --archive also the release's Passenger-container-tools-<rid>.tar.gz
just notices               # fail naming any package the solution, Roslyn's server or a dotnet tool carries that the
                           # notices miss
just test [filter]         # every test, or those whose name matches
just test-times RUN [RUN...]  # rewrite TestTimes.json (each test class's seconds on CI, which deals the shards) from
                           # CI runs' results
just mcp [FOLDER]          # Passenger as an MCP server over a folder (this one by default), from a clone of the last build
just site-shots [SCENE...] # site/images/<scene>-<dark|light>.webp retaken (after just build; macOS, cwebp) from the
                           # sample in tools/site-shots/sample, copied under TMPDIR with a fixed history, its NuGet
                           # under artifacts/site-shots; SITE_SHOTS_FLAGS (--window-buttons by default) on every capture
```

**Use Passenger on itself.** `.mcp.json` starts Passenger's MCP server over this repository (`tools/mcp.sh`), so its
tools are the quickest way to ask Roslyn about Passenger's code (`diagnostics`, `find_symbol`, `outline`, `usages`),
to debug the IDE's own projects, and to look at the window (`screenshot`). It runs from an APFS clone of the last
Debug build, so `just build` never changes the files under it; reconnect the server to pick a new build up. A tool's
lines and columns count from 1 and its paths are the folder's. Anything it gets wrong is a Passenger bug to fix.

The screenshot is the self-check for anything visible (with nothing named it shows the Welcome screen, its list
empty, since a screenshot keeps no state: unless PASSENGER_SETTINGS names a folder, its state and logs go to a
temporary one removed as it ends, never the person's own log, and its terminal's shell keeps its history and startup
files apart from the person's). It opens what the command line names, takes
its steps in order through the window's real input, waits for background work to land, and captures
the compositor's own frame:

```sh
artifacts/bin/Passenger.App/debug/Passenger . --file src/Passenger.App/App.axaml.cs --caret 12:5 \
    --key enter --type "var x = 1;" --key "shift+meta+left" --click 400,300 \
    --screenshot out.png --size 1400x880 --scale 2 --crop 320,76,900,500 --theme light
```

A step after one that opens a dialog goes to that dialog, as a person's keys and clicks would. `--key` takes
"cmd+shift+L" or Rider's "meta shift L"; `--drag X1,Y1,X2,Y2` drags (`--drag-hold` keeps the button down,
to capture a tool window over its drop place); `--right-click X,Y` opens the menu of what is there; `--crop X,Y,W,H`,
`--click X,Y` and `--hover X,Y` (the pointer left to rest, for a hover card) are in logical units; `--click-name NAME`
clicks the visible control of that x:Name at its centre (a name none has ends it with exit code 2); `--wait MS` gives a
running program time. `--window-buttons` draws macOS's traffic lights into a main-window capture (`Theme/TrafficLights`:
Passenger's own drawing, measured from AppKit's of an active window made as Avalonia makes Passenger's, Dark Aqua under
the Dark theme and Aqua under the Light); Windows' and Linux's merged bars draw their own, so there it changes nothing,
and it draws none in full screen or when a dialog is captured. `--agent COMMAND` is a step that runs an agent in the
Terminal window's Agent tab and waits for it to end with exit code 0 (else exit code 2, no picture): the capture's window
opens its agent socket in its state folder whatever Enable MCP Server says and lets that agent use every tool unasked
(`MainWindow.AgentsUnattended`, set only by a screenshot), and the agent joins with `$PASSENGER --mcp-attach
$PASSENGER_PID`. macOS opens no socket path over 103 bytes, so its `PASSENGER_SETTINGS` stays short (the site's agent
scene, `tools/site-shots/agent.py`, uses `$TMPDIR/pss-agent-<theme>`).

Headless, the dispatcher's timers (the editor's hover, a caret's blink) fire inside a running dispatcher frame: a wait
that needs one pumps a frame (`Screenshot.Pump`, `Ui.WaitFor` in tests). `Ui.Settle` fires every one that has come due
(it promotes them, as a running frame does), so the caret's 530 ms blink redraws the editor during any settle that long: keep what
a key holds in `BackgroundWork` short (the XAML service waits for typing to pause only in a C# file under a loaded XAML
project).

Builds land under `artifacts/` (`UseArtifactsOutput`), never beside the sources. Package versions live only in
`Directory.Packages.props` (Roslyn's server's too, as `RoslynServerVersion`); shared properties only in
`Directory.Build.props`. Packages come from nuget.org alone, as nuget.org signed them (`nuget.config`), at what each
project's `packages.lock.json` pins: a restore that adds or changes a package rewrites the lock file, committed with
the change, and CI restores in locked mode. Every lock file holds all eight systems of `RuntimeIdentifiers` (osx-x64 restores, though no Intel Mac build ships; linux-musl-x64 and linux-musl-arm64 for the container tools alone), even a project carried
only on Windows (`Passenger.Inspector.Wpf`, `Passenger.WpfPreview`): a publish's RuntimeIdentifier reaches the restore
of every project it references (`GlobalPropertiesToRemove` keeps it out of the build only). Every `just` recipe and CI set `AVALONIA_TELEMETRY_OPTOUT=1`, which
Avalonia's build task reads only from the environment. `Directory.Build.targets` gives every
`ProjectReference` its full path, because the .NET 11 SDK opens a solution reached through a symbolic link (anywhere
under macOS's `/tmp`) by the link while MSBuild resolves references from the real folder, which built each referenced
project twice at once.

**Look at UI changes.** After changing anything visible, run `just screenshot` and view the PNG at `--scale 2`
before calling it done. The capture runs on Avalonia's headless platform with real Skia drawing, so it never opens a
window or takes the focus; it shows everything but the system's own window chrome.

## Layout

- `src/Passenger.Core`: what every part shares and no UI needs. `ChildProcesses` starts every program Passenger runs (an
  input of its own, UTF-8, none of a git hook's variables, a log line when one fails or takes a while) and ends it with
  the IDE, a crash or a kill included (`Lifeline`: the runtime's job object on Windows; on macOS and Linux a process
  group per child, which a watchdog shell kills once Passenger's pipe to it closes). A child the IDE ends (`Kill`, or
  `Ending` before asking it to go: a language server's shutdown, the host's input closed) and an exit code the caller
  names as an answer (`RunAsync(answers:)`, git's 128 for something not there) are no failures: their end is logged
  only with Log every program. `RunAsync`'s limit covers a child's run to its exit; once it has exited, whatever the
  limit, its output is read for a 2 s grace (`OutputGrace`), then a job it left holding the output open (a git hook's
  `daemon &`) is ended with its group and the output let go. `DotnetCommand` builds without
  MSBuild's server (which Stop could not reach) and in nodes salted to this IDE, which end with it. `Dotnet`,
  `Programs` and `ShellEnvironment` find the tools as the login shell would (the shell writes its environment to a file,
  its output to /dev/null, so a job its profile leaves running holds nothing up, and `Ready` waits out a slow profile
  once, 10 s from the read's start); `PathNames` makes paths canonical
  (`realpath`, or `GetFinalPathNameByHandle` on Windows), answers `IsUnder` for every "is this inside that folder", and
  `FoldersUnder` is every walk of a project's folders (the XAML core's, and the project host's stamps, which compiles
  PathNames.cs in): each folder once by its canonical path whatever links reach it, none a link goes up to, as MSBuild's
  globs walk them.
- `src/Passenger.Text`: no UI. The rope (`Rope.cs`, an immutable B-tree, so every version is a
  thread-safe snapshot), `TextDocument` (undo, change events in LSP order; a change's `Edits` is a `SortedEdits`, so
  `TextEditing.MapOffset`/`MapRange`/`MapThrough` carry an offset through a key's edits by halving: pass `e.Edits` as
  it came, not a copy), carets
  (`SelectionSet`) and every editing command as a pure function to an `EditPlan`. A line command (Move Line, Delete
  Line, Duplicate, a line's Copy and Cut) takes a line with the lines folds hide behind it (`folded`, the editor's
  hidden ranges), and Move Line keeps the lines moved in place and takes their neighbour round them, which
  `EditPlan.Moved` names so the editor carries the folds in it along. `MarkupIndent` is Enter's indent in
  XAML, MSBuild files, XML and HTML, as Rider's: in after a start tag, the end tag split onto its own line, under the
  first attribute inside a tag, HTML's void elements known and its optional end tags closed as HTML's parser closes
  them; XML reads back from the caret 4 KB at a time (widening ×4) and forward from the start only where tags don't
  pair up, so Enter costs what is around the caret, while HTML still reads from the start.
- `src/Passenger.Editor`: the `CodeEditor` control (drawing, input, commands, in partial files),
  the keymap (`Keymap.cs`, chords included; defaults in `EditorActions.cs` follow Rider's Visual
  Studio keymaps) and the highlighting contract (`IHighlighter`), with JetBrains Mono embedded.
  An inlay hint (`CodeEditor.Hints.cs`) is a parameter's name, which goes with the text after it, or a type, which
  goes with the text before it, or a value (`InlayHintKind.Value`: what a compose file's `${VAR}` comes to), which goes
  with the text before it as a type does and shows whatever Settings say of the other two; the hints at an offset are a
  caret place of their own, as IntelliJ's inlays are.
  `CodeEditor.Usages.cs` is what the caret lights up: the host's usage highlights (an edit takes them away unless
  kept, as Highlight Usages in File's are) and the bracket pair from a highlighter that is an `IBraceMatcher`
  (`CSharpHighlighter`, from the bracket's own node, never the server); a grammar's bracket or tag pair
  (`TextMateHighlighter.Pairs`) is read where it stands within 2,000 lines of the caret, as every caret move asks, Move
  Caret to Matching Brace's (`PairAt`'s and `TagPairAt`'s `far`) within 50,000. Soft wrap (`CodeEditor.Wrapping.cs`)
  makes every line one or more rows: `Rows` maps lines to rows and back, so whatever turns an offset into a place, a
  click into an offset or a line into a height goes through a row's `RowSpan`, never a `LineSpan` alone; an edit wraps
  again only the lines it touched, in the document's `ChangedFirst`, ahead of every `Changed` handler, so a host that
  asks for rows as it carries its folds through the edit never has the whole file wrapped again. The wraps are kept in
  runs of lines the next text's map shares (`WrapMap`), a line's row is worked out from them and the folded ranges and a
  row's line found by halving (`RowMap`), and the open file carries its regions in a `FoldList`, whose runs an edit moves
  only where it reaches them, the rest shifted whole: a key in a long wrapped file with folds costs what it changed.
- `src/Passenger.Languages`: colour before any language server answers. C# is Roslyn's parser
  in-process, incremental and off the UI thread (`CSharpHighlighter`), classified from syntax
  alone (`CSharpClassifier`); the language server's semantic colours layer over it. `CSharpHighlighter` is also each
  open file's one syntax service (`OpenDocument.Syntax`: the newest tree, carried through the edits since): a feature
  that reads C# syntax (templates, postfix, test marks, selection ranges, inline values) uses it, never a fresh parse.
  `Markup/FoldScan` finds XML's and JSON's folds in one scan of the rope off the UI thread; every fold region has a
  `FoldKind`, which Settings' Code Folding (Rider's Fold by default) and Collapse to Definitions read, and a folder keeps
  each file's folds as it closed (`WorkspaceState.Folds`, through `FoldMemory`). Every other language is coloured by VS
  Code's TextMate grammar for it (`TextMate/Grammars`, read by TextMateSharp with Onigwrap's Oniguruma), its scopes
  mapped to Rider's colours in `TextMate/ScopeColours` (named in full: TextMateSharp never matches parent selectors);
  `TextMateHighlighter` re-reads only from a changed line until the grammar's state is the same, off the UI thread.
  `Highlighters.For(language, document)` is the one place an editor's colours are chosen. A new grammar is its file,
  its scope in `Grammar.Files`, its language in `Grammar.Languages` and `Workspace/Language.ForPath` (with its
  comments), and its entry after THIRD_PARTY_NOTICES.md's marker line. Razor's grammar and C#'s (from dotnet/roslyn's
  Razor tooling) colour Razor files before the server answers, and C#'s colours Markdown's C# blocks. `TypeHints` turns Roslyn's type hints
  into Rider's (after the name, left off where Rider calls the type evident).
  A key costs what it changed, not the file's size: folds (`FoldRegions`) and bracket depths come from the parse
  before through the nodes Roslyn reused, never from a walk of the whole tree, and the server's colours are asked for
  the screen's lines first and the whole file only once typing stops. A finder never tells its answer inside the
  document's `Changed` (`FoldScan` awaits its scan with `ForceYielding`): the open file moves its regions through the
  edit in a later handler of the same event, so regions told during it were moved twice.
- `src/Passenger.Xaml`: Passenger's own XAML core, no UI and no Avalonia, which WPF (#187) shares: `Syntax` (XmlTree and
  MarkupExtension, positions kept, tolerant of half-typed markup), `Metadata` (a TypeSystem over the project's
  assemblies through System.Reflection.Metadata and its C# through Roslyn's parser), `Projects` (ProjectInputs reads
  project.assets.json; XamlProjectBuilder keeps a project's types and XAML under its own lock; its walks of a project's
  folders are `PathNames.FoldersUnder`), `Dialects`
  (AvaloniaDialect, WpfDialect) and `Features` (XamlCompletion, XamlDiagnostics, XamlNavigation, ThemeVariants, Colours). Not
  Avalonia.Ide.CompletionEngine, which is archived, predates Avalonia 12 and went with AvaloniaVS into Accelerate.
  A project's dialect is the framework it references (`XamlProjectBuilder.DialectOf`): Avalonia's package, or WPF's
  shared framework (restore's Microsoft.WindowsDesktop.App[.WPF], else UseWPF); any other's .xaml (MAUI, WinUI) gets no
  XAML service. WPF's types come from the WindowsDesktop reference pack, read from where restore put it
  (EnableWindowsTargeting off Windows) before the SDK's packs. Design-time data-context hints (d:DesignInstance,
  DataContext, an items control's ItemsSource for its templates and columns) are read only where the dialect compiles
  no binding, so Avalonia's compiled bindings read x:DataType alone. MarkupCompile's errors (MC####) mark their lines as
  AVLN's do; a named WPF element's code-behind field is MarkupCompile's `obj/…/File.g.cs` (the server's design-time
  build writes it), found from C# by its `#line`. An `x:Class` no C# declares is a class of its root element's type,
  declared at its `x:Class`, where the dialect's compiler makes it (`XamlDialect.MakesClasses`: WPF's MarkupCompile);
  Avalonia's compiler refuses one (AVLN2000), so there it stays marked. XAML Preview on a WPF file draws it on Windows
  (`WpfPreviewHost`) and elsewhere says WPF previews only on Windows; `XamlPreview` is the pane's model for either
  previewer (`AvaloniaPreviewHost`, `WpfPreviewHost`), its `PreviewElement`s what a click in the frame chooses and the
  caret outlines. `src/Passenger.WpfPreview` is WPF's previewer, Passenger's own (MIT, net8.0-windows, built
  everywhere with EnableWindowsTargeting, carried under `wpfpreview/` only in a Windows build or publish):
  `WpfPreviewHost` copies the project's last build to the state's `previews/<pid>-<guid>` (as Visual Studio's shadow
  cache, so `bin/` is never locked) and runs it with `dotnet exec` on the project's runtimeconfig and deps.json (or its
  own rolling-forward one for a library or a project before .NET 8); JSON lines over stdio (`Protocol.cs`, compiled
  into both), each frame a PNG at 96 DPI with the elements' boxes from `VisualDiagnostics.GetXamlSourceInfo`, placed in
  an XML reader's lines (`ServerLines.OfXml`, a lone '\r' ending one, as `LooseXaml` blanks by them). The file
  loads as loose XAML (`LooseXaml`: the root of its tag's type, x:Class, events, EventSetters and x:Code blanked);
  App.xaml's resources become the application's; `pack://application:,,,/` is the project's through WPF's private
  resource-assembly fields. A WPF control with no code-behind has no constructor calling `InitializeComponent`, so it
  draws empty where it is used, in the app and its preview alike.
  `Features` also has `XamlFormatter`/`XamlFormatSettings` (Rider's XML formatter, which is XAML's, at Rider's
  defaults, read from .editorconfig by Rider's `xml_*` keys or their `resharper_` forms and `max_line_length`; XAML
  Styler's attribute order and empty-element options as `xaml_enable_attribute_reordering`,
  `xaml_attribute_ordering_rule_groups` and `xaml_remove_ending_tag_of_empty_element`, off by default; otherwise only
  whitespace changes, and a second pass changes nothing), `XamlRefactorings` (Extract Resource, Extract Style, Surround
  With's targets) and `CreateFromUsage` (a binding path's property or command, and a missing converter; the MVVM
  Toolkit known by its attributes' full names). `CSharp/CodeAssist.XamlEditing.cs` is the window's side (dialogs in
  `Xaml/XamlRefactoringDialogs.cs`, surround templates in `LiveTemplates.Xaml`), each change's files applied through
  `ApplyXamlChanges` as one command; Settings' Code Style: XAML page is `AppSettings.XamlFormat`.
  `src/Passenger.App/Xaml`: `XamlService` is the session's (`ProjectSession.Xaml`), one `XamlDocument` per open XAML
  file, projects read off the UI thread, told each build's AVLN problems, a C# change (typed or on disk) sent to every
  project whose XAML reads it (`XamlService.Reading`: its own and those referencing its project, each builder deciding
  by `ProjectInputs.Compiles`), a project read again, its builder and dialect made anew (one that takes Avalonia later
  gets its help), as its project file changes or a restore's `project.assets.json` lands (the folder watcher passes over
  obj/ but reports that file apart, never to Local History, the Explorer or Git), including one under
  `artifacts/obj/<Project>/` (UseArtifactsOutput), which no project's folder holds and `XamlService.RestoredFor` maps to
  its project by name; `LibrarySources` decompiles a library's type read-only, once per assembly version (`Entry`: a
  folder named by a SHA-256 of the assembly's path, time and length, alike in every session; Source Link's too), pruned
  at a process's first use as the debugger's symbol cache is (30 days unused, 256 MB, never one used in the last day);
  `SourceLink` fetches a theme's XAML through its PDB's Source Link from symbols.nuget.org (whose request needs the
  `SymbolChecksum` header; tests point `PASSENGER_SYMBOLS` at a dead loopback address), each path, which the theme's own
  includes give, made canonical and refused outside its version's folder (`LibrarySources.Inside`), each file written
  whole through a temporary file and a rename (`LibrarySources.WriteWhole`).
  `XamlPreview`/`XamlPreviewPane` run Avalonia.Designer.HostApp (MIT) from the project's own Avalonia package through
  ChildProcesses over Avalonia.Remote.Protocol, restarted by `BuildRunController.BuildFinished`; frames are bitmaps at
  96 DPI sized by the host's scale, and the host's viewport resize requests go unanswered (answering made it ask again
  in a loop). `dotnet new avalonia.app` adds Accelerate's DevTools (`AvaloniaUI.DiagnosticsSupport`,
  `.WithDeveloperTools()`), which Passenger never adds or relies on.
- `src/Passenger.Lsp`: the Language Server Protocol client (StreamJsonRpc over stdio) and the
  Roslyn language server it runs, downloaded per platform at build and copied beside the app under
  `roslyn/`, less the four Visual Studio assemblies it never loads (`RoslynServerLeftOut`; the build fails if the
  package stops carrying one). `RoslynServerVersion` is the latest stable build (the owner's rule), 5.11.0: every later one
  so far, all prereleases, starts Visual Studio Telemetry's session even with telemetry off, loading the left-out
  RemoteControl (dotnet/roslyn#86122; moving past it is #234); after lowering it, delete `artifacts/bin/*/debug/roslyn*`, since the copy skips older
  files and leaves a mixed server that won't start. `RoslynSession` opens the folder's solution; `LspDocument` keeps one document in step
  and makes requests in the editor's offsets. A server that stops, restarts or refuses a request is one exception,
  `LanguageServerFailed`, which callers catch; StreamJsonRpc's own types never leave the project. `RoslynExtension` is
  Passenger's own extension to the server (`src/Passenger.Roslyn.Extension`, compiled against the server package's own
  assemblies through `RoslynServer.targets`, carried under `roslyn-extension/` with none of them): the server loads it
  with `--extension`, `server/_vs_activateExtension` registers its handlers, and `LspDocument.ExtensionAsync` sends a
  message through `textDocument/_vs_dipatchExtensionMessage`. A handler reads the server's solution and answers with
  edits, never changing its workspace. Razor (.razor, .cshtml) is the server's own, cohosted in it: `LspDocument.IsRazor`
  opens such a file as `aspnetcorerazor`, its colours take the legend Razor registers as it starts (why the client
  capabilities take dynamic registration), its diagnostics are one pull, and `RazorHtml` answers what Razor asks the
  client of a file's HTML, since Passenger has no HTML server. A request nothing waits on, sent as a window closes,
  goes through `LanguageServer.Tell`, never awaited on the closing window's thread. Requests resume off the caller's
  thread inside Passenger.Lsp (`ConfigureAwait(false)`), and a large answer (completion's list) is read in
  `LspDocument` before its caller resumes, so the UI thread never decodes it. `CountedMessages` notes each of the
  server's `workspace/semanticTokens/refresh` as it is read off the connection, before the one-at-a-time dispatch handles
  it, with how many messages had then crossed to the server's input whole (`TracedStream.Crossed`: StreamJsonRpc copies
  what it writes there on the thread pool); `SemanticHighlighter` asks again only for a word read once its request had
  crossed (`SentRequest`). Roslyn reads a column past a line's
  end as the line's end, so no change is sent between a "\r\n"'s halves: `LspDocument.Unsplit` takes in the half
  beside it and puts it back. A server's lines are not the editor's: a lone '\r', U+0085, U+2028 and U+2029 end one
  there (`ServerLines`, as Roslyn's own parse counts them, and `SourcePlace` with it), so every position crossing either
  way goes through `ServerLines` of the file's text, a place in another file through `App/CSharp/ServerPlaces` (the
  editor's text, else the disk's), never `new TextPosition(line, character)`; MCP's places are the editor's lines too.
  Roslyn's compilers count a C#, Visual Basic or Razor file's lines the same way (`ServerPlaces.CountedByRoslyn`), so a
  build's problems and console links to its messages and to stack frames are read through it as well, and so are the
  debugger's lines, which are the PDB's: `Debugging/BuiltLines` sends breakpoints, Run to Cursor and Set Next Statement
  at the compiler's line in the built text and shows a stop's frame (the arrow, its row, MCP's place) at the editor's
  line now, and Hot Reload's rude edits are listed at the editor's line. XAML's compilers count lines as an XML reader
  does (`ServerLines.OfXml`: a lone '\r' ends a line, U+0085, U+2028 and U+2029 do not), and `ServerPlaces.Compiled`
  gives any file's lines as its compiler counts them, which BuiltLines and a profile's hot spots read the PDB through.
  The places a running app reports of its XAML (each element's XamlSourceInfo, in the Live Visual Tree's details and Go
  to, a failed binding's in Problems, the consoles and its fixes, the Frames page's rows, `app_details` and `profile`)
  are its XAML compiler's lines too, WPF's BAML as Avalonia's XamlIl, and `LiveTree/AppPlaces` reads them at the editor's.
  `LanguageSession`/`ServerStart` run a server other than Roslyn: its `LspDocument`s carry
  a language id and their diagnostics are pushed (`DiagnosticsPushed`) or pulled. `RazorHtml` opens each Razor file's
  HTML in the HTML server (`LanguageServer.HtmlServer`) as `<uri>__virtual.html` and forwards Razor's HTML questions
  there, mapping answers' URIs back; a Razor file's pull adds the HTML server's diagnostics on lines its HTML has as the
  file does. `WebServers.targets` carries the web language servers under `web/`: `WebServers/package.json` pins them
  and `package-lock.json` everything they bring, each with its sha512; `tools/Passenger.NodePackages` puts them in
  place at build with no Node.js (cached in `artifacts/node/cache`), leaving out the HTML server's own TypeScript's
  tsserver, tsc and translations and moving its `lib*.d.ts` to `node_modules/node_modules/typescript/lib`, where the
  HTML server reads them; SchemaStore schemas and the Compose specification's (`compose-spec.json`, pinned by commit)
  go to `web/schemas`. To move a pin: edit package.json, run
  `npm install --package-lock-only --ignore-scripts` there, then update THIRD_PARTY_NOTICES.md's "Inside web/" table.
- `src/Passenger.App/Web`: `WebLanguages` is `ProjectSession.Web`, one server per kind (TypeScript over the project's
  own node_modules/typescript, else Passenger's; HTML; CSS, SCSS and Less; JSON; Dockerfiles, by name before extension
  (`WebLanguages.IsDockerfile`: Dockerfile.dev, Containerfile, *.dockerfile), dockerfile-language-server-nodejs; YAML,
  yaml-language-server), each run on `NodeRuntime` (Settings'
  Node.js page, else `Programs.Node()`) through ChildProcesses and started with the first file of its kind; without
  Node.js, `OpenDocument.Notice` (the editor's banner) and the status bar say why and the file keeps its colours. Every
  per-kind switch in `WebLanguages` names each kind (JSON is their fallback). The YAML server reads its settings from
  `workspace/configuration` (`YamlSettings`): schema store and Kubernetes' catalogue off, Compose's `!reset` and
  `!override` known, compose files (`JsonSchemas.ComposeFiles`) on the carried Compose schema by its file URI, and
  `yaml/registerContentRequest` sends any HTTP schema through `JsonSchemas`, so it fetches nothing itself. The Dockerfile
  and YAML servers push diagnostics naming no version, so `LspDocument` tells each push the version it answers as it
  arrives, by its server's `ServerStart.Pushes` (`PushPace`: the Dockerfile server's one push for each text that changed,
  in order; the YAML server's for the latest text once it has rested its 200 ms, an earlier push answering none), and a
  file's checks are held from a change until the push for its text lands (5 s at most).
  `WebDocument` is an `IServerDocument`, as `CSharpDocument` is, and `CodeAssist` asks it through `Current`.
  `JsonSchemas` answers `vscode/content` from the carried schemas, else downloads while Settings' Remote JSON Schemas
  allows (`PASSENGER_SCHEMAS` points the tests at a dead address). `CodeAssist.Web.cs` gives JSON the Go to Declaration
  (package.json dependencies) and property Rename its server lacks. `ComposeDeclarations` is Passenger's own Go to
  Declaration in a compose file (from the YAML server's outline: a build to its Dockerfile and the project its ENTRYPOINT
  or dotnet publish names (the final stage's ENTRYPOINT, through the stage it is FROM, comments passed over), a service,
  volume, network, config or secret named to its declaration, `${VAR}` to `.env` (one only an env_file sets is said not
  set for Compose's interpolation; `$$VAR` is no variable), an env_file to its file, an image to its registry's page
  through `CodeAssist.Browse`), tried before the
  server's own (`CodeAssist.Compose.cs`) and shared with MCP's `definition`; a Dockerfile's FROM image that names no
  stage goes to its page too (`CodeAssist.FromImageAt`). The Structure window shows the web servers'
  outlines too (a flat `SymbolInformation` list, the Dockerfile server's, read as symbols by `LspDocument.OutlineAsync`).
  The Dockerfile server completes a FROM image's tags by asking Docker Hub itself, only while Settings' Complete image
  tags from Docker Hub (its Docker page) is on: it runs with Passenger's preload (`node --require` of `Web/docker-hub.cjs`,
  carried as `web/passenger/docker-hub.cjs`), which `PASSENGER_DOCKER_HUB` (`DockerHubTags`) tells to answer no tags or
  to ask another place (the window's `DockerHub` stand-in, else the IDE's own variable, which the tests' run points at a
  dead address), and a change of the setting starts the server again (`WebLanguages.UseDockerHub`).
  `WebLanguages.Eslint` (`EslintService`/`EslintDocument`) runs vscode-langservers-extracted's ESLint server per folder
  on the project's own eslint (`ProjectPackages`: found from the file up, or Settings' Manual package); its fixes are
  commands whose edits come back as `workspace/applyEdit` (`ServerStart.ApplyEdit`, applied through `WorkspaceEdits`),
  and its markers go beside the server's (`WebDocument.ShowLint`), as the container engine's do (`WebDocument.ShowEngine`,
  an engine's left off a line the server already marks as gravely). `WebLanguages.Prettier` runs the project's prettier
  through `Web/prettier.cjs` (carried as `web/passenger/prettier.cjs`), one warm Node.js per folder over JSON lines,
  Prettier 2 or 3: a request's answer is the line of its id (any other line ends the runner), and what a plugin or
  configuration prints goes to its standard error, the IDE's log.
  `CodeAssist.Linting.cs` is ESLint's context actions, Fix ESLint Problems, Reformat Code / Reformat with Prettier and
  `BeforeSave`, which `MainWindow.SaveExplicitly` runs on File Save / Save All only, then saving the files it was pressed
  on (Save's, the one shown then) whatever came of them. A banner's link is
  `OpenDocument.NoticeAction` (Run 'npm install', through ChildProcesses).
- `src/Passenger.Host`: MSBuild in a process of its own (net10.0 on the machine's dotnet, one per SDK in
  `ProjectHosts`, started through ChildProcesses, carried under `host/`), loading the installed SDK's MSBuild (the one
  the folder's global.json picks) through MSBuildLocator. It answers JSON lines: `evaluate` (the Solution view's
  projects as a build sees them, `Workspace/ProjectModels`, cached in the folder's `projectModels.json`), `build`
  (Roslyn's design-time builds, written as its build host writes ProjectFileInfo, `Workspace/ProjectBuilds`, cached
  Brotli-compressed in `projectBuilds.json`) and `cancel` (a request given up: no more projects started, the build's
  submissions cancelled, so a left folder never holds the host), `hold` (a build of the IDE's own: no design-time build
  that generates files a build writes too (WPF's markup compilation, Avalonia's resources: `DesignTimeBuilds.Generating`)
  starts until it is cancelled, answered once those running have ended, since Windows lets one writer have a file;
  design-time builds also run with `UseAppHost=false`, so a build's apphost is created and signed by the build alone) and `inspect` (one project evaluated for the code help
  in its MSBuild files, `Inspection.cs`: the open files' unsaved text fed to MSBuild in place of the disk's; properties
  with the places that set them, items, targets, imports, the SDK's frameworks and the evaluation's errors (a skipped
  import as MSB4019), places by a file table, answered from its cache while the inputs hold). Off Windows a project
  that references a framework the SDK marks Windows-only and leaves EnableWindowsTargeting unset is loaded again with it
  as a global property (`WindowsTargeting.cs`), in evaluate, build and inspect alike; Passenger's builds follow
  (`BuildOutput.WindowsTargeting`, `-p:EnableWindowsTargeting=true`), and Roslyn's server has it in its environment off
  Windows, since its `dotnet restore` takes no properties; it is never written into a project. Each result carries stamps of what it read
  (`Stamps.cs`), so a reopen redoes only what changed: the files it imported, its folders, and what would change it by
  coming (`Evaluation.LookedFor`: each Directory.Build.props, .targets and Directory.Packages.props MSBuild looks for from
  the project's folder up, as far as the first found, and the restore's `obj/X.csproj.nuget.g.props` and `.targets`,
  which the window evaluates again for as a restore lands); the caches are read off the UI thread (`StateDocument.ValueAsync`).
  The shown file's project and what it references load first (`ProjectSession.ActiveProjectsAsync`).
  `Passenger.Roslyn.Extension/ProjectLoader.cs` subclasses the server's own `LanguageServerProjectLoader` so the server
  loads the host's builds (`passenger/loadProjects`, `passenger/projectInfo`, `passenger/projectLoader`), compiled
  against the server's publicized assemblies (`src/Publicize.targets`, which the debugger imports too); a server
  without the host builds its projects itself. A file in several projects (a multi-targeted project's) has the
  editor's target framework switcher (`CodeEditor.ContextLabel`, `MainWindow.TargetFramework.cs`), its requests naming
  the chosen context (`LspDocument.Context`, Roslyn's `_vs_projectContext`).
- `src/Passenger.App/CSharp`: the language server in the IDE. `CSharpService` runs Roslyn for the
  folder; `CSharpDocument` gives an open file its semantic colours and diagnostics; `CodeAssist`
  is completion, hover, signature help and the code actions (`app.code.*`), its popups on a layer
  over the editor; `WorkspaceEdits` applies renames and fixes across files (a code action through `ApplyAsCommand`, one
  command of global undo, which keeps a command that changed a file not open). `UsageHighlighting` asks the server's
  `documentHighlight` once a split's caret rests and is Highlight Usages in File. `CodeAssist.Surround.cs` is Surround With
  and Complete Current Statement, from the file's syntax with no server (surround templates are `LiveTemplates` entries
  with a `Surround` scope); `CodeAssist.Refactorings.cs` is Optimize Imports (shared with Code Cleanup), Safe Delete
  (the server's definition and references, the removal from `SafeDeletion`) and the server's Introduce Field,
  Introduce Parameter and Encapsulate Field under Rider's names. `TypeRename` renames a type with the files named after
  it (their name up to the first dot), from Rename, the Solution view's Rename of a file and MCP's `rename`; Rename's
  options are the store's `refactorings.json`. `LiveTemplates` is the built-in live and surround templates and Rider's
  macros; `TemplateSet` is the set in force (the settings' `TemplateChange`s, each a built-in's difference keyed by the
  shortcut it ships with, over the built-ins), which `ApplySettings` hands to `CodeAssist.Templates`. `CodeAssist.Dialogs.cs` is Change Signature, Extract Interface, Move,
  Pull Members Up and Push Members Down: the extension describes what is at the caret, a `RefactoringDialog` asks the
  options, and the answer is applied inside `EditorArea.Commands.Record`, which keeps the files a command creates so
  Undo deletes them. The server's colour and diagnostics refreshes are two
  events, each refreshing only its own. Roslyn sends one toast per project-load batch, so `ProjectLoad.cs` reads its
  project-system log lines (`Warning/Error while loading P: …`, `Successfully completed load of P`) and `CSharpService`
  keeps each project's load problems, said in the status bar as a warning or error and listed in Problems' Toolset,
  Environment tab. The server's progress is followed by its token (`OnProgress`); a restore is held until it names a
  project, and the server's own `CanonicalProject` (temp's roslyn-canonical-misc/…/Canonical.csproj, made for files no
  project holds) is never shown, logged as a warning, or read as a load problem.
- `src/Passenger.App/Editors`: `EditorArea` is a tree of splits, each an `EditorGroup` (tabs, find bar, editor); its
  `CodeEditor` and `Active` are the active group's, so code that acts on "the editor" keeps working, and wiring for every
  editor's events goes through `ForEachEditor`, since a split makes a new one. An editor window (Open Tab in New Window,
  or a tab dragged out) is an `EditorFrame` of the same `EditorArea`, never a second area, so a file shown in two
  windows is one `OpenDocument`; the folder keeps the windows in `WorkspaceState.EditorWindows`. Pinned tabs come
  first (`EditorTab.IsPinned`, kept as `KeptTab.Pinned`); `EditorArea.CloseTab` is the one close a person's goes
  through, which Reopen Closed Tab remembers. `EditorArea.Commands` is Rider's global undo: a command that changes
  several files or moves one (`Commands.Record`, as renames do) is undone and redone whole, asked first. The tab
  actions are `MainWindow.Tabs.cs`, filling a tab's menu through
  `EditorArea.TabMenuOpening` with `ActionContext.Tab` naming the tab they act on. `MainWindow.CloseSolution` ends the
  folder's session and shows the Welcome screen; `LeaveFolder` keeps or closes editors and `EndSession` ends the
  session, so new per-folder state belongs in `ProjectSession`, made with it and ended in its end. An image (PNG, JPEG,
  BMP, GIF, WebP, ICO) or a TTF/OTF font opens as a read-only preview: `OpenDocument.Load` gives it an `AssetPreview`
  (`OpenDocument.Asset`, shared by its tabs, disposed with the document, followed by its bytes through a folder moved
  elsewhere) and an empty text stand-in, and each group shows it through an `AssetPreviewControl` of its own; it is
  never saved, converted or written. The asset is a stand-in (`IsPending`, the tab saying it is loading) until its
  decode lands off the UI thread through `BackgroundWork` (`OpenDocument.Decode`, one at a time); a first decode that
  fails closes the tab and the status bar says why (`EditorArea.OpenFailed`), a later one keeps the picture under the
  banner. Only the folder watcher's word decodes it again (`FollowDisk`): `ChangedOnDisk`, an agent's lookup and Git's
  reload of open files read nothing of it. Its `Language` is Image or Font, which the status bar names; its `HoldsText`
  is false, as a workbook's is, so the status bar's text widgets, Find and Replace in Files, Local History's look at an
  opened file, a line's actions and agents' `editor_text` and `edit` pass it over.
- `src/Passenger.App/Docking`: the tool windows' frame, in IntelliJ's six places. `DockHost` is the layout and what
  it keeps; `Stripe`, `ToolPane` and `FloatWindow` (a window of its own that knows nothing of tool windows) are its
  parts. A window moves by dragging a stripe button or a header (`DockDrag` says where it lands); `DragGesture` and
  `DropOverlay` are the one drag and drop zone, which editor tabs share. The layout is kept in the state store as a
  `DockLayout`, with the folder open and as the one a folder without its own opens on; `DockHost.RestoreDefault` puts
  each window back where `Dock.Add` put it. A new tool window is a view and
  one `Dock.Add` in `MainWindow.AddToolWindows`.
- `src/Passenger.App/State`: the IDE's own state, in one `StateStore` under `PASSENGER_SETTINGS` or the user's local
  application data, kept only by the person's window, shared by every window of the process that keeps state there
  (`StateStore.Open`), with `layout.json` only the default for a folder with no layout of its own: the app's documents at its top (`settings.json`, `layout.json`,
  `recentSolutions.json`, `updates.json`, `find.json`, `refactorings.json`, `containers.json`)
  and each folder's under `workspaces/<name>-<hash>/` (editors and recent files, layout, breakpoints, bookmarks, run
  configurations, Find in Files' scope, `localHistory/`, the
  profiler's `snapshots/`), with the debugger's decompiled
  symbols in `cache/symbols/` at the root. A Passenger that Run, Debug or
  Profile launch (Passenger.App, or any program named Passenger) keeps its state in the launcher's `sandbox/`
  (`LaunchPlan.SandboxedState`, as IntelliJ's DevKit sandbox), unless its configuration sets `PASSENGER_SETTINGS`. A
  `StateDocument` is a record that is an `IStateDocument`: its `Upgrades` from each older version and its `Clamped` value;
  it is written debounced off the UI thread through a temporary file, merged over the file so values it does not know
  stay (and an entry another window removed stays removed; a list its `Sets` names merges as what each side added and
  took away), and a file that does not read is set aside as `.bad`, logged and said once in a window's status bar
  (`StateFile.TakeUnsaid`). New state is a document there, never a file of
  its own.
- Logs: one folder, `logs/` under the state root, written by `IdeLog` off a queue with every file rotated by size:
  `passenger.log` (IDE errors, the language server's warnings and errors, each folder's startup phases with times),
  `roslyn/` (the server's own logs), `lsp.log`, `dap.log`, `vstest.log` and `mcp.log`. Settings | Diagnostics turns on protocol
  traces (`TraceProtocols`) and a line for every program started (`LogEveryProgram`; otherwise only failing or slow
  ones); Help | Show Log opens the folder. What the language server says to show reaches the status bar. A rotation
  moves the log aside first, so one another program holds where it is (Windows: a reader not sharing deletion) is
  written on past the limit with its rotated logs kept, and tried again once it has grown a tenth of the limit more.
- `src/Passenger.App/Vcs`: Git through the `git` executable (`Git`, porcelain v2), Myers' line diff (`LineDiff`) for
  the gutter's change bars, and `VcsController`, the folder's working tree (its session's; the Git and Commit views bind to it): status, branch, each open file's bars,
  commit, push, update, checkout and rollback (after which open files take their text from disk); `DiffView` is the
  side-by-side diff and `CommitGraph` the log's lanes. `GitView` is the Git window's content: the Log and a `GitLogView`
  tab per Show History, given a `HistoryScope` (a file followed with `--follow`, a folder, or lines with `-L`), read by
  `Git.HistoryAsync`, as `vcs_log` with a path reads it. `MainWindow.History.cs` is Show History, Show History for
  Selection, Compare with Branch or Tag and Compare with Revision. `WordDiff` (Passenger.Text, IntelliJ's ByWord) marks
  changed words inside changed lines for `DiffView` (Highlighting Differences is `AppSettings.DiffHighlighting`),
  `ChangePopup` and `MergeView`. `ToolWindows/BranchesPanel` is the Log's branches tree, its operations in
  `VcsController` and `MainWindow.Branches.cs`; a branch filters the Log through `GitLogView.FilterBranch`, and
  favourites are the folder's `gitBranches.json`. `MergeState` is what a merge, rebase, cherry-pick or
  revert stopped on and Rider's words for its sides (during a rebase yours is git's theirs); `ConflictsDialog` is
  Rider's Conflicts dialog and `MergeWindow`/`MergeView` the three-way merge over `ThreeWayMerge` (each stage read with
  `git cat-file --filters` in the conflicted file's encoding, the result written with its encoding and breaks), only
  for a file both sides have (`GitConflict.CanMerge`: one a side lacks is kept or deleted whole, so the Commit window's
  Merge shows it chosen in the Conflicts dialog); `MainWindow.Conflicts.cs` wires them with Continue Rebase and the
  aborts, and `vcs_status`/`vcs_resolve` give an agent the same. A file moved through `EditorArea.MoveFile` takes its index entry with it, as git mv does. `MainWindow.Vcs.cs` is the branch widget, the Commit and Git
  windows' places (`ToolWindows/CommitView`, `GitLogView`) and the `app.vcs.*` actions; the widget gives each local
  branch the branches panel's `Menu(branch)` as its submenu (`MenuChoice.Holding`), so nothing checks out until
  Checkout is chosen.
- `src/Passenger.App/Packages`: NuGet. `PackageSourceSet` reads the folder's nuget.config files through
  NuGet.Configuration as restore does (feeds, credentials, packageSourceMapping); `FeedSet` searches All Feeds or one,
  over `V3Feed`, `V2Feed` and `FolderFeed` (what the tests use offline); each HTTP feed's requests go through
  `FeedClient`, with its own credentials or else `CredentialProviders` (NuGet's plugin protocol, through
  ChildProcesses), never logged, and only to the feed's own host: another its answers name that asks for them is
  refused; an advisory's address from a feed is a link only when it is http or https (`Advisory.Link`, `Releases.Web`).
  `NuGetConfigEditor` changes one nuget.config in place, every other character kept;
  `PackageManager` changes projects through `dotnet add`/`remove package`. `ToolWindows/NuGetView` is the window, its
  Packages and Sources tabs. A run's NuGet is its own on macOS and Linux (DOTNET_CLI_HOME under the run's folder).
  `VulnerabilityData` reads the VulnerabilityInfo resource of the folder's auditSources (else its enabled sources), as
  restore's NuGetAudit does; the window marks vulnerable installed versions and offers the lowest that fixes them.
  `StandInFeed` is the tests' v3 feed on loopback (optional credentials, an advisory resource); tests never reach
  nuget.org for a package or an advisory. A stand-in credential provider is a `nuget-plugin-*` program on PATH that
  sends its own Handshake request: restore runs `NUGET_NETCORE_PLUGIN_PATHS` entries with dotnet (which builds a script as
  a C# file) and waits for a provider's handshake, either way retrying the feed's 401s for about 45 s on CI.
- `src/Passenger.App/Endpoints`: the Endpoints window (`EndpointsView`, `MainWindow.Endpoints.cs`) over the open folder's
  .http and .rest files (`HttpFile`, read off the UI thread while the window is shown, again as one or an env file
  changes), their `{{variables}}` from the file and the environment chosen in http-client.env.json and its private one
  (`HttpVariables`), and their `# @expect` criteria (`Expectation`). Nothing goes over the network but on Test (Test
  Request, `AppActions.TestEndpoint`, a `Working` that Stop ends) or the Interactive Test dialog's Send
  (`InteractiveTestDialog`, through the window's `EndpointsView.SendAsync`), each on a handler and HttpClient of its own
  (`EndpointClient`), disposed before it answers; env files are read off the UI thread as it sends. A send that takes
  the person's environment (`$processEnv`, `$env.`, through env files' values too) or a body's file outside the folder
  (`< file`, links resolved) to a host the folder has not allowed asks first (`EndpointQuestionDialog`: the host and each
  read, an unset variable said); an Allow lasts while the folder is open, Always for this folder is the folder's
  `endpoints.json` (`EndpointGrants`, `ProjectSession.EndpointGrants`), and the body's file is read only once allowed. A
  redirect is followed by hand: to another host without the request's headers, and not when it would carry the body
  there, either said in the answer's notes. A body is read to `MostRead` and shown to `MostShown`, a request's
  variables filled in to `MostFilledIn`; a row's last outcome is kept by its name, else its method and URL. Tests send
  only to `EndpointsTests`' stand-in on loopback (its `localhost` name the other host).
- `src/Passenger.App/Containers`: the person's container engine through their `docker` CLI and its current context
  (Docker Desktop, Colima, OrbStack, Podman's, a remote one alike), never the Engine API nor a Docker package.
  `ContainerEngine` is the process's (`Shared`; a window's `MainWindow.Engine`, a test's stand-in CLI): the CLI found
  through `Programs` then Docker Desktop's, OrbStack's and Rancher Desktop's folders, every command through
  ChildProcesses (`Command` to stream, `RunAsync`, `RunForAsync` to read for a while, `JsonAsync`/`JsonLinesAsync` for
  `--format json`; one given a folder has PWD set to it, which Compose reads for `$PWD` over its working folder), its `StateAsync` (no CLI, not running, Windows containers, no Compose v2; the context's endpoint for
  `DOCKER_HOST`) and `Follow` (`docker events --format json` while anyone follows, started on a worker, never the
  caller's thread, which finding the CLI could hold on the login shell; ended by the engine, as Docker Desktop's quit or
  restart ends it, it starts again once `StateAsync` says the engine is ready, asked every 2 s doubling to 10 s, the
  followers told `engine` `stopped` and `started`, on which the window reads it all again). `ContainerSnapshot` reads what the
  engine holds at once (Compose projects by `com.docker.compose.project`, stopped ones too, their statuses from `compose
  ls` where the CLI has Compose v2, else from their containers' states, then containers, images, volumes, networks); `ContainerCommands` is each action's words on a row, which the window's actions and MCP's
  `container_action` share (a service's name, a label's, always after `--`, never read as a flag; an agent's start of an
  id names a container only when one container's id begins so, `ContainerRow.Named`). `ContainersView`
  (`MainWindow.Containers.cs`) asks the engine only while shown, its lists again as events say, a port's link on the
  engine's own machine (the host of a `tcp://` or `ssh://` endpoint, else localhost, `ContainerPort.LinkOn`); a
  container's Log (`docker logs -f` through `ProcessRunner` into an `OutputView`, its links the
  consoles'; a stopped container's kept as it was read, its scroll and Find with it, until one of its containers runs
  again), a project's merged one (a toggle a service), each with the editor's `FindBar` over it (Find's keys while
  the window has them; an earlier run's lines are not folded yet: the console has no folds), Stats (`docker stats`,
  streamed only while its tab shows) and Files (`ls` through `docker exec` as a folder opens, a file copied by `docker cp
  -L …:path -` under the state's `cache/containers/`, which `OpenDocument.IsReadOnly` takes as read-only, as Inspect's JSON
  there; what a container says is never trusted: a listed name with a slash, `.` or `..` is no file's and is dropped, a
  copy's place is refused unless it is under the container's folder of the cache (links resolved), the tar is read as it
  comes and a file over 16 MB refused at its header, the copy written beside its place and renamed over it, and a copy
  opened again, as Inspect's is, takes its new text in the tab it has, `MainWindow.OpenCopy`). Open Shell is `docker exec -it … sh` in a Terminal tab, an image with no shell said. Prune and a volume's
  Remove ask first; a project's Remove is `down`, its volumes kept, and `container_action` reaches no volume or image.
  The status bar and Problems' Containers tab say the engine's problem, the link starting Docker Desktop where it is
  installed; a test run of a project referencing a `Testcontainers*` package asks the engine first
  (`TestController.ContainerEngine`; untested, since a project referencing it restores from nuget.org). `ContainerLabels`
  and `FolderContainers` (`ProjectSession.Containers`) are what a folder starts and how it is ended (Rules); the store's
  `containers.json` (`ContainersState`) says the state ever labelled something, without which a start runs no docker.
  `EngineChecks` (`WebLanguages.Checks`, given each Dockerfile and compose file with its server, as ESLint is) is what the
  engine says of the open files while `StateAsync` says it can, each check off the UI thread as background work holding
  the file's `Checking()`, one at a time, dropped when the text moved on before it landed: BuildKit's own checks as a
  Dockerfile opens and is saved, never on unsaved text (one typed over as it ran waits for the next save; `DockerfileCheck`: `docker buildx build --call=check,format=json -` on its text, on the
  context's own builder, no context sent, its `syntax` parser directive in either form BuildKit reads (`# syntax=`,
  `// syntax=`) blanked to spaces and BUILDKIT_SYNTAX out of the CLI's environment (`DockerfileCheck.BuiltIn`), so the
  engine's own frontend checks it and no frontend image a file names is pulled or run, a text BuildKit would read as a
  JSON directive not checked at all; a rule's warning linked to its page, a build error but for a parse error,
  which the server marks, or a registry out of reach), while Settings' Check Dockerfiles with BuildKit (its Docker page,
  `AppSettings.BuildKitChecks`) is on, since BuildKit reads each base image's metadata from its registry; and Compose's
  model of a compose file as typed, which reaches no network (`ComposeCheck`, its commands run with
  `COMPOSE_EXPERIMENTAL_GIT_REMOTE` and `COMPOSE_EXPERIMENTAL_OCI_REMOTE` false, so an `include` of a Git repository or an
  OCI artifact is refused, not fetched, and Compose's word that it was is nothing marked;
  for the files Compose reads unasked: the first of compose.yaml, compose.yml, docker-compose.yml and
  docker-compose.yaml in a folder and the first override beside it; `docker compose --project-directory … -f - [-f
  override] config --format json --no-path-resolution`, the text typed as `-`, again as the other or the .env changes).
  Compose's error is marked where its message names a place (a parser's line, or a model path `ComposeText`, Passenger's
  reading of block YAML, finds), else on the first line, or listed as the other file's; each `${VAR}` gets a hint of
  what Compose's own interpolation makes of it (asked as an `x-passenger-values` list in the folder, never worked out
  here), and a base file's scalar its override changes the value Compose ends with, where the base's own model has the
  scalar written (a value it normalizes gets none). A secret's value shows as the same dots whatever its length: one
  whose variable, the key or list item's `KEY=` it is given to, or the name just before it (`Password=`) is a secret's
  (`ComposeCheck.Secret`: PASSWORD, PASSWD, SECRET, TOKEN, CREDENTIAL or CONNECTIONSTRING anywhere in it, `_`, `-`, `.`
  and case aside; or a word of it, split there and at case turns, PASS, PW, PWD or KEY, or its plural, or ending in KEY
  but for an ordinary word's, as MONKEY's; never the shell's own `PWD` and `OLDPWD`), or that stands as a URL's password
  (`postgres://u:${PGPASS}@db`); a value holding such a URL shows it with its password as the dots. Their problems are Problems'
  Containers tab by file
  (`ShowContainerFiles`); with no engine or no buildx nothing is shown or said per file. A check costs well under a
  second (buildx's 0.1–0.6 s, a base image's metadata read from its registry the first time; Compose's about 50 ms a
  run).
  Its tests (`ContainersTests`, `McpTests.Containers.cs`, `EngineChecksTests`) skip saying why unless `docker info`'s
  OSType is linux (`EngineChecksTests`' Dockerfile one without buildx too; its stopped engine is `StandInCli`'s), use
  busybox and Compose files they write, and never prune or start or stop Docker Desktop: an engine that stops and starts
  again, or has no Compose v2, is `ContainersTests.StandInEngine`'s, a shell script (so skipped on Windows) whose `state`
  file says up or down; stand-in container tools are a script copied to `ContainerTools.Copied` in a busybox container.
  Container run configurations (#343): a `RunConfiguration`'s `Kind` (Project, Compose, Dockerfile, SdkContainer), made as
  the folder opens by `ContainerConfigurations` (compose files beside the folder, the solution or a project, overrides with
  them; a .dcproj's properties and DockerCompose profiles, read as XML; a runnable project's Dockerfile; a project file
  that has the SDK make its container; a Docker or SdkContainer launch profile is its project's). `ContainerPlans.ResolveAsync`
  reads Compose's own model (`ComposeModel`, `config --format json`; a Dockerfile or SDK container configuration is a
  compose file of one service Passenger writes) and the project each service runs (its Dockerfile's ENTRYPOINT assembly
  or dotnet publish, `ComposeDeclarations.Built`, else the configuration's choice, else the .dcproj's), and every command
  takes Passenger's override from the folder's state (`workspaces/…/containers/<config>.override.yaml`, JSON, written
  after the host's build): labels on every service, image build and network; in fast mode each .NET service's runtime
  stage (`DockerfileStages.FastStage`), `dotnet /passenger/app/X.dll` as PID 1 (`init`) over the build's output mounted
  read-only, its static web assets' folders mounted and its manifest rewritten (`ASPNETCORE_STATICWEBASSETS`); in images
  mode BUILD_CONFIGURATION=Debug where the Dockerfile declares it; the dev certificate (`DevelopmentCertificate`) for a
  service that serves HTTPS and user secrets in Development; a fast .NET service's ports the file leaves to the engine
  chosen free once (`FreePort`) and put in the file's place (`!override` on JSON's flow list, Compose 2.24 on), since a
  restart otherwise publishes another. An SDK container as deployed is the SDK's own image (`dotnet publish
  -t:PublishContainer` into the engine, `ContainerRun.PublishAsync`, named `passenger-<config>-<folder>:passenger`, which
  carries no label of Passenger's). `ContainerRun` (`ProjectSession.ContainerRun`) is `up
  --detach --wait` streamed into the Run console, then `logs --follow` through the session's `Runner` (so Run's Stop,
  `IsRunning` and its end are the log's), Compose's own watch for other services that declare one, Rerun's restart of the
  services whose build changed (`Changed`, by the output's stamps), and
  `StopAsync` (`down`, or `rm --stop` of the .NET services when backing services are kept, which then go with the folder).
  `ContainerPaths` maps an image's paths (its Dockerfile's COPY of the context into WORKDIR, and `/_/`) to the workspace's
  and back (a container's path never to a file outside the folder it maps to: a `..`, a `//` or a rooted rest gives
  none), for `ConsoleLinks.Paths` (a Compose log's "web-1  | " prefix passed over) and `BuildOutput.Parse(line, paths)`;
  the debugger takes it at the protocol's edge (`AdapterPlace`, a service's `DotnetService.Paths` in images mode,
  `ContainerPaths.None` in fast mode). A run is started one of three ways (`ContainerStart`): Run; Debug, its services
  marked Debug (else every .NET service it starts, `ContainerPlan.Debugged`) held at their runtime's start
  (`ContainerPlans.HeldVariable`, DOTNET_DefaultDiagnosticPortSuspend=1, with DOTNET_MODIFIABLE_ASSEMBLIES=debug, in the
  override); and Run with Hot Reload (Settings' Hot Reload when running, or `run`'s `hotReload`), every .NET service held, since
  dotnet watch would need the SDK in the image. `ContainerRun.UpAsync` runs `up --wait` while each held service is attached
  to alongside (`BuildRunController.AttachAllAsync`: its container from `compose ps -q` once Compose starts it, the tools
  copied in for its image's system, never mounted, since the system is known only once the image is and an engine shares
  only some of this machine's folders; its program from the tools' `processes` by its assembly), so a healthcheck or a
  dependent's `service_healthy` is not waited on before the attach lets it go. Debug gives each service a Debug tab named for
  it, of the run's group (`DebugTab.Group`, the configuration's name: Hot Reload's Apply goes to each tab of the group),
  its console the container's `docker logs --follow --since` its start (`AdapterPlace.Output`); Run with Hot Reload attaches
  quietly (`DebugController.AttachForHotReloadAsync`, `DebugSession.Quiet`: no breakpoint sent, no exception stops it, the
  store never told), Apply Hot Reload Changes and a save applying the edits through it (`ApplyRunningAsync`). An edit Hot
  Reload cannot apply builds the service's project again (fast mode) and restarts it (`RestartContainerServicesAsync`: only
  the services whose project holds a rude edit, since Roslyn names one in another project rude to each session; images mode
  `up --build --no-deps --force-recreate`), its session ended first and attached again, as Rerun's restart of a held service
  does. A Release build (the tools' `optimized`), a runtime with DOTNET_EnableDiagnostics=0 (not held, and not attached: it
  runs on) and an image of another processor than the engine's are said in the status bar after the run comes up. Stop ends
  the run's sessions (`ContainerRun.Debugged`) before the containers go, since a container taken from under its adapter
  would end it as a failure. Profile of a container configuration is still refused (`LaunchPlan.InContainers`; #345).
  Edit Configurations' Compose page lists the services from Compose's model
  (`ContainerPlans.ServicesAsync`, `Run/ComposeServicesTable`: each .NET service's project, what Run does with each), and
  the gutter's run icons (`RunMark`, `MainWindow.RunMarks.cs`) stand beside a used compose file's services and a used
  Dockerfile's final FROM, their menu running or debugging that one service (`BuildRunController.RunContainersAsync`). `ContainerRunsTests`
  run them against the engine on aspnet:10.0 and busybox; their HTTPS test exports the machine's development certificate
  first and skips, saying why, when the export does not come back (on macOS a locked login keychain asks for its password,
  which the IDE's own export waits a minute for, `DevelopmentCertificate.ExportLimit`), and their SDK container image
  (unlabelled, the SDK's) is removed by the test that made it.
  `ContainerTools` is the
  container tools on this side (`src/Passenger.ContainerTools`, below): a copy for a system (`ContainerSystem.Rid`: the
  image's processor, musl when docker cp's archive of `/lib/ld-musl-*` begins, nothing written here and the copy ended at
  its first header, read once per image; an image never run is read from a container created and removed,
  `SystemOfImageAsync`) found beside the app (`container-tools/<rid>/`), in a dev build's `artifacts/container-tools/<rid>/`
  (one of another version, as its `passenger-tools.dll` says without being run, `VersionOf`, passed over and named should
  the download then fail) or the state's `cache/container-tools/<version>/<rid>/`, else downloaded from the
  release of this version (`ReleaseFeed.Downloads`, `Releases.DownloadAsync`, HTTPS only) and checked against that
  release's SHA256SUMS (a mismatch refused and said), unpacked through a temporary folder renamed, one download a system
  at a time in a process, another process's copy that lands first kept and a folder with the tools in it never deleted
  (`ContainerToolsSource`, the window's `ContainerToolsFrom`); then mounted read-only at `/passenger/tools` where Passenger
  starts the container (`MountFor`, the `-v` value or Compose volume a run configuration adds) or copied to
  `/tmp/passenger-tools` (`CopyIntoAsync`, refused, saying both versions, unless the copy's `version` there is the IDE's),
  used only when its `version` is the IDE's; what the tools' `processes` answers is read as it comes (an entry with no
  numeric id passed over). `ContainerProcesses` is what Attach to Process lists of the running containers, each inspected
  on its own so one that ends meanwhile leaves the rest listed: the tools' `processes` where they are in, else `docker top` from
  outside (the dotnet host, a .dll, or in a .NET image any program but a shell), their ids there 0 until the tools,
  copied in as the person attaches (the dialog's heading says so first), name them.
- `src/Passenger.App/Discord`: Rich Presence over the Discord app's local IPC, Passenger's own client
  (`DiscordConnection`; `DiscordPresence` on one worker off the UI thread, at most one update every 4 s, a look every
  20 s without Discord). Only the person's window reaches Discord (`ShowActivityInDiscord`, called by App); the test
  run points `PASSENGER_DISCORD_IPC` at a place of its own where nothing listens. Show activity in Discord is on at
  first and shows Passenger only, in the site's words (`DiscordPresence.OwnDetails`, `OwnState`, `LargeText`), also
  while idle; naming the solution, or the solution and the file, is the person's choice, and off tells Discord nothing.
  Settings' version 2 upgrade (`AppSettings.DiscordOnNamingNothing`) turns an older settings.json that never turned it
  on to on, Passenger only.
- `src/Passenger.App/Settings`: `AppSettings` (the store's `settings.json`) and Rider's searchable `SettingsDialog`;
  `SettingsStore` holds them once per process: a change reads settings.json again, puts over it only what changed and
  raises `Changed`, which every window follows (`MainWindow.Follow`), the file watched for other Passengers. A new
  setting is a property, an option row in the dialog, a line in `MainWindow.Follow` (the window's side) or
  `SettingsStore.InForce` (the process's), and its range in `Clamped` when it has one. `SettingsDialog.Keymap.cs` is the Keymap page: Rider's
  Edit Shortcuts menu, the Keyboard Shortcut dialog (chords) and Find Actions by Shortcut; `Keymap.Conflicts` is Rider's
  conflict rule, and a conflict is removed, never kept, since a shortcut runs one action. A chord can't be a native
  macOS menu key equivalent; in-window menus show it as text.
- `src/Passenger.App/Updates`: the update check and the update. `Releases` asks GitHub's releases/latest (a GET naming
  Passenger and its version, nothing else) and offers a release newer than `Product.Version`, never a draft or
  pre-release; its files download over HTTPS only (`Downloads.cs`), checked against the SHA-256 the same release gives.
  `Updater` is how this copy installs one, only for a copy its installer or disk image put in place
  (`Updater.OfThisCopy`): `VelopackUpdater` on Windows (the release's `releases.<rid>.json`, full packages only) and
  `DiskImageUpdater` on macOS (the release's disk image checked against SHA256SUMS and its app by codesign, swapped in
  as the process ends). Every other copy (`just run`, tests, `--mcp`, screenshots) only links the download.
  `UpdatesState` (`updates.json`) keeps the version announced and the one being installed. `MainWindow.Updates.cs`
  is the status bar's notice, Check for Updates, Restart to Update and the daily check while Settings' Check for
  updates is on; `Program.Main` installs a ready update last.
- `src/Passenger.App/Templates`: New Solution and Add > New Project. `ProjectTemplates` reads `dotnet new`'s installed
  templates once per SDK from its own cache (`templatecache.json`, refreshed by `dotnet new list`) and each template's
  `ide.host.json` for the options an IDE shows; `NewProjectDialog` is Rider's New Project dialog;
  `MainWindow.NewProject.cs` opens a new solution through `Open` and adds a project through `SolutionEdits`.
  `ProjectTemplates.Ahead.cs` reads the templates ahead once a person's window has started (`MainWindow.PreloadTemplates`;
  screenshots and `--mcp` don't), keeps the newest per SDK (`Ready`, what the dialog opens with), and reads again when a
  watch on dotnet new's `packages.json` (home or `Hive`) or the .NET install's sdk/templates/template-packs folders sees a
  change, raising `Changed`. `TemplateFamilies` groups them as Rider's New Solution does: a template group is a Type
  that joins the first of Rider's project types its classifications name (`Kinds`, Rider's names and order), the rest
  grouped by their short name's family; `NewProjectDialog` lists families under a search, with a Type chooser whose
  choice sets the options below it, values the person set kept where the new Type has them. `TemplateFamilies.RiderOrder`
  is Rider's sort, by order then name (Console 10 … Unit Test 70; Aspire, Azure Functions and MAUI 90); MAUI is a family
  only where dotnet new lists its workload's templates (GitHub's Windows runner has them), so TemplateFamiliesTests ask
  `dotnet new list --tag` in the run's home what the machine has.
  `TemplatePacks` is the template packages New Project offers when dotnet new lacks them (Avalonia's, and Rider's
  Aspire, Azure Functions and AI Extensions), their kinds kept with the version they were taken from; choosing one
  offers Install (`dotnet new install` through a `ProcessRunner` into the dialog's console), then the templates are
  read again (`ProjectTemplates.Forget`). Solution templates are New Solution's only and make the solution themselves.
  dotnet new runs with the template's skip-restore option and `DeveloperTools.TakeOut` removes Avalonia Accelerate's
  DevTools from what it made before Passenger restores; every `dotnet new` goes through `ProjectTemplates.New` or
  `NewCommand`, which add the IDE's `Hive` (`--debug:custom-hive`) when set.
- `src/Passenger.Profiler`: the profiler with no UI. `ProfilingSession` starts a program held at a diagnostic port of
  its own and streams EventPipe to a .nettrace; `Snapshot` reads it (TraceEvent) into call trees and hot spots.
  `App/Profiling` is the window (`ProfilerView`, `FlameGraph`), `ProfilerCommands` the Profile action, which holds its
  folder from its first moment to the hand-over (`launching`), so Profile and Attach Profiler are refused while a start
  waits for its program. Every session
  enables `UiFrames.Provider`: `Snapshot.UiFrames` are an Avalonia app's frames, each with the compositor's first
  drawing after it, recorded by the inspector (below); `App/Profiling/FramesView` is the Frames page (`FrameChart`, the
  layout table by type then control, own time first), a control's row opening its XAML and, for the Live Visual Tree's
  process, choosing it there; `UiFrames.Percentile` (interpolated) is the one percentile for frames, and MCP's
  `profile` of an inspected Avalonia app always answers `frames`, with `why` when there are none (AgentApp takes
  `--quit-once-drawn` / `--quit-at-once` for its tests). `ProjectSession.ProfileStarting` is a Profile not yet handed
  over, which the folder's end cancels; `ProjectSession.Profiled` is the program it handed over, until its end has landed
  (`ProfiledProgram.Landed`): Get Snapshot ends the run and lets go of its port (`StopAsync`), but the program goes on,
  Kill, Stop and the folder's end ending it, and is put away (`DisposeAsync`, after which its end is never heard) only
  once it has ended. It writes to the Run console, which is one program's at a time: Profile is refused while Run's
  program has it, and Run while Profile's does. The folder's end lets go of every run (`LetGoAsync`: its port and
  listener), the leave waits for the program's end to land, and the window's next session clears the profiler window's
  run (`ProfilerCommands.Rebind`). `ProfilingSession.AttachAsync` profiles a running program at its own diagnostic port
  (Run > Attach Profiler to Running Program: the debugged one, else Profile's going on, else Run's, in the Run console
  or the terminal, under Hot Reload the app dotnet watch started, found by `DotnetProcesses.Under` as the attach
  begins); an `Attached` one is never killed. A program in a container is profiled there (#345): `AttachAsync` given a
  `ProfilePlace` (the tools' command over `docker exec -i`, the container's name, a PDB document's path made this
  machine's) runs the tools' `trace` there (`InContainer.TraceAsync`: the same `ProfilingSession.Providers` at the
  program's own diagnostic port, the .nettrace on their standard output, a `recording` line on their errors once it
  begins, their input's end stopping the session with its rundown; `resume` lets a program held at its start by
  DOTNET_DefaultDiagnosticPortSuspend=1 go once recording), the bytes written to the trace here as they come and read by
  `Snapshot` as ever, which keeps the `Place`. Attach Profiler takes the debugged container session first
  (`DebugSession.Place`, by its id there and the debugger's path map), else, from the action with nothing of the
  folder's running, one chosen among the running containers' programs in Attach to Process's dialog
  (`AttachPurpose.Profiler`, `MainWindow.ChooseProfiledAsync`; resumed), the tools put in by `ProfiledInContainerAsync`
  as the debugger's are, under a `Working`; MCP's `profile` takes `container` with `attach`. Hot spots' sources
  (`MethodSources.FindAsync`) are read by the tools' `sources` in the container, where the modules are, each document
  made this machine's by the place's map (as it is for a build of this machine's). A container's run has no inspector,
  so the Frames page (`FramesView.InContainer`) and MCP's `frames` of an app with Avalonia's modules say why it is empty.
  `MemorySnapshot` is a heap dump read with ClrMD into `HeapGraph` (roots by dotMemory's kinds, shortest paths, a
  dominator tree for retained bytes); `HeapHistory` follows a run's snapshots' objects through every GC (an EventPipe
  session of the GC's survival and movement events), so `SnapshotComparison` gives new, dead and survived objects;
  `App/Profiling/MemoryView` is the Memory page. A container's snapshots are taken there (`ContainerHeap`: the tools'
  `heap`, started with the first snapshot and kept for the run, `InContainer.HeapAsync` running `HeapHistory` itself
  with the container's own DAC, a line `snapshot` a snapshot), each sent whole (`HeapWire`: a kind, its version, its
  length, then the graph's arrays and the earlier snapshots' identities, deflated, 7-bit numbers, addresses as their
  steps) and read back into the same `MemorySnapshot`, so the Memory page and comparisons know no difference; a frame of
  another version is refused, and nothing in one is trusted: a snapshot's frame over 1 GB (a failure's over 64 KB) is
  refused at its header, its body taken as it comes, inflated to 1 GB at most, each count held to the bytes left and each
  index to what it indexes, and whatever fails to read is said as the snapshot's refusal, the tools ended and started
  again for the next; of the tools' errors only their last line is kept, cut at 2,048 characters (`ErrorLines`). ClrMD 4 finds statics itself and reads .NET 11;
  `HeapGraph` loads the program's own runtime folder's DAC and gives ClrMD a locator that finds nothing (no symbol
  server, no cache in the person's app data). ClrMD's Azure packages stay carried: its `FileLocator` getter loads
  Azure.Core when the DAC reads memory a dump lacks, and pruning them hung snapshots on Linux and left types unnamed on
  Windows.
- `src/Passenger.Terminal`: the terminal. `TerminalScreen` is the VT emulator (no UI), `TerminalView` draws it and
  encodes the keys, `TerminalSession` runs the shell in a pseudo-terminal (Porta.Pty, which tells a program's end from
  the spawn on, so the start looks once more after listening: `sh -c "exit 3"` can end first); on Windows PowerShell is
  told `HISTFILE` as PSReadLine's `HistorySavePath` before its first prompt, so a test's or screenshot's never reaches
  the person's history. `MainWindow.Terminal.cs` is its
  tool window and the keys it takes ahead of the IDE's.
- `src/Passenger.App/Run` and `ToolWindows`: build and run through the .NET command line
  (`ProcessRunner` streams a process into a document; `BuildOutput` reads MSBuild's error lines),
  and the bottom panel's Build, Run and Problems windows (Problems' File tab lists markers moved by keys once typing
  pauses, 250 ms, and fresh markers for the text already shown at once; its solution-wide list asks a refused
  workspace pull again after a pause, 1 s doubling to 30 s, and `LanguageServer.RefuseNext(method, n)` refuses one for a
  test; Roslyn holds a workspace pull open until the next change, so an answer lost after it was sent waits for another). Every console (Run, Build, Debug, the profiler's) is written
  through `ConsoleSink`: a frame's output at a time, colour codes stripped whole across reads, the head trimmed past
  Rider's 1024 KB, and what is typed into it sent to the program. A run configuration is data (`RunConfiguration`: the
  folder's own in `runConfigurations.json`, the rest made from the executable projects' launch profiles, those that
  cannot launch here saying why: a project whose framework references Microsoft.WindowsDesktop.App runs only on
  Windows (`ProjectModel.WindowsOnly`), so off Windows the Solution view marks it, `RunConfiguration.WhyNotHere` greys
  its configurations and `LaunchPlan.ResolveAsync` refuses it before its build; a container kind's are run by
  `BuildRunController.Containers.cs`, Containers above, the store's version 2 giving older ones the Project kind);
  `LaunchPlan.ResolveAsync` works one out once, in one MSBuild request, into what Run,
  Debug, Profile, Hot Reload and a debugged test host start, and `RunConfigurationsDialog` is Run > Edit
  Configurations. A build says what started it (`BuildCause`): explicit builds open the Build window as they start,
  the build before Run, Debug or Profile only with Settings' implicit-build option, a test run's never, and its end
  brings the Build window forward as Showing build results says (errors only by default). Every build holds the host's
  generating design-time builds off while it runs (`ProjectBuilds.HoldAsync`, released in `FinishBuild`), its dotnet command
  starting once the host answers (`ProcessRunner.StartDotnet`'s `after`), so a test reads `Builder.ProcessId` only once
  it has started; dotnet watch's own builds aren't held, and Run with Hot Reload is never refused for the working folder
  its build makes (`LaunchPlan.WorksWhereBuilt`: a project never built gets its output folder made before dotnet watch
  starts there). A test project's first Project launch profile gives its variables to its tests. A configuration that
  uses the terminal (Rider's Use external console) runs in a Terminal tab of its name (`TerminalPanel.RunAsync`, a
  `TerminalRun` with its process, exit code and Stop); Debug answers the adapter's runInTerminal the same way
  (`DebugController.RunInTerminal`), and `debug_input`/`debug_output` use that tab. In a program's tab, Run, Debug,
  Rerun, Stop and the debugger's keys stay with the IDE. Console links come from `ToolWindows/ConsoleLinks` (URLs, .NET
  stack frames, compiler messages; in the terminal also paths to any file, hidden until Cmd/Ctrl), drawn by
  `CodeEditor.Links` and `TerminalView.Links`; `MainWindow.Browse` is the only way the IDE opens a browser (recording
  `Browsed` for tests), and Run and Debug open a plan's `BrowserUrl` once its port answers (`Run/BrowserLaunch`).
- `src/Passenger.App/Testing`: unit testing works in Passenger's own `TestItem` (its id the project and full name, plus
  a data row's name) and `TestOutcome`, behind `ITestRunner`; `VsTestRunner` is the only file that knows VSTest's
  object model. `TestFinder` shows a solution's tests from code as it opens, before any build: test projects' files
  parsed off the UI thread, an open file's from its own parse (`TestsAtCaret.Read` is the one reading of a file's
  tests), changed ones through the folder watcher; discovery and runs merge into those rows by id.
  `MainWindow.Tests.cs` wires it. `TestRunners` sends each built assembly to its runner: a Testing Platform application
  (its `Microsoft.Testing.Platform.Application` assembly metadata: xUnit v3, MSTest's runner, TUnit) to
  `TestingPlatformRunner`, which runs it with `--server` through ChildProcesses and speaks the platform's JSON-RPC, one
  request per process; every other to `VsTestRunner`. `TestTree` groups by Rider's Group By levels (`TestGrouping.cs`) and filters by
  outcome and search; code that wants every test asks `TestTree.Methods` (each method once), never `Roots`. The
  grouping and filters are a folder's `unitTests.json`. `TestFrames` gives a run of a project with Avalonia.Headless
  and xunit v3 beside it the startup hook `src/Passenger.TestFrames` (carried under `testframes/`,
  `PASSENGER_TEST_FRAMES=1`): when an exception reaches xunit's runner frames on the headless session thread, it adds
  each shown window's `CaptureRenderedFrame`, popups drawn in, to the test's attachments as a PNG, before Dispose runs.
  `TestOutcome.Attachments` is read from VSTest's `TestResult.Attachments` and the platform's `attachments.N.uri`/
  `display-name` properties; `ToolWindows/TestFramesStrip` shows every picture attachment over the details, and
  `FrameViewer` shows one at its size with zoom.
- `src/Passenger.App/Navigation`: Recent Files and the Go to popups (Go to File scores every file on every key on the
  UI thread, so `FuzzyMatch` allocates nothing and first rejects a name without the query's letters in order),
  `NavigationHistory` (Back and Forward, the places
  edited for Last and Next Edit Location, and those Recent Locations lists, carried through edits) and
  `RecentLocationsPopup`; `Navigator` records the places visited and edited. Base and Derived Symbols use the
  server's type hierarchy, references and implementations, since Roslyn has no go-to-base request
  (`CSharp/Declarations.cs`). Find in Files is `Navigation/FindInFilesPopup`; Rider's scopes (Solution, Project, Directory, Scope) and file mask are
  `Workspace/SearchScope.cs`, which `MainWindow.Search.cs` turns into files (`ScopeOf` for Solution view rows).
- `src/Passenger.App/History`: Rider's Local History. `LocalHistory` is a folder's store under
  `workspaces/<name>-<hash>/localHistory/` (content once per hash, Brotli, in `blobs/`; a JSON line per event in
  `changes.log`), fed by `FileText.Wrote` (every IDE write, with the bytes it replaced), the folder watcher (External
  change, or the `VcsController.Activity` name), files opened, `EditorArea.MoveFile` and Explorer deletes, off the UI
  thread; it prunes to Rider's 5 days of activity and a size cap (`HistoryKeeping`), working from the log on disk, since
  another process over the same state (an agent's `--mcp`) writes to it too. `LocalHistoryView` is the tool
  window, reusing `DiffView` on the file's document. A test that deletes through the IDE sets `Trash.Folder`.
- `Explorer/FileNesting` nests a project's files in the Solution view by Rider's rules (its
  `ProjectViewNestingRulesProvider`s, chained as IntelliJ's `FileNestingBuilder` does), the project's `DependentUpon`,
  and `appsettings.*.json`; File Nesting… in the Explorer's options turns the rules off (`AppSettings.NestFiles`). A
  rename, move or copy takes a file's dependents along, and what names a file moved follows it (`MainWindow.FollowMoved`,
  inside the move's command): every project's items through `ProjectEdits.MoveItems` (an `Include`, `Update`, `Remove`
  or `Exclude`, a glob by the folder it begins in, `DependentUpon`, `LastGenOutput`, a `ProjectReference`) and the
  solution's projects and files through `SolutionEdits.Move`. A tool window's own header ⋮ items are `ToolWindow.Options`.
- `src/Passenger.App/Workspace`: `SolutionModel` reads solutions and projects for the Explorer's Solution view (a
  Docker Compose project, .dcproj, stays in the solution's entries but out of `WorkspaceModel.Projects`, its
  `ComposeProjects`: never evaluated, design-time built or built, and a solution holding one is built project by
  project, since its SDK is Visual Studio's), and
  `SolutionEdits` is every change to a .sln or .slnx (solution folders, the files they list, projects in and out, paths
  following moves): a .sln line by line and a .slnx through XDocument with its whitespace kept, so the rest of the file
  stays byte for byte as it was; never `dotnet sln`, which rewrites a whole .sln. `MainWindow.Solution.cs` is the
  Solution view's New Solution Folder, Existing Project, Existing Item, Rename and Remove; a project's Rename
  (`MainWindow.RenameProject`) renames its file, its solution entry (a .sln's name too) and every reference to it
  following, one command Undo and Redo take whole. `ProjectEdits` puts a project's
  `ProjectReference`s in and takes them out as text, the rest kept byte for byte (never `dotnet add reference`), for
  Add > Reference (`MainWindow.References.cs`), and sets a property (`SetProperty`, one in a comment passed over) for
  Add > Docker Support's SDK route. `MainWindow.DockerSupport.cs` is Add > Docker Support on a project (a Dockerfile
  beside it and a .dockerignore at the context, the solution's folder; or EnableSdkContainerSupport and
  ContainerRepository) and Add > Docker Compose on the solution (compose.yaml, a Dockerfile for each project chosen
  without one, the backing services its packages call for with healthchecks and connection strings, listed in Solution
  Items), the text from `Workspace/DockerFiles`, each Add one `Commands.Record` with every file watched before it is
  written and an open project or solution file taking its new text inside it (`FollowWritten`), and refused, saying so,
  while a project is still being evaluated. A project's image is aspnet when it or a project it references has the
  ASP.NET Core framework reference as evaluated, or its restore's `project.assets.json` says a package or reference
  brings it, else runtime; the files a restore reads are copied by the names the disk gives them, a project at the
  context's root into `./`. A Dockerfile a project has is built from the folder its COPY sources are found in (the
  project's unless more are from the solution's) on the ports it sets (ASPNETCORE_HTTP_PORTS or ASPNETCORE_URLS, else
  EXPOSE, else 8080 for ASP.NET Core), read through `DockerfileStages`; only Passenger's own new Dockerfile takes the
  launch profiles' ports. A connection string reaches the backing service its value's shape names
  (`BackingService.Reached`: Host= postgres, Server= or Data Source= SQL Server but a file's, mongodb://, amqp://, a
  Redis host:port; one of another shape, SQLite's, is left as the app has it), its database kept and made by
  POSTGRES_DB (RabbitMQ's virtual host by RABBITMQ_DEFAULT_VHOST), compose.yaml saying in a comment what SQL Server and
  a second Postgres database leave to the app. `MainWindow.FileMoves.cs` is the Explorer's Cut, Copy, Paste and drags,
  every move through `EditorArea.MoveFile` so open files, breakpoints and bookmarks follow.
  `EditorConfig` reads the .editorconfig files over a file (its own parser and globs); `FileIndent` picks a file's
  indent (its .editorconfig, else `Indentation.Detect` on its lines, else its language) and names it in the status bar;
  `OpenDocument.Save` applies the save-time properties. Anything Passenger itself writes to a project, solution,
  nuget.config or Directory.Packages.props saves that file first if it's open (`MainWindow.SaveBeforeWriting`, or
  SaveAll before `dotnet add package`) and then loads what it wrote (`FollowWritten`); a delete saves the open files
  under it first, and holds off design-time builds under it (`ProjectBuilds.HoldOff`). Every write of a workspace file
  goes through `FileText.Save`: a temporary file and rename, through symlinks, permissions kept, on Windows its
  attributes, ACL and streams (ReplaceFile, `FileText.Over`), on macOS its tags, ACL and creation date
  (`MacFileMetadata`), a read-only file refused (`ReadOnlyFileException`; its first edit, Save and the
  window's close ask Clear Read-Only Status, `MainWindow.ReadOnly.cs`), an open file whose folder another program moved
  or deleted written back there only by the person's Save or Save All (`FolderGoneException`: no autosave makes that
  folder again; the open files under a folder moved elsewhere follow it, `EditorArea.FollowMove`), Local History given
  the old bytes; projects,
  solutions, nuget.config, .gitignore and merge results included, never `File.WriteAll*`. A server's edit to files not
  open goes through `WorkspaceEdits.StaleAsync` (refused if one was written within 2 s of the request or its text differs
  from the server's, the extension's `DocumentTexts`; an agent's call waits for the server instead, `Patience`). A project edit or a save waits out a reader that shares no
  writes (Windows refuses the write while a project load reads the file) for up to 2 s (`FileText.WhileHeld`). Every
  edit from the language server goes through
  `LineEndings.Fit`, then `Indentation.Fit`. `OpenDocument` keeps how its file is stored, encoding, BOM and
  `LineSeparator` (a CR file's breaks are held as '\n' and written as '\r'); the status bar's widgets and File > File
  Encoding / Line Separators change them (`MainWindow.FileProperties.cs`), each one undoable step, a choice winning
  over .editorconfig's while the file is open. `FileEncoding` lists the encodings; `EncodingChange` weighs Reload
  against Convert as Rider's EncodingUtil does. Bytes that aren't UTF-8 are read in the .editorconfig's 8-bit charset,
  else the system's ANSI code page on Windows, else windows-1252 (`FileEncoding.EightBit`), Latin-1 only when chosen; a
  typed character the encoding can't hold is said in the banner (`OpenDocument.EncodingWarning`) before a save.
- `src/Passenger.App/PassengerApp.cs`: the process root. Its project windows (a folder or solution each), the one in
  front, and the one `OpenProject(path, ThisWindow | NewWindow)` that a start, Open, the recent solutions, the Welcome
  screen, the Window menu, the system's file activation (`App.OnActivated`) and `--mcp` go through; `NewWindow` makes a
  window as the one it is opened from. `MainWindow.Open` asks Rider's Open Solution (`OpenProjectDialog`) unless
  Settings' Open project in answers; what follows an open (New Solution's build) is its `then`, told the window it opened
  in once the session has begun there (`OpenFolder`'s `opened`), never when it was not opened. Quit keeps every
  window's solution for the next start; a window closed alone does not. A window that asks, as Passenger or the system
  quits, what to do with changes it could not save holds the quit until the person answers, then carries it on or gives
  it up (`PassengerApp.Answered`; Restart to Update's restart goes with it). Discord's one presence (the front window's) and
  the one watch for updates live here. A window's state is its own fields, and a static says why it belongs to the
  process.
- `src/Passenger.App/ProjectSession.cs`: one open folder's services, made together and ended together: the folder and
  its solution, the language server, index, watcher, Local History, state, recent files and places, run
  configurations, the build and run runners, terminal and profiled runs, tests, the debugger and breakpoints, Git and
  bookmarks. `LeaveAsync` when the window stays (opening another folder waits for it, within the window's limit),
  `CloseAsync` with the window. The window builds `BuildRunController`, `ProfilerCommands`, `Navigator`, `PopupHost`
  and `StatusBar` over it, each a file of its own given an explicit parts record; a handler hung on a session acts only
  while that session is the window's (`Bind`).
- `src/Passenger.App/StatusBar.cs`: what the status bar says, its whole text its tooltip, and a `StatusPlace` a click on
  it goes to, held only while those words stand (a link then, `Button.statusMessage.place`): a load problem's row in
  Problems' Toolset, Environment, a build's problems in Build, Hot Reload's edits, a binding error stopped at in
  Bindings, Unit Tests, IDE Internal Errors, a `.bad` file, Show Log (`MainWindow.StatusPlaces.cs`). A controller says
  through a `StatusSay`; the window's `Say` keeps a place when the same words come again (a failed build's, from Run).
- `src/Passenger.App/StatusBar.cs`: what the status bar says, its whole text its tooltip, and a `StatusPlace` a click on it
  goes to, held only while those words stand (a link then, `Button.statusMessage.place`): a load problem's row in
  Problems' Toolset, Environment, a build's problems in Build, Hot Reload's edits, a binding error stopped at in Bindings,
  Unit Tests, IDE Internal Errors, a `.bad` file, Show Log (`MainWindow.StatusPlaces.cs`). A controller says through a
  `StatusSay`; the window's `Say` keeps a place when the same words come again (a failed build's, from Run).
- `src/Passenger.App`: the shell. Explorer, editor tabs, files on disk. Each action is an `IdeAction` (id, title,
  group, menu places, `Run`, `WhyNot`, whether its keys work while the terminal has them) that its feature adds to the
  window's `ActionRegistry`: `MainWindow.Actions.cs` for the window's own, a partial's or controller's `Add…Actions`
  for the rest, and a `ToolWindow`'s `Action` for its show action (`IdeAction.Words` are other names Find Action finds
  it by, as Rider's older names). `AppActions.cs` holds the ids and each platform's
  keymap. Keys, the main menu (`MainMenu.Build`), Find Action, context menus and MCP's `ide_action` all run actions
  through the registry, so one that cannot run says why; editor actions go to the focused editor. An action that takes a
  while holds a `Working` (`ActionWork.Begin`, under its id or `IdeAction.Works`) from its first moment until its work is
  done or handed over, saying each stage (`Say`) and honouring `Stopping`: while it works the registry starts it no more
  ("Already under way: …"), the status bar says the stage beside a `WorkingRing`, Stop can end it, and a button bound
  with `ActionButtons.Bind` (every toolbar button, `WorkingActionsTests`) turns a ring round its icon. Run, Debug, Profile,
  every build, test runs, Git's push, update, fetch and commit (`VcsController.Committing`), a NuGet change
  (`NuGetView.Changing`), Attach to Process and Apply Hot Reload Changes do (#254); the begin is in the controller (the
  session's are lent the window's `ActionWork` through `SessionSetup.Work`), so an agent's call shows too. Stop keeps its toolbar place,
  greyed when nothing is under way: a button that comes and goes moves its neighbours from under the pointer.
  `Product` is what the build is (version, commit, runtime, the licence and notices beside it), the one place the
  version is read; `AboutDialog` is About (the application menu on macOS, Help elsewhere).
- `src/Passenger.App/Welcome`: the Welcome screen (`WelcomeView`, in the editor area while nothing is open) and the
  recent folders and solutions (`RecentSolutions`, the store's `recentSolutions.json`), read by the screen, File >
  Recent Solutions and a start that names nothing (Settings' Reopen projects on startup); `MainWindow.Welcome.cs`
  wires them and Clone Repository (`Vcs/CloneDialog`, `Git.CloneAsync`). `MainWindow.Open` is how File > Open (a
  solution or project), Open Folder, the Welcome screen and Recent Solutions open a workspace, refusing anything else
  with why (Avalonia's panel takes files or folders, never both). A .csproj opens on its own, as Rider opens one
  (`WorkspaceModel.ProjectFile`); what was opened is kept and reopened as itself (`WorkspaceModel.OpenedAs`), under
  the name that is also the window's title. `Installation.cs` is what an installed
  Windows copy does on install, update and uninstall (Velopack's hooks): the file types it opens, under the user.
- `src/Passenger.App/Mcp`: Passenger as an MCP server (the official C# SDK). `PassengerTools` is every tool, each
  run on the UI thread as the window's own commands are and ended by its `CancellationToken`, answering with what its
  operation returned (`VcsOutcome`, `CommitResult`, `BuildOutcome`, the debugger's `Stop`), never with what the status
  bar says by then; `PassengerTools.OnUi` answers known refusals with their message and sends any other exception to
  `IdeErrors`, answering "Passenger failed: Type: message", and a non-C# file's `diagnostics` waits only for that
  file's own checks (`OpenDocument.IsChecking`/`Checking()`, which a check's `Debounce` holds). The code tools serve
  each file through its own language (`PassengerTools.Languages.cs`, `Serve`): Roslyn for C# and Razor, `XamlDocument`
  for XAML of either dialect, a `WebDocument`'s server for the web languages (ESLint's fixes first, the project's
  Prettier for `format`), `MSBuildDocument` for project files; a command a language lacks is refused naming what it has
  (`Lacks`), and where a tool and a window command answer the same question they share one method in `CodeAssist`
  (`XamlPlaceAsync`, `XamlUsesAsync`, `XamlRenameAsync`, `JsonDeclarationAsync`, `PrettifyAsync`). `refactor` (`PassengerTools.Refactor.cs`) is the window's refactorings with their dialogs' options as one JSON
  object per kind: Change Signature, Move, Extract Interface, Pull Members Up and Push Members Down
  (`CodeAssist.ChangeSignatureAsync`, `MoveTypeToFileAsync`/`MoveTypeToNamespaceAsync`, `ExtractInterfaceAsync`,
  `MoveMembersAsync`, which their dialogs call too), Safe Delete (`FindDoomedAsync`/`Delete`, refused with its usages
  unless `deleteAnyway`), the server's Introduce Field, Introduce Parameter and Encapsulate Field, and Optimize Imports
  and Code Cleanup over a file, project or solution (files not open `EditorArea.Borrow`ed without a tab, one command);
  without options a kind with a dialog describes it (`DescribeAtAsync`), and each answers a `RefactorOutcome`'s files,
  one command of global undo, saved. `refactor`, `rename` and `apply_code_action` run in a `Patience` (`WorkspaceEdits.cs`):
  refused by `StaleAsync` for a file not open the server may not have read (the agent's own write a moment before), the
  call waits until `DocumentTexts` says the server holds the disk's text (else for the 2 s to pass) and works it out
  again, 10 s in all, a file so seen trusted until it is written again; the window's commands still refuse at once. `app_details` is the
  Live Visual Tree's details for an agent (value sources; styles with why each applies or not); `breakpoint_set` and
  `breakpoint_remove` take `control` and `property` for Break When Value Changes, waiting for
  `LiveTreeController.Watching`; `debug_state` carries `bindingValues`, and with several programs debugged lists them
  (`sessions`): the debug tools and `stop` take `session`, a Debug tab's name, else act on the one paused, else the only
  one, and are refused naming them when they cannot tell (`PassengerTools.TabFor`; `stop` naming none is Stop All);
  `hot_reload` is Apply Hot Reload Changes
  (`RestartForAgentAsync` never asks; under dotnet watch, `SaveForWatchAsync`'s last word; a container run with Hot Reload,
  `MainWindow.HotReloadRunning`, through `ApplyRunningAsync`); the headless `--mcp` IDE
  starts on the default settings, so `run` takes `hotReload` and `run_output` reads the Run console; `debug_start` on a
  container configuration debugs each service in a session of its own, answering with them (`sessions`); `McpTests.Actions`
  holds `ide_actions` to every `AppActions` id. `settings`/`settings_set` (`PassengerTools.Ide.cs`) read and change
  the settings by their settings.json names through the window's `ApplySettings`, as the dialog's OK does, a number's
  range found by asking `AppSettings.Clamped`, an unknown name or choice refused and Enable MCP Server never changed;
  `find_in_files`/`replace_in_files` are the window's own search in its scopes and masks once `FileIndex.IsWalking` is
  false; the `local_history` tools wait for `LocalHistory.IsRecording`, their revert `RevertLocalHistoryAsync`;
  `problems` is Problems' Toolset, Build and Solution tabs (after `CSharpService.IsAnalysing`), `build_output` the Build
  console, and `profile` takes `attach` (and `container`, a container's program). The tools reach the
  folder through the window's `ProjectSession` as each call finds it, and the
  window only for editors, the terminal, actions, opening and the picture; `McpHost` hosts them three ways: `--mcp [FOLDER]` (a headless IDE on standard input and output, whose window no other
  agent joins, its server answering before the IDE is up and each call waiting for it), a window's own socket in the state's socket folder (`McpHost.SocketFolder`: in the user's own temporary
  folder, macOS's by `confstr` whatever TMPDIR an agent has, else XDG_RUNTIME_DIR or /tmp, named by a hash of the user and
  the state folder, mode 0700 and used only once its mode is the user's to set, so a socket's path stays within macOS's
  104 bytes whatever PASSENGER_SETTINGS is, and `TestRun` removes the run's) (`AgentSocket`, open from the window's start but for
  `--no-mcp` and a screenshot without `--agent`), and `--mcp-attach [PID] [FOLDER]` (`McpProxy`: the agent's server for
  its whole session, answering initialize and tools/list from `McpHost.ToolMethods` and joining, at a tool call, only a
  window whose folder holds FOLDER or the agent's working folder; with none it asks the running Passenger to open it
  through the process's `PID.open` socket (`AppSocket`, `PassengerApp.OpenProject`), else starts Passenger (`open -a` on
  the app bundle on macOS, else `ChildProcesses.StartApart`) and waits for that folder's window). Each mode ends on
  SIGTERM as on the end of its input (`McpHost.EndOnSignal`, gone within `EndLimit` at the latest), since an agent's
  client may send it a moment after closing the input (OpenCode 1's, 20 ms after), and `--mcp-attach` ends the test
  run's headless window by closing its input, so it takes its socket away before the watchdog would kill it. Every call on a
  window's connection waits until the agent is let in (`AgentConnection.AdmitAsync`): Enable MCP Server, the folder among
  `AppSettings.AgentFolders` (what the question's Always for this folder adds, settings.json's, never `settings_set`'s),
  else the person's answer to `AgentAdmissionDialog`; turning the setting off, or
  the window's folder taken off the list, ends the connections (`AgentSocket.ShutOut`). An agent is let in on a folder:
  the window leaving it (`OpenFolder`, Close Solution) ends the connections of those let in on it (`AgentSocket.Moving`)
  unless agents are let in unasked where it goes; the agent whose own `open` moves it stays, and those that stay are asked
  again before each tool's first use there; `--mcp-attach` joins again by folder once its window has another folder open
  than the one it joined it on (its own `open` aside). On a person's
  window a tool neither `ReadOnly` nor `Destructive = false` asks them the first time an agent uses it (`ConnectedTool`,
  `AgentConnection.AllowAsync`: one question per tool, which every call of it waits on and the last to give up takes
  away), and `open` asks before it replaces their folder. What runs code in a debugged program or changes it
  (`debug_evaluate`, `debug_set_value`, `debug_input`) and `breakpoint_set` (whose condition runs in later sessions
  too) are `Destructive`; `save_all`, `stop` and the debugger's steps are not, the owner's choice (#278). No wait on
  the person is unbounded (#255): each question goes through `MainWindow.AskForAgentAsync`, which closes it unanswered
  after `MainWindow.AgentAnswerLimit` (2 minutes), the call then saying nobody answered and a later one asking again, brings the window forward (`Attention.Ask` bounces
  the Dock icon on macOS, where Activate is refused while another app is in front) and has the status bar say who waits
  on what (`AgentStatus`, "X waits for you: allow build?"). Every call is an `AgentCall` (`ConnectedTool`, `McpProxy`):
  when its request gave a progress token it tells the agent MCP progress every `AgentCall.Every` (10 s;
  `PASSENGER_MCP_PROGRESS_MS`, the tests' run 1 s) and at once as it starts waiting on the person, since OpenCode 1 gives
  up on a call it hears nothing of for 60 s and OpenCode 2 waits 12 hours; what it says is what it waits on, else the
  window's `ActionWork` stage it follows (`AgentCall.Follow`: build, run, debug_start, profile), else what the tool said
  (`AgentCall.Say`, reached through an AsyncLocal the dispatcher flows), with the time so far. `--mcp-attach` sends the
  window a progress token of its own and tells the agent the window's words under the agent's token and its own rising
  counts, beating itself only until the window speaks. With protocol traces on, `mcp.log` has each call's start, waits
  (with what they came to) and end with their times, names only (the proxy reads `traceProtocols` from settings.json);
  passenger.log has each question and its answer always. In the MCP modes (`--mcp`, `--mcp-attach`, `--headless`)
  `IdeLog.TakeConsole` writes Console.Out and Console.Error to passenger.log, and on macOS and Linux makes the
  process's own standard error a pipe a thread reads into it, since an agent may never read stderr (OpenCode's never does)
  and a full pipe stops its writer; Program.Main's fatal-flag message still goes to real stderr. Settings' MCP Server
  page (`SettingsDialog.Mcp.cs`) lists the
  folders, each taken back by Remove, and each agent's setup (`AgentSetup`: Claude Code's and Codex's commands, a
  repository's `.mcp.json`, Codex's config.toml, Claude Desktop's with the window's folder after `--mcp-attach`,
  OpenCode 1's opencode.json with a 10-minute timeout, which progress starts over, and OpenCode 2's `mcp.servers` with
  Code Mode off and a 30-minute execution timeout, which progress does not: checked against both by hand, #255) naming
  this executable by its real path, in a `CodeBlock` (`Border.code`) with Copy, and says when `--mcp FOLDER` (asks
  nothing) suits better than `--mcp-attach`. `McpHost.Instructions` tell an agent to
  begin with `workspace`, find its way by `find_symbol`/`outline` and `refactor`'s describe, and what a person's window
  asks; every answer is written by `McpHost.ToolCreateOptions` (relaxed escaping: code reads as written, its quotes and
  angle brackets never unicode escapes), and every tool's schema is `McpHost.Lean`'s (no null default or null type). An
  agent pays for the tool list every session (69 KB before #236's trim, 55 KB after), so it says each thing once: the
  element query in `app_tree`'s description, which the other app_ tools' `control` points at; the wait for an agent's
  own write in the instructions; lines and columns from 1 never per parameter. `edit` replaces text in one file as the editor does (found once as written,
  else refused saying where it is; one Undo step, saved, the changed lines said back); a tool naming an open file the
  editor has not changed takes the disk's text first (`PassengerTools.Document`), since an agent's own file tools write
  there and ask at once. `run`, `debug_start` and `hot_reload`'s restart return once an inspected app's UI is up
  (`AfterLaunch`, which watches 2 s more for a stop in its first layout); each run of a stop's frames outside the
  folder's code is one line of their modules (`Frames`); a run, debug, test run or profile refused for a failed build
  names its errors (`WithBuildErrors`); `build` gives MSBuild's output tail only when it failed; `tests_list` takes `filter`, `state` and `limit`; `find_symbol` puts the name asked
  for first. `nuget_install`/`nuget_remove` answer a `PackageChange`: the version installed and the files that changed
  (the project, the nearest Directory.Packages.props). `profile`'s hot spots in the folder's code carry `source`
  (path:line of the method's first sequence point: `Profiler/MethodSources` reads the module's PDB by the trace's
  module path and method token, `HotSpot.Code`; the line the editor's, through `ServerPlaces.Compiled`). A resume of a debugged test run (`debug_continue`, `debug_step`)
  whose host ends waits for the run and answers its tally as `tests_run` does (`DebugSnapshot.Tests`). Usability pass
  (#236): an agent's `--mcp-attach` with no window open starts the person's Passenger in a window they see, so a pass
  drives `--mcp FOLDER` (or `--mcp-attach` with `PASSENGER_START=headless` and a state of its own, as `TestRun` sets).
- Attach to Process: `MainWindow.Attach.cs` (`Debugging/AttachToProcessDialog`, Reattach, the recently attached);
  `Core/DotnetProcesses` lists the .NET programs a debugger can attach to (the runtime's `clr-debug-pipe-*` in TMPDIR
  on macOS and Linux, checked against the process's start; its diagnostic pipe on Windows), and `Under` those a process
  started, through each one's parent (on Windows only a parent that began first). An attached session is
  let go by Detach, by leaving its folder, by the window's close and by the adapter when the IDE goes without disconnecting
  (`ManagedDebugger.Abandon`), never ended by them. On macOS the debugger needs no entitlement and no
  get-task-allow on its target: the adapter runs under the machine's dotnet and talks to the runtime through its
  pipes, so the package carries no com.apple.security.cs.debugger. The dialog (`MainWindow.AttachableAsync`) also lists
  the running containers' programs under each container (`DotnetProcess.Container`, its `Id` the container's own):
  attaching one runs the adapter inside it (`MainWindow.InContainerAsync`: the tools put in, the program found by its
  command line there, an `AdapterPlace` of `docker exec -i CONTAINER …/passenger-tools dap … --remote`), Hot Reload
  offered when the tools found it started with DOTNET_MODIFIABLE_ASSEMBLIES=debug (`DotnetProcess.HotReload`); one whose
  runtime has DOTNET_EnableDiagnostics=0 says why and cannot be chosen (`WhyNot`), and a Release build (its
  DebuggableAttribute) or a container of another processor than the engine's says so first (`Note`, after its command
  line, and in the status bar once attached). Reattach finds them by container and command line; MCP's
  `debug_processes` names the container and `debug_attach` takes `container`.
- Return values and Smart Step Into: a step over sets a breakpoint after each call in its statement
  (`ICorDebugCode3.GetReturnValueLiveOffset`) and keeps each value by a strong handle (a value type boxed by an eval,
  after which the step's stepper is made anew, since an eval mid-step loses it); `$ReturnValue`/`$ReturnValueN` are
  expression-compiler aliases, as `$exception` is, typed by the program's own assemblies. Smart Step Into matches the
  statement's IL calls to its Roslyn syntax by name, in order, and steps over it with a breakpoint at the chosen
  method's first line; the editor's choice mode is `CodeEditor.Choices`.
- Async stepping (`AsyncStepper`): a step over an await ends its stepper at the await's yield point (which Roslyn puts
  before the awaiter is told to go on) and goes on from the resume breakpoint on whichever thread the run resumes, the
  return-value watches following it there. The run is told by its state machine object (a class in a Debug build, read
  with nothing run); only a struct state machine's is asked of its builder (`ObjectIdForDebugger`, an evaluation, which
  runs its thread alone and can wait for good on what a paused thread holds), and one not told takes the method's first
  resume. Nothing may leave `_stepper` set but inactive: `HandleBreakpoint` reads such a stepper as one whose
  StepComplete is queued, and lets a breakpoint on the stepping thread go (the step's stop stands for both), while
  another thread's waits behind the step's stop (`PutOffBehindStep`), handled as a hit once the program goes on. A
  StepComplete whose stepper is no longer `_stepper` (a stop ended its step after it had completed) is let go, and one
  that comes during an evaluation on another thread is put off as a breakpoint's is (`PutOffDuringEvaluation`).
- `src/Passenger.App/Debugging`: `BreakpointStore` holds line breakpoints by file and Rider's CLR exception
  breakpoints (`ExceptionBreakpoint`; "Any exception" is every folder's and is never removed), kept with the folder;
  `Muted` is Mute Breakpoints, which sends none while on. Several programs are debugged at once (#341): the controller
  holds a `DebugTab` per session (the Debug window's header tabs, `DebugView.Sessions`, each with its own console, built
  lines and Hot Reload), and `DebugController.Session` is the chosen tab's, which the window shows and every action and
  `Stop` act on; a stop in any program chooses its tab, and the chosen one's end chooses another still debugged (one
  paused first). Debug or Attach of a program already debugged (a tab's name: its configuration's, or the program's)
  stops it, or lets an attached one go, and takes its tab; with no other program debugged the new tab takes every
  ended tab's place and the first console, so one session is as it always was. Stop All (`AppActions.StopAll`) ends
  every one, and Stop does when the chosen has ended; the status bar says how many are debugged and which paused
  (`DebugController.Told`). Every breakpoint goes to each session, the store keeping what each one's debugger said
  (`Breakpoint.Tell`: bound where any bound it) until its session ends (`BreakpointStore.Leave`); a data breakpoint goes
  to the session whose object it watches alone. A session `Elsewhere` (a container's program) never holds, signals or
  kills a process of this machine by its program's id (`DebugSession.IsLocal`, which Attach Profiler asks too), its
  adapter started with `--remote` so its `process` events say `isLocalProcess: false`. A session given an
  `AdapterPlace` (`DebugController.StartAsync`/`AttachAsync`'s `place`) runs its adapter there, a container's over
  `docker exec -i` (the protocol unchanged over its standard input and output; killing the CLI, Stop and Detach close the
  adapter's input there, which lets the program go and ends it), and crosses paths at the protocol's edge through the
  place's `ContainerPaths` (launch's program and folder, breakpoints, Run to Cursor, Set Next Statement, Hot Reload's
  line updates and active statements, stack frames): a path the map does not cover, or `ContainerPaths.None` (a build of
  this machine's, fast mode), crosses as it is. The engine decides exception breakpoints (`ExceptionRule`)
  when the runtime reports a handler found or an exception unhandled, the stack still at the throw ("user code" is a
  Just My Code frame); they cross the protocol as DAP `exceptionOptions` with Passenger's own scenarios and conditions.
  An evaluation the runtime won't start where the thread is stopped (a pause in optimized or native code) is
  `EvalRefusedException`; a value shown by its ToString then shows its type. An evaluation the engine could not stop runs on with the program, its
  abort still pending: it fires wherever the thread then is (a rude abort runs no finally), so code evaluated at a stop
  takes no lock another thread may hold. `Extensions.EvaluationTimeout` (5 s, the
  adapter's `PASSENGER_EVAL_TIMEOUT_MS`) also sets the abort's wait (0.4×) and the rude abort's (1×). Data breakpoints (Variables' Set Data
  Breakpoint, `ManagedDebugger_DataBreakpoints`, `DebugRegisters`): the runtime watches memory only on Windows x64
  (`FEATURE_DATABREAKPOINT`), so elsewhere the item is greyed with the reason; the engine pins the object
  (`ICorDebugHeapValue4`, the runtime's advice over GC notifications) and sets DR0–DR7 on every thread through both the
  runtime's context and Win32. They last one session, as Rider's do, and their window tests run only on Windows x64.
  XAML breakpoints: XamlIl (through Cecil) writes a sequence point per .axaml element and attribute, in documents of
  no language, mid-expression, where the JIT maps few; for such a document `ModuleMetadataReader.ResolveBreakpoint`
  binds at `JitBoundaryAtOrBefore` (after a call, a nop, the method's start), an attribute with no point of its own at
  the one after the attribute before it (`UnpointedAttribute`), a template's root in its builder, and the stop is shown
  at the breakpoint's place. `LineBreakpoint.Attribute` is an attribute's breakpoint (F9 on it, `XamlBreakpoints`),
  kept with the folder, sent with its name's column, line and column both as XamlIl counted them (an XML reader's,
  `BuiltLines`). `PropertyBreakpoint` is the Live Visual Tree's Break When Value
  Changes, for one session: the inspector watches that element's property (protocol v3 `watch`, `PropertyBreaks.cs`)
  and calls `Breaks.Changed`, on which the IDE sets one function breakpoint per property breakpoint (condition
  `breakpoint == Id && (…)`, the hit count kept in the app). The inspector is never optimized (the debugger reads that
  call's arguments) and its watching code is `[DebuggerNonUserCode]`, shown as external code under Just My Code, so
  the stop selects the code that set the value. `BindingErrorBreakpoint` is XAML Binding Errors ("Any binding
  error", off until turned on, never removed, kept with the folder): under a debugger the inspector's binding sink calls
  `Breaks.BindingFailed(BindingError)` (`Breaks.cs`, with `Breaks.Changed`) on each failure, repeats too, on the thread
  that evaluated it, where the IDE sets one function breakpoint (`Wire.Failed`), sent with the property breakpoints' in
  one `setFunctionBreakpoints` (the engine keeps each asked for again as it was whole, its hit count included, as it keeps
  a line's); its condition and log message say `$bindingError` (rewritten to the parameter). The
  stop reads `bindingError.Said` (a `BindingFailure` as JSON) in frame 0, selects the first user frame, says the failure
  in the status bar, annotates the line, lists `$bindingError` in Variables and writes a linked console line. A function
  breakpoint's binding is told apart by its module, not its base address: the inspector is two modules over one image.
  The engine's lock (`EngineLock`) is held by one request or runtime event at a time and is not re-entrant: code under
  it never takes it again (`DrainRuntimeEventQueue` runs under its caller's hold). A read of the stop (threads, frames,
  scopes, variables, evaluate, exceptionInfo: `DebugAdapter.Read`, `ReadLockAsync`) waits behind every other holder, so
  continue, the steps, pause, disconnect and terminate never queue behind a stop's reads; one that leaves the stop
  (`DebugAdapter.Leave`: continue, the steps, disconnect, terminate) is in line before it calls `ManagedDebugger.Leaving`,
  and the read under way then starts no evaluation and leaves the one it runs to the program (`Abandoned`, aborted only
  once its time is up, `AbortWhenDueAsync`), since `ICorDebugEval::Abort` answers only after half a second, the program
  running or stopped, every other call into the runtime waiting meanwhile. The window shows the program running as a
  continue or step is asked (`DebugSession.Resume`; refused, the stop is shown again), asks nothing more of a stop once
  it is gone, and writes each stop's time to its frames and locals to passenger.log (`DebugController.LastStopTimes`).
- The debugger, Passenger's own, started from SharpDbg and ICorDebugSharp (MIT; `THIRD_PARTY_NOTICES.md` says what came from where):
  `src/Passenger.Debugger.Interop` (ICorDebug, IMetaDataImport and dbgshim for the COM source generator),
  `src/Passenger.Debugger` (the engine), `src/Passenger.Dap` (the Debug Adapter Protocol, both ends) and
  `src/Passenger.DebugAdapter` (the engine behind the protocol, its own process, carried under `debugger/`; its flags
  and loop are `AdapterProgram`). `src/Passenger.ContainerTools` is `passenger-tools`, what runs inside a Linux
  container: `dap` (`AdapterProgram` and `DebugAdapter.cs` compiled in, the one engine), `processes` (the container's
  .NET programs as JSON, `DotnetProcesses.List` plus those mapping libcoreclr.so with no debugger pipe, each with
  whether it was started for Hot Reload, built optimized, and why it cannot be attached to), the profiler's `trace
  --pid PID [--resume]`, `heap --pid PID` and `sources` (`Passenger.Profiler`'s `InContainer`, ClrMD and its Azure
  packages carried; `ContainerTools.ToolsCommand`) and `version`. Self-contained and InvariantGlobalization, never
  carried by the app (the release's `Passenger-container-tools-<rid>.tar.gz`, or `just container-tools`); a musl
  publish keeps its own libdbgshim.so
  (`KeepMuslDbgShim`: the glibc package's matches a musl system too, and won). The engine's `SourcePaths.Match` takes a
  PDB document that exists where the adapter runs while the breakpoint's file does not (the IDE's, outside the
  container) by what they share from the end, and compares a Windows path (C:\…, \\server\…) ignoring case on any
  system; the engine gives a Windows document's path back with forward slashes. Before
  letting a program go (Disconnect without terminating), the engine runs it off any patch or single-step stop and stops
  it again (`ManagedDebugger_LetGo.cs`): under CET a thread resumes from a breakpoint only through the debugger's
  out-of-process SetThreadContext, which mscordbi's Detach waits for only once it has answered one such flare, so a
  first stop after an attach, detached at, would fault (0xC0000409). Symbols for assemblies without their own are
  decompiled into the folder the adapter is given (`--symbolCache`; the IDE's `DebugSession.SymbolCache`, the state
  folder's `cache/symbols`) by `Decompilation/SymbolCache`: `NAME/MVID/NAME.<decompiler>-<Format>.pdb` (raise
  `SymbolCache.Format` when `PortablePdbWriter2`'s output moves), written through a temporary file and renamed, one that
  does not load deleted and made again once, pruned as the adapter starts (30 days unused, 512 MB, never one used in the
  last day). A decompile never runs under `EngineLock`: resolveStackFrame takes `SymbolsWantedAt` under it,
  `DecompileAsync` off it, then the frame under it again; without Just My Code a stop in such an assembly waits as
  `AfterDecompiling`, dropped if the program was let go. A [DebuggerDisplay] fragment of dotted names that doesn't
  evaluate is read as the value's property by that metadata name (Roslyn's `{System.Collections.ICollection.Count}`).
  configurationDone's answer comes before any of the program's events: the adapter launches or attaches under the
  engine's lock and keeps it until the answer is written (its `then`, or `AbortAsync` when the client went), and
  `DebugSession.StartAsync` leaves a stop told before that answer standing, as `Resume` does. A launch or attach waits
  for the runtime's handshake as long as the program lives and the session lasts (a runtime's first start on a fresh
  runner reads it off a cold disk); disconnect, terminate and the client's going call `ManagedDebugger.Ending()` before
  asking for the engine's lock, and a failed configurationDone's answer is written under the lock too
  (`DapConnection.Handle`'s `evenIfFailed`). A debug session is `DebugController.Session` from its first moment
  (`DebugSession.LaunchAsync`/`AttachAsync`'s `starting`), Starting until configurationDone answers: Stop then
  disconnects at once (a launched program ends, one being attached to is let go), the start failing as "Debugging NAME
  was stopped as it started", and a start a disconnect was asked of sends no configurationDone. A breakpoint changed
  while it starts goes once configurationDone has answered, with whatever else differs from what was sent
  (`DebugSession.SendChangedAsync`). Profile's Kill shows
  while its program has yet to reach the profiler and cancels the start.
  Hot Reload while debugging (Edit and Continue): `Passenger.Roslyn.Extension/HotReload.cs` drives the server's own
  `EditAndContinueService` (its session starting at the debugging session's first C# edit, given each file's pre-edit
  text, checked against the build PDB's checksums), the runtime's capabilities read from the program's CoreLib; the
  deltas reach the engine as Passenger's `applyChanges` request (`Passenger.Dap`, with `hotReloadState`), and
  `ManagedDebugger_HotReload.cs` applies them (`ICorDebugModule2.ApplyChanges`, which needs `GetMetaDataInterface` asked
  first) and remaps frames (`FunctionRemapOpportunity`/`RemapFunction`, a stepper deactivated before the remap).
  `ModuleMetadataReader_EnC.cs` keeps each update as a generation: read a method's sequence points, locals and IL only
  through `Points`, `DebugOf` and `MethodIL` at the frame's EnC version (`VersionOf`), never the PDB's directly; after
  an update `_peMetadataReader` is mscordbi's saved copy of the module's metadata, `_imageMetadataReader` the image's.
  A Debug launch sets `DOTNET_MODIFIABLE_ASSEMBLIES=debug`. `Debugging/HotReload.cs` and `MainWindow.HotReload.cs` are
  Apply Hot Reload Changes (Alt+F10, toolbar and Debug/Run windows' flame, edits made while paused applied on the next
  resume or step), Problems' Hot Reload tab for rude edits, and the restart (Settings' Hot Reload page: always restart
  for a change it can't apply, on by default, else dotnet watch's question); an attach gets it only when its caller
  started the program held for the debugger with DOTNET_MODIFIABLE_ASSEMBLIES=debug (`AttachAsync`'s `hotReload`,
  Passenger's `AttachArguments.HotReload`), and the restart only as a container run's service (Containers above; a
  container run's restart is never asked, Settings' always restart deciding). Roslyn
  applies a changed signature as a delete plus an add and recompiles only members whose code changed, so `StaleCallers`
  blocks an update that leaves a loaded module's unchanged code calling the deleted body (ENC0033 at the new
  declaration); under dotnet watch, `SaveForWatchAsync` follows dotnet watch's words 10 s (`PASSENGER_WATCH_LATER_MS`) past its first, the last
  standing. MCP's nuget_install and nuget_remove have open project files take the disk's text before answering
  (`MainWindow.FollowWritten`).
- `src/Passenger.Inspector`: the Live Visual Tree's helper inside an Avalonia app being debugged (Passenger's own, MIT,
  net8.0, carried under `inspector/`, never put in the app's build): a startup hook `ProjectSession.Inspected` gives only
  a Debug launch whose build has Avalonia.Base.dll beside it (`DOTNET_STARTUP_HOOKS`, which it takes back out of the
  environment). It compiles against Avalonia 12's implementation assemblies (the reference ones leave out [PrivateApi]
  value-store diagnostics) and runs in a load context of its own that takes the app's Avalonia whatever its 12.x; it
  never touches `Dispatcher.UIThread` before the app makes it, and answers on the app's UI thread over a Unix socket in
  the temp folder (macOS allows 104 characters). `Protocol.cs` is compiled into both ends, deeper than System.Text.Json's
  default 64. Its wire's types are made ready as it connects, before the app's Main
  (`Connection.Configure`): System.Text.Json readies a type on first use under one lock for all of a context's types,
  which a thread the debugger stopped can hold from an evaluation at the stop, whose abort, still pending once the app
  runs on, then fires inside the lock and keeps it for good. `App/LiveTree/LiveApp` is Passenger's end (the session's, ended with its debugging); `LiveTreeController`
  and `LiveTreeView` are the window, which asks nothing while the app is paused. Why a style fails is read from its
  selector's text (`Selectors.cs`); whether it applies is always Avalonia's answer. Agents' tools (`app_*`,
  `Mcp/PassengerTools.App.cs`) ask the same inspector: its windows, the tree with each element's box, text and state,
  `capture` (`Pictures.cs`: the window rendered by the app at a scale, its popups drawn over it) and `input`
  (`Driver.cs`: raw input into the window's `ITopLevelImpl.Input` with a mouse device of its own, so the window is never
  activated and the real pointer never moves, waiting for a rendered frame before and after, since hit testing reads
  the last one). `LiveTree/AppQuery` is how an agent names elements (`@id`, type, #name, .class, "text", steps by /,
  [n]); `LiveApp.Own()` loads the inspector into Passenger itself for `app: ide`, once per process, whose input never
  reaches a question Passenger asks the person for an agent nor Settings (`MainWindow.KeptFromAgents`, given to the
  inspector's `Agent.Guard`, weighed at each event the driver sends); a headless test gets a dispatcher of its own, so
  one test a run inspects Passenger in the test process (`McpTests.AvaloniaApp`'s person's window). Binding values
  (`BindingValues.cs`, protocol v4 `values`): each element by its XamlSourceInfo place (a root by x:Class), of several
  instances the one nearest the element chosen in the Live Visual Tree, each binding's path walked from Avalonia's
  binding expression nodes. Paused, the IDE evaluates `Wire.ReadValues` on the UI thread (the stop's, else the main
  thread; the startup hook's copy forwards to the inspector's own), the answer an array whose slots the debugger shows
  as Variables does; running, only for the control chosen, read again on `valuesChanged`.
  `App/LiveTree/BindingValuesController` shows them after the lines (`OpenDocument.LiveValues`,
  `LineAnnotationKind.Failed`) and on hover. The runtime evaluates nothing on a UI thread waiting outside .NET, so a
  stop on another thread shows none, saying so. The startup hook turns on Avalonia's
  `Avalonia.Diagnostics.Diagnostic.IsEnabled` switch, so Avalonia makes its `Avalonia.Diagnostic.Source` Measure and
  Arrange activities (tagged `Control`) and `Avalonia.Diagnostic.Meter` histograms; `FrameRecorder` (`Frames.cs`)
  listens only while a session enables Passenger's EventSource `Passenger-Avalonia`, so they land in the .nettrace; a
  session's end lets go, on the UI thread, of the layout it had under way, whose own end it no longer hears. Protocol
  v5 `overlays` sets each window's `RendererDiagnostics.DebugOverlays` (the Run and Debug windows' toggle,
  `LiveTree/OverlaysCommands`, `ToolWindows/OverlaysButton`, Run > Avalonia Debug Overlays; the last choice is the next
  launch's `PASSENGER_INSPECTOR_OVERLAYS`). Profile launches get the inspector too (`ProjectSession.InspectedProfile`).
  `src/Passenger.Inspector.Wpf` is the WPF counterpart (MIT, net8.0-windows, built everywhere with EnableWindowsTargeting,
  carried under `inspector/wpf/` only by a Windows build or win-* publish): `LiveApp.For` gives it (`LiveAppKind.Wpf`) to
  a Run or Debug launch on Windows whose runtimeconfig names Microsoft.WindowsDesktop.App and whose assembly references
  PresentationFramework, with `ENABLE_XAML_DIAGNOSTICS_SOURCE_INFO=1`. It links Protocol.cs and Connection.cs (the same
  socket, protocol v5, `Hello.Framework`), runs in the default load context, and retries a main-thread dispatcher WPF is
  still making. Value sources are `DependencyPropertyHelper.GetValueSource`; places `VisualDiagnostics.GetXamlSourceInfo`
  turned into files through the assembly's PDB; binding errors come from `PresentationTraceSources` and
  `BindingDiagnostics.BindingFailed`, and `Breaks.BindingFailed`/`Changed` keep their names so one function breakpoint
  serves both frameworks (XAML Binding Errors; a WPF stop has no user frame, so the editor shows the XAML). Its
  `Driver.cs` clicks through UI Automation patterns in-process and sends WPF's own key, text and wheel events, never
  activating a window. `PASSENGER_INSPECTOR_TRACE=FILE` traces its start and answers. A stop whose ids came in an unanswered setFunctionBreakpoints waits for
  that answer (`DebugSession.TakeStopAfter`), so a property or binding-error stop is known as one.
  The inspector also takes Avalonia's logger before the app's Main (`Bindings.cs`, put back over any sink the app sets,
  which is still fed) and tells of each failed binding (`bindingFailed`: control, property, path, message, XAML place,
  and for a member the source's type lacks its nearest members) once, then its count at most every 250 ms. Run gives an
  Avalonia app the inspector too (`ProjectSession.InspectedRun`, `RunApp`; none under Hot Reload, whose dotnet watch
  would load it); the Live Visual Tree follows only the debugger's `LiveApp`. `App/LiveTree/BindingErrors` is how a
  failed binding shows: a console line with its place first in a compiler message's shape (so `ConsoleLinks` links it),
  Problems' Bindings tab (`BindingProblem`, by file, each once with its count), and fixes through
  `CodeAssist.EditorFixes`, Passenger's own fixes that Show Context Actions lists before the server's.
- `tools/Passenger.Notices`: writes the app's `THIRD-PARTY-NOTICES.txt` (beside `LICENSE.txt` in every build and
  publish, through `WriteNotices` in `Passenger.App.csproj`) from the packages the build copies, the self-contained
  runtime pack, the debugger's and Roslyn's server's `.deps.json`, each package's licence and copyright, and the part
  of THIRD_PARTY_NOTICES.md after its marker line; a licence it cannot write fails the build.
- `src/Passenger.App/MSBuild`: code help in project, .props and .targets files. `MSBuildService` is the session's
  (`ProjectSession.MSBuild`), one `MSBuildDocument` per open MSBuild file, evaluated through its project (itself, the
  workspace project whose evaluation imports it, a Directory.* file's nearest project, or the one Go to Declaration
  came from) once typing pauses, on the text as typed. Completion reads the text as typed (`Passenger.Xaml`'s XmlTree)
  with `MSBuildSchema` (dotnet/msbuild's Core and CommonTypes XSDs, embedded because the SDK lacks them) and the last
  evaluation that succeeded; package ids, versions and advisories come from the window's NuGet (`NuGetView.FeedAsync`,
  `VulnerabilitiesAsync`), and under CPM a new PackageReference takes no version while Directory.Packages.props gets
  its PackageVersion, as one command. Installed MSBuild files (the SDK's, packages') open read-only
  (`MSBuildFiles.IsInstalled`). `CodeAssist.MSBuild.cs` connects it to completion, the hover card and Go to Declaration.
- `MainWindow.TitleBar.cs` and `TitleBars`: the toolbar is the main window's title bar, as Rider's header is: always
  on Windows and macOS, on Linux by Settings' Merge main menu with window title (on at first, as checked on a real GNOME
  desktop; greyed under WSL, tiling window managers or an unnamed desktop). Windows
  learns the bar's parts from Avalonia 12's `WindowDecorationProperties.ElementRole` (TitleBar on the toolbar, User on
  what is on it, Minimize/Maximize/CloseButton on its window buttons), so Windows itself moves, snaps and maximizes the
  window and shows snap layouts: anything new on the bar goes inside `ToolbarStart` or `ToolbarEnd`, or Windows takes a
  press on it as the caption's. Linux and macOS move the window through `OnToolbarPressed` (macOS's double click as
  the system's setting says). The layout follows `TitleBars`, not `IsExtendedIntoWindowDecorations`, so headless
  pictures show the merged bar; Avalonia's own drawn decorations get the empty `TitleBarDecorations` theme. Only the
  main window merges.
- `tools/package-mac.sh` and `tools/dmg-layout.py`: the disk image's Finder window is a .DS_Store written as Finder
  writes one, with no AppleScript: the icons' places and the background (`.background/background.tiff`) named by a
  Carbon alias made on the image's own read-write mount, since it carries the volume's creation date and the picture's
  catalog IDs, which the compressed image keeps; `dmg-layout.py check` reads it all back on the mounted image. The
  picture stays light in either appearance, as Finder draws the names black over a light picture in Dark too.
- `tools/linux`: what the Linux archive carries beside the app: the launcher, install.sh (per user, `--system`,
  `--uninstall`), the desktop entry and the file types it opens. CI runs the suite on Ubuntu 24.04 too, and installs,
  uses and uninstalls the archive. ClrMD's DAC stays loaded once used (`HeapGraph.Runtime`): unloading it crashed the
  IDE on Linux. Linux's file systems tell letter case apart, so a test of another letter case skips there.
- `site/`: passenger-ide.net, plain HTML and CSS, deployed to Cloudflare Pages by `.github/workflows/site.yml` when run by hand
  (`gh workflow run Site --ref main`). Its downloads are the latest GitHub release's assets, by fixed names (#56); its pictures are
  `just site-shots`' (each scene in both themes, a `<picture>` choosing by `prefers-color-scheme`, as the README's do);
  its share card, `images/social-<hash>.png`, is drawn from `tools/social-card.html` (the command is in its first comment)
  and named by its content, since Cloudflare and the sites that embed it keep `/images/*` by URL for a week (`_headers`):
  a changed image takes a new name, never the old one's
  after the pictures.
- `.github/workflows`: `test.yml` is every test on macOS, Windows and Linux, each system's suite in four shards run at
  once (jobs named as "macOS (arm64), shard 1 of 4"), then a summary job per system named as the system ("macOS
  (arm64)", the check that stands for it). Every workflow runs only when started by hand (the owner's
  choice, to spend Actions' minutes on purpose): `ci.yml` (`gh workflow run CI --ref main`, with the packages' and
  notices' jobs) calls it, and so does `release.yml`, run from a `vX.Y.Z` tag with dry run unticked: the tests, then the Apple Silicon disk image (no
  Intel Mac build, the owner's call), both Windows installers, both Linux archives and the four container tools' archives at the tag's version, `SHA256SUMS` and Velopack's feeds, published as the release under the fixed
  names the site and the updater link to. Its Run workflow with dry run builds it all and publishes nothing. ci.yml
  makes one disk image, so it and the four macOS shards fit GitHub's five macOS runners at once.

## Rules

- **Never use Microsoft's debugger (vsdbg) or anything from C# Dev Kit**, including Dev Kit's pieces that ship inside
  VS Code's C# extension (`.debugger`, `.roslynDevKit`, `.testDiscovery`). Their licences limit them to Microsoft's
  own editors. Nothing is copied out of an installed extension's folder; `roslyn-language-server` comes from NuGet.
  Nor Microsoft's `VSCodeDebugProtocol` package, whose terms are not open source: `Passenger.Dap` is the protocol.
- **No Avalonia Accelerate components** (TreeDataGrid and the rest need a paid licence).
- **Every program Passenger starts goes through `ChildProcesses`** (`Command` or `DotnetCommand`, then `Start` or
  `RunAsync`; `Kill` ends it with all it started), so none outlives the IDE. Only the terminal's shell (Porta.Pty's,
  ended by its pseudo-terminal's hangup), vstest.console (VSTest's, which watches the IDE) and, on Windows, Velopack's
  Update.exe (started as Passenger closes with an update ready, since it must outlive Passenger to replace it), and
  the person's Passenger that an agent's `--mcp-attach` starts outside an app bundle (`ChildProcesses.StartApart`, a
  session of its own that outlives the agent; in a bundle on macOS through LaunchServices' `open`, through ChildProcesses)
  start elsewhere. macOS's update runs nothing after Passenger: it swaps the app in as the process ends and starts the new
  one through LaunchServices (`open`, through ChildProcesses). A program a run configuration runs in the terminal is
  started by Porta.Pty and then `ChildProcesses.Adopt`ed: its process group goes on Lifeline's watchdog (macOS, Linux),
  and Porta.Pty's job ends it on Windows. Docker Desktop, which the status bar's link starts, is the person's: `open -a`
  (through ChildProcesses) on macOS, `StartApart` on Windows.
- **Containers are ended by label**, since no child holds one and Lifeline cannot end it: everything Passenger has an
  engine make carries `ContainerLabels.Arguments` (`net.passenger-ide.state`, a hash of the state folder,
  `net.passenger-ide.machine`, a hash of the machine's name and id, `net.passenger-ide.process`, the process's id and start
  time, and the folder's), its folder's `ProjectSession.Containers` removes what it started as the folder ends (of a
  Compose project only the containers and networks this process's labels are on, never `down`, which would take the
  person's own of it), the watchdog's last command (`ChildProcesses.AtIdeEnd`) removes the process's own on macOS and
  Linux however it ends, reaching the engine the IDE's commands reach (the CLI's DOCKER_* variables as the IDE has them,
  the context by name, told again when it changes), and the next start (`ContainerLabels.RemoveLeftoversAsync`) removes
  those of its state and machine whose process is gone, once the state has labelled anything
  (`ContainerLabels.EverLabelled`): another machine's on a shared engine are never judged by this one's processes. Tests
  label what they make with the run's `Docker.RunLabel` (its machine and process), removed at its end, and the first test
  that uses the engine removes what a run of this machine whose process is gone left: its label's objects and what its
  IDE made under its state (`Docker.RemoveDeadRuns`).
- **Every dependency, icon or asset** has an open-source licence and an entry in `THIRD_PARTY_NOTICES.md`, added in the
  same change that brings it in: a package by its NuGet id in backticks, with its licence and copyright as the package
  states them (`just notices`, and CI, fail on one missing), copied source by its file with the project it came from.
  The one exception, decided by the owner: `Microsoft.VisualStudio.Telemetry` and
  `Microsoft.VisualStudio.Utilities.Internal`, under Visual Studio's terms, stay inside Roslyn's server (MIT on nuget.org)
  because its built-in Razor support needs them; THIRD_PARTY_NOTICES.md says so. Nothing else under such terms ships.
  The second, also the owner's: the TextMate bundle licence ("Permission to copy, use, modify, sell and distribute this
  software is granted", no SPDX identifier), which the HTML grammar carries and the XML, Markdown, CSS and JavaScript
  grammars name as their origin, as VS Code ships them. A new SPDX licence needs its text in
  `tools/Passenger.Notices/Licenses`, a package inside Roslyn's server a row in THIRD_PARTY_NOTICES.md's roslyn/ table,
  and copied source or an asset its entry after that file's marker line, so the app carries them.
- **Colours are tokens.** A control names a brush from `Theme/Tokens.axaml` (`Passenger.*`), never a literal colour,
  and every token exists in both the Dark and Light dictionaries. A theme (`Tokens.axaml`, `Harbour.axaml`, and each
  `Look` tools/themes.py writes to `Theme/<Look>.axaml`, which `Themes.Apply` lays over Passenger's) is
  `Passenger.*` tokens alone, a new token given in every one (`just themes` fails until its script gives it); Fluent's own keys are named once for every theme in `Theme/FluentKeys.axaml`, which
  `FluentKeys` (App.axaml's resources) answers with that token as the look in force gives it. A Fluent key a
  Passenger control can reach goes there, never in a theme. `UiRulesTests` fails on an unmapped Fluent colour, brush
  or corner (an Avalonia update adds them) and on a colour or non-zero `CornerRadius` written outside `Theme/` unless
  it's the content's own (a colour in the XAML or CSS being edited, the terminal program's colours); the editor's
  palette has no colours of its own.
- **A malformed flag or value is fatal**, never a silent default (`LaunchOptions.cs`).
- **Work off the UI thread that changes what is drawn goes through `BackgroundWork`**, so a
  screenshot waits for it instead of capturing a half-finished frame: `BackgroundWork.Run` rather than a bare
  `Task.Run` (one that must not hold screenshots says why in a comment), and `Debounce` rather than a
  `DispatcherTimer` for a pause before such work, since a headless timer never fires while a screenshot settles.
- **One threading rule.** Controls, the documents editors show, the window's `ProjectSession` and its parts,
  controllers and views belong to the UI thread
  (controllers call `UiThread.Check()` in their entry points, which fails at once in a Debug build). The projects
  without UI (Text, Lsp, Dap, Debugger, Profiler) never reference Avalonia, and their events and callbacks come on
  any thread; Editor, Languages and Terminal raise theirs on the UI thread. The App crosses from another thread only through `UiThread`: `Post`, or `Marshal` for a handler given to
  a library, holds `BackgroundWork` and reports an exception to `IdeErrors` rather than ending the IDE; `InvokeAsync`
  is for a caller that waits for the answer. An `await` on the UI thread comes back to it; `Dispatcher.UIThread.Post`
  only defers work already there.
- **Paths are canonical** (`PathNames.Canonical`, symlinks resolved), because MSBuild and Roslyn
  report resolved paths; a path compared with another must come from the same function.
- **Shortcuts follow Rider's.** A new action takes the binding Rider's Visual Studio (Windows) or
  Visual Studio OSX (macOS) keymap gives it; Rider's keymap files are in its jars for reference.
- Commit messages go through a file (`git commit -F msg.txt`): backticks in a `-m` string are zsh command
  substitution.

## Tests

Tests drive the IDE whole, headless (`tests/Passenger.Tests`, xunit v3 on Avalonia's headless
platform with real Skia text): a window or an editor, real key presses by Rider's macOS keymap
(`EditorRig`), typing, clicks, files on disk. There are no unit tests of one function or type on its
own; a test says what a person would see or get ("an edit saved keeps the file's line endings",
"typing never loses a word's colour"), and its failure message names the consequence. The long
random editing session checks every key against a plain string model, which is what keeps the rope,
the line index and undo honest; keep it passing rather than narrowing it; it also runs with soft wrap on, moves checked against what a move among rows
must do and rows against a fresh editor's. Headless, a window draws each frame into a new WriteableBitmap whose Skia memory
is GC memory pressure (Avalonia.Headless's `HeadlessWindowImpl.Lock`), so a test that presses thousands of keys draws
a small window (the sessions are 300 high) and checks text without copying it (`EditorRig.DifferenceFrom` reads the rope
piece by piece). Performance is held by what the UI thread allocates, compared between a
small case and a large one, rather than by a time where a time is noisy; to see where the UI thread's time goes,
attach Passenger's own profiler to the test process (`ProfilingSession.AttachAsync(Environment.ProcessId, …)`). One test of something a person sees or
gets is worth more than many that restate the code they call: a test that only checks one type or function on its own
is deleted, and what it covered tested through the window instead. `BannedSymbols.txt` makes that a build error
(RS0030) for the internals tests used to drive directly (the terminal's emulator, a Roslyn or profiling session started
by hand, a lone `TextDocument`, `LineDiff`, `CommitGraph`, `Snippet.Parse`); the editor rig in `Rig.cs` is the one
exception.

A test waits for what it checks, however slow the machine (#237). `Ui.Settle`, and every wait built on it (`Ui.Press`,
`Ui.Landed`, `Ui.WaitFor`), waits until the IDE is quiet: `BackgroundWork` landed, nothing in `Passenger.Core.InFlight`
(each language server request unanswered, all but Roslyn's `workspace/diagnostic` pull, which it holds open until the
next change), every dispatcher timer come due fired and the last layout drawn (`Ui.Draw`; hit tests read the last frame
drawn, so a test reads bounds or clicks right after a wait). A program `ProcessRunner` ran that has ended holds
`BackgroundWork` until its console has its last output and exit code; the folder watcher's gathering and delivery,
Local History's queue and a console's batches were background work already. Left out by design: a program still
running and its output, the state store's half-second write delay (a test that reads a state file flushes it), the
templates' read ahead, and the system's own delay before the watcher hears a change (a test that needs it heard waits
for the watcher's `Changed`). A wait that gives up lists each hold by the file and line that took it (`InFlight.Watched`,
set by `TestRun`, names `BackgroundWork`'s holds, `UiThread`'s crossings and each `Debounce` by caller, at no cost
outside tests) and each piece in flight, with since when (`Ui.Busy`). No fixed sleeps: `Thread.Sleep`, `Task.Delay`
and `Screenshot.Pump` are banned in the tests (RS0030) outside `Waiting` and `Ui`. A wait for something is `Ui.WaitFor`,
`Ui.PumpUntil`/`Ui.Pumped` (the window's loop run, not settled) or `Waiting.Until`/`UntilAsync`/`Within` (off it);
time a test needs to pass (a debounce it measures, a moment in which nothing may happen) is `Ui.Hold(time, why)`,
`Ui.Stall` (the window's thread blocked) or `Waiting.For`. Every wall-clock limit is written as the time on a
developer's machine and multiplied by `TestRun.TimeScale` (`PASSENGER_TEST_TIME_SCALE`, 1 when unset, a value that is no
number above 0 failing every test with why, from the run's fixture; `Limits.Scaled`): Ui's and Waiting's waits scale what they are given, and the settle,
folder, process, restore, git, debug adapter and MCP call limits go through it. CI sets it per system in `test.yml`
from how much slower its runners are than the Mac (2 on macOS, 2.5 on Windows, 2 on Linux: the 90th percentile of a test's
time there over its time on the Mac, to the nearest half). `FLAKES.md` is the ledger of CI failures and their
causes, each a race in the IDE or a test that waited wrong, so a pattern shows.

A run keeps to a folder of its own (`TestRun`, set up by a module initializer before anything starts): the IDE's state
(`PASSENGER_SETTINGS`: its log, the language servers' logs, profiler snapshots; agents' sockets in the folder named
for it), every test's
folders (`TestFolders.Create`) and the shells' history, with an empty Git configuration in place of the person's
(`GIT_CONFIG_GLOBAL`, `GIT_CONFIG_NOSYSTEM`); on macOS and Linux NuGet's home is the run's too, its package cache named
as NuGet names the default, ending in a separator, so a restore of the repository's own projects (SelfDebuggingTests')
writes the nuget.g.props the person's build did and compiles nothing again. The run also shortens waits tests hold the IDE to: `PASSENGER_EVAL_TIMEOUT_MS` (3 s for
the debugger's 5 s; 1 s timed out a cold LINQ evaluation on CI's Linux), `PASSENGER_WATCH_LATER_MS` (3 s for 10) and
`PASSENGER_MCP_PROGRESS_MS` (an agent's call told its progress every 1 s, not 10); a test that holds a language server to not responding
sets that server's `SlowRequest`/`ResponseLimit`, never the run's, since a check Roslyn answers late under load would end
a server that was only slow. Nothing changes those variables afterwards; a test that needs fresh
state gives the window a folder of its own (`MainWindow.StateFolder`). So a run never touches the person's settings,
log, history or Git signing and hooks. Nor does a test start the person's Passenger: the run sets `PASSENGER_START=headless`, so an agent's
`--mcp-attach` with no window to join starts this build with `--headless` (a person's window on the headless platform,
ended with its input) as the proxy's child, in the proxy's own PASSENGER_SETTINGS (`McpTests.Attach.cs`). No test
reaches a registry through a Dockerfile it opens: the run sets `PASSENGER_BUILDKIT_CHECKS=off`, the default of Check
Dockerfiles with BuildKit (`Containers.BuildKitChecks`), which `EngineChecksTests`' one test of it turns on in its window's
Settings. No test reaches GitHub: the run points `PASSENGER_RELEASES` at a dead loopback
address, and a test that needs releases gives its window a `Releases` feed of its own (`UpdatesTests`); one that tests
an update gives its window the updater of a copy installed in a test folder, and no other window has one; each
version's disk image is made once a class (`UpdatesTests.DiskImages`), since hdiutil takes seconds over each. After every window test, pass or fail, the windows it left open are closed,
what they started is waited out and its folders are removed (`TestsLeaveNothingBehind`), so one failure never
cascades into the next test's waits. A plain `[Fact]` that runs window code through `HeadlessUnitTestSession.Dispatch`
must not end on the session's thread (the next window test would wait on it for good): it hands a failure back as the
run's answer and `await Task.Yield()`s before throwing. After a resume, a debugging test waits for the session to leave
the stop before waiting for Ended or Paused. A Mac that sleeps during a run fails tests with "The IDE was not quiet within …"
(Settle's limits are wall-clock); `pmset -g log` shows the sleeps, and the lead's full runs go under `caffeinate -is`.

What runs beside what is chosen (`Collections.cs`): window tests run one at a time on Avalonia's session thread and, as
the assembly's one collection, class after class; beside them run only the debug adapter's protocol tests
(`DebugSession.Collection`). A class with a test that holds what it tests to a time (a key's, a frame's, how soon the
window answers, a profile's samples, the debugger's evaluation limit), or that debugs through the window, goes in
`Alone`, which runs after the rest with nothing beside it. A class whose tests share one project derives its fixture
from `SharedProject`: its windows share one warm language server (`MainWindow.WarmServer`), the project is put back as
written before each test, and a file a test adds goes through `project.Write`, which ends the shared server: a file
every test opens is the project's from the start (`LongFileAssistTests`' 20,000 lines). A class whose tests each restore
a fresh folder takes it from `RestoredFolders`; one whose tests mostly leave the project as written shares it and takes a
fresh folder only for a test that changes it (`DebuggerTests.Sum` and `Fresh`), since `RestoredFolders.Take` may come in
the test itself, its batch being no test's own (`TestFolders.Unowned`). A shared project that its first test builds
makes its bin first (`DebuggerTests.Sum`): bin's making changes the folder's own time, which the next reset takes for a
file added and ends the server for. A `SharedProject` leaves a repository's `.git` alone (a class whose tests change the
repository puts it back itself) and keeps bin and obj between tests, so a window that never saves launches on an
up-to-date build (`HotReloadEditsTests`); its `Dispose` is virtual for a fixture that holds more (`ProjectFileHelpTests`'
stand-in feed). A rule held under both looks is checked in one window, the look switched in place through the window's
settings (`UiRulesTests.UnderEachLook`), a failure naming the look. `RestoredFolders` saves nothing for classes whose
tests change their folder throughout (`ExplorerFilesTests`, `PackagesTests`, `FileNestingTests`): share a
`SharedProject` where the tests mostly read. CI splits each system's suite into shards, a runner each (#206), so a class
never counts on another having run before it: each shard lists the suite (`tools/test-shards.py list`, listed again while a
listing names no test or says discovery failed: xunit 3.2's runner can spoil its answer to the adapter's -assemblyInfo,
xunit #3576) and `tools/test-shards.py` deals the classes
by their seconds on that system in `tests/Passenger.Tests/TestTimes.json` (one it lacks by its budget), the same deal in
every shard, checked before any test runs to put every test in exactly one; inside a shard the collections keep their
order. A shard writes its classes' times into its summary (`tools/test-times.py` over the .trx, budgets in
`tests/Passenger.Tests/TestBudget.json`): a class over its seconds warns, and a shard over its 22 minutes fails (the
whole run was an hour in one job a system; 20 until the MVP's last week grew macOS's classes to 72 minutes, which four
shards deal at about 19.6 each, and a fifth macOS shard would queue behind the disk images for GitHub's five macOS
runners; on e258c203 macOS's classes came to 87 minutes, about 21.8 a shard, so the budget has little to spare). Each
system's summary job fails unless every shard succeeded and every test
has exactly one result, and warns when its run's own times would deal a slowest shard two minutes shorter: `just
test-times RUN` writes them to TestTimes.json, for committing. Raise a budget, or add a shard, knowing why. The run
sets its own nice value to 0 when it starts below it (GitHub's macOS
runner starts at −10), since MSBuild keeps no idle node at any other; a test that needs one skips, saying why, when
the run is above 0 and cannot come down (`Nice`). MSBuild itself sometimes keeps no node (a race as a build ends, or
more than 1.5 nodes a processor running machine-wide): a test that needs one waits for the count
(`Processes.MSBuildNodesCounted`), builds again, or skips saying why.

The debugger's tests (`tests/Passenger.Tests/Debugging`) run the adapter from the tests' own `debugger/` folder, as
the IDE does, and launch or attach to the programs in `tests/Debuggees`, whose lines and columns (tabs included) they
stop at: moving a line there means moving the tests that name it. A line breakpoint on a line with a lambda binds inside
the lambda, so a test stopping at such a statement gives its column. `SelfDebuggingTests` opens this repository and
debugs Passenger.App; it finds its breakpoint by the code `status.Text = why;` in MainWindow.Actions.cs, so changing
that line means changing the test. `TerminalConsoleApp` there needs a real terminal;
window tests copy it into a folder of their own (`Debuggees.CopyTo`). The protocol tests run one at a time (`DebugSession.Collection`), beside the window tests; one that holds the engine
to a time runs in `Alone`. `PASSENGER_ENGINE_LOG=FILE` keeps the engine's own log, for the
tests' sessions and the IDE's alike. `Debugging/Helpers/StartHold` holds a launched program's runtime at its start by
pointing `DOTNET_DiagnosticPorts=…,connect,suspend` at a port of the test's own, which comes before the runtime starts
its debugger (LaunchTests, DebuggerTests, and McpTests through a launch profile); a window test with a held start waits
with `Ui.PumpUntil`, since the start holds BackgroundWork. Attach to Process's search matches text, so tests choose a
program's row by its id (`ChooseById`). LoopingConsoleApp's `waiting` and `waiting first` modes give an async method a builder whose
`ObjectIdForDebugger` waits on the paused passing thread, `elsewhere` awaits something that goes on on a thread of
its own, and `race` races a step's end with another thread's breakpoint, the hit first in most turns (`race throws`,
with its exception's handler found, which an exception breakpoint's condition is tried at). `LiveTreeTests` debug `tests/Debuggees/LiveTreeApp`, headless, with no project file
of its own (the test writes one on the tests' own Avalonia version); "click X,Y" typed into its Debug console stands in
for the pointer. `BindingErrorsTests` run and debug `tests/Debuggees/BindingErrorsApp` (written the same way), whose
caption binds a misspelt Name; "rebind" typed into its console makes it fail again. `XamlBreakpointsTests` debug
`tests/Debuggees/XamlBreakpointsApp` the same way ("paint", "warn" and "rename" typed into its console change Save's
background and width, the buttons' style class, and the caption's binding). `XamlEditingTests` drive each XAML editing
tool in an Avalonia app and a WPF app of their own (`AvaloniaGallery`, `WpfGallery`), the MVVM Toolkit stood in by its
attributes' full names. `ProjectFileHelpTests` drive the MSBuild code help on an SDK-style project and a
Directory.Packages.props with `StandInFeed`. No test sees the system's window frame: `TitleBarTests` checks the roles
Windows is asked about through Avalonia's internal `IInputRoot.HitTestChromeElement` and counts moves by
`MovesStarted`; a real Windows window is checked by hand in the VM (`~/Documents/passenger-vm/handcheck/bar205`). A
screenshot's `--type` sends its text as one input, so completion opens only when its trigger is typed alone
(`--type "<" --type "Tar"`). A test of what reaches the language server fails if the server ended during it
(`CSharpService.Stopped`), since a server started again is sent the whole file, which hides a drift. `TestFramesTests`
(fixture `HeadlessTestsFolder`) run an xunit v3 headless project on the platform and one on VSTest
(`IsTestingPlatformApplication=false`), restored through `AvaloniaApp.UseFeed`. `WebLanguagesTests` need Node.js on
the PATH and skip, saying why, without it (the TypeScript ones also on a Node.js older than typescript-language-server's
`engines.node`). `EslintPrettierTests` copy the tests' own eslint and prettier, and Prettier 2 as `prettier2` (npm's
alias, named as Settings' Manual package) (`tests/Passenger.Tests/NodePackages`, put in `node-packages/` at build by
`tools/Passenger.NodePackages`) into their project, `Project.Holding` a configuration that holds Prettier while a test
acts; the run's npm is offline with
its own cache and config (`TestRun`), so Run 'npm install' fails fast without the registry. The Windows VM has no
Node.js, so the web languages' tests skip there; CI's Windows runner has it. `BindingErrorsTests` also debug BindingErrorsApp with XAML Binding Errors on. Headless, a window
never loses activation, so a menu stays open under a dialog, and when the dialog closes the window puts focus back on
what it remembers, never a menu (a focus scope of its own): `MainWindow.KeepKeys` gives an open menu the keys back.
`UiRulesTests` holds the window-wide rules (menu marks and placement, tool window tabs and rows, popup widths, titles
and rows, scrollbars, the editor tab's ×, the focus ring, the half-1366 layout and empty texts), and
`DialogButtonsTests.Check` also holds dialogs to the plain button look, a disabled default, the chrome page, one
drop-down width per column and Don't ask again's place.
A test waits for a Run, Debug, Profile or test run with `Ui.Launched`, and says the status with `Ui.Said`, both naming
the build's errors (`Ui.BuildErrors`: Problems' rows, else the Build window's last lines) when it failed, so a CI-only
build failure says why. A test's stand-in that holds a file a target writes leaves it as it found it: a file it creates, newer than the
target's inputs, makes MSBuild skip the target as up to date (an empty `obj/…/Avalonia/resources` was embedded:
AVLN9999). Read any log the IDE or a server may be writing with sharing (`Ui.Shared`), and a `WaitFor` condition returns false,
never throws, while what it waits for isn't there yet; `WaitFor`'s limit also covers background work held all along (a
build, a test run), where `Settle` alone gives up after 90 s (scaled). A chord goes through one `Ui.Press(window, "control M ,
control S")` or `Ui.Run`: the window forgets a chord's first key after 2 s (a dispatcher timer that fires during a
settle), so a settle between its keys can lose it. Find Usages, Safe Delete and every list of the server's locations
make each URI a path once (`DocumentUris.ToPath` asks the disk), never once per location on the UI thread. A test that opens a dialog holds it to `DialogButtonsTests.Check`
(centred words, one height and radius, Rider's order). `HotReloadTests` and `HotReloadEditsTests` (Alone) edit debugged
programs (`KeyTextInput` drops `\n`, so multi-line edits go through `Typing.Replace`); `WpfLiveTreeTests` (Alone,
Windows only) debug `tests/Debuggees/WpfTreeApp` in a real window, and `McpTests.WpfApp.cs` drives it with `--mcp`. `WindowsOnlyProjectsTests` open a dotnet new wpf and winforms app
(`WpfApp.Create(asMade: true)`, `WpfApp.CreateWindowsForms`) never restored: Passenger restores and builds them itself.
`DecompiledSymbolsTests` (Alone) give each adapter a cache folder of their own; PassengerCases' `corelib` case stops in
a callback from List.ForEach, CoreLib's frame below it. `BindingValuesTests` debug `tests/Debuggees/BindingValuesApp`
("rename" typed into its console renames the customer on the UI thread). `AvaloniaFramesTests` (Alone) profile,
debug and run `tests/Debuggees/FramesApp` (its SlowPanel spins 30 ms a measure). `DebugSessionsTests` (Alone) debug two
programs at once (`Pair`: First and Second, which share Shared/Counter.cs), and `McpTests.Sessions.cs` has an agent name
which; its test of a session elsewhere has the adapter let its program go rather than killing the adapter, since on
Windows a killed debugger takes its program with it. `ContainerDebuggingTests` (Alone, skipped saying why without a Linux
engine) attach through Attach to Process to a program in a container they start themselves (`Turns`: a .NET 10 console
app built here in Debug, as it is, with its PDB naming the container's paths and naming a Windows machine's by PathMap,
copied into images of `mcr.microsoft.com/dotnet/runtime:10.0` and its `-alpine`, labelled with the run's label and
removed with the fixture), on glibc and musl, through a path map (`AttachThrough`, as a container run configuration
will), and with tools downloaded from `UpdatesTests.StandIn`; the fixture publishes the tools for the engine's processor
into `artifacts/container-tools` (seconds when up to date, a minute or two the first time); `ContainerPrograms` builds,
images and publishes for both. `ContainerProfilingTests` (Alone, skipped the same way) profile `Spins` (a program that
spins in `Hot.Spin` and keeps `Keepsake`s, saying how many, made once a run, `Spins.Shared`) in containers they start:
chosen in Attach Profiler's dialog, debugged there first, held at its start (let go once recording), and memory
snapshots compared; `McpTests.ContainerProfile.cs` has an agent profile it with `attach` and `container`. `ContainerRunDebuggingTests`
(Alone) debug ContainerRunsTests' Compose solution (`ContainerRunsTests.Shop`, its Program.cs stopping in Main and in a
request's handler), run it with Hot Reload, and in images mode build its apps here with `-p:PathMap=…=/src/` copied into
aspnet:10.0 by Dockerfiles of their own, so no SDK image is pulled; a request is asked again while nothing listens yet,
since the attach lets the service go a moment before Kestrel listens. `WpfPreviewTests` (Windows only)
preview WpfXamlTests' gallery; the VM's hand check is `~/Documents/passenger-vm/handcheck/wpfprev200/preview.cmd`.
`XamlTests`' `AvaloniaApp` makes `dotnet new avalonia.app` from templates installed into the run's own hive
(`--debug:custom-hive`) and takes Accelerate's DevTools out of it.
The run's IDE keeps dotnet new templates in its own home (DOTNET_CLI_HOME) on macOS and Linux and in
`ProjectTemplates.Hive` (a hive of the run's) on Windows, apart from `AvaloniaApp`'s. `AvaloniaApp.Feed` is the run's
local feed (Avalonia.Templates at `AvaloniaTemplatesVersion`, downloaded at restore by a `PackageDownload` and copied
from the package cache), so installs never reach nuget.org; `UseFeed(folder)` points a folder's nuget.config at it
alone. `NewSolutionTests` installs Avalonia's templates through New Solution and runs the app it made headless.
`WpfXamlTests`' app (`WpfApp`, `dotnet new wpf` at the test project's own target) restores from that feed alone:
Microsoft.WindowsDesktop.App.Ref is a `PackageDownload` at Directory.Packages.props' `WindowsDesktopRefVersion`, which
the tests check against the version the SDK's KnownFrameworkReference gives the target, failing with the version to
pin when an SDK changes it; off Windows the app sets EnableWindowsTargeting and MarkupCompile builds it there too.

The MCP server's tests (`McpTests`) are agents using the SDK's own client: one starts `Passenger --mcp` and debugs a
project through it, others attach to a window with `--mcp-attach` (its sockets in the folder named for the run's
`PASSENGER_SETTINGS`) and change what the window shows, once it lets them in and the person answers its questions.
An agent that gives up on a call sends the protocol's "cancelled" notification itself (`CallGivingUp`): the C# SDK's
client can drop its own when the call's token is cancelled. `McpTests.AvaloniaApp.cs` drive `tests/Debuggees/AgentApp`
(headless, its popups windows of their own; `AgentAppFolder` writes and restores it once per class from
`AvaloniaApp.HeadlessProject()`, which LiveTreeApp and BindingErrorsApp also use). The Windows on Arm VM runs a test
filter on a commit with `~/Documents/passenger-vm/vm-test.sh NAME FILTER` (66–120 min for the whole suite, by the Mac's
load); the VM can't see dot folders, and a batch file in the share runs as `powershell -Command "& cmd.exe /c '…'"`.
The Ubuntu VM ("Ubuntu 26.04 ARM64", a real GNOME desktop CI lacks) runs one with
`~/Documents/passenger-vm/vm-test-linux.sh NAME FILTER`, headless as CI's are, from `~/src/passenger-NAME`, its .NET in
`~/.dotnet` and Node.js in `~/.local/node` on the login path; a script in the share runs as `bash -l
/media/psf/Home/…`. `TemplateFamiliesTests` installs and uninstalls a test template of its own (`probetest`, from a
folder) in the run's template home while a dialog is open; the watch sees it.

## Code style

- C#: four spaces, file-scoped namespaces, braces on every block, explicit types over `var`, accessibility always
  written. `.editorconfig` enforces most of it in the build.
- Comments say why, or what else depends on this, never what the next lines do. A type's or member's `///` summary
  is at most five lines; an in-body comment is one line.
- XAML: four spaces; one style per selector in `Theme/Shell.axaml`, restyling Fluent's states on its
  `PART_ContentPresenter`, where Fluent paints them. Shell.axaml starts with base styles every Button, TextBox,
  ComboBox, NumericUpDown, CheckBox and MenuItem takes (content centred, `Passenger.ControlHeight` 28, the radius
  resources `ControlRadius` 4, `ToolRadius` 6, `PopupRadius` 8); a class style comes after them and says only how it
  differs. A visual inconsistency is fixed once in a shared place (a token, a base style or resource, or a helper such
  as `DialogFooter.Of`), never per control, Passenger's own consistency settling a design in question; a control that can't use
  the shared piece says why in one line. A dialog's buttons come from `DialogFooter` in a `Border.footerBar`:
  `Of(default, cancel, more…)` for OK, Cancel, Apply; `Listed(…)` for Rider's DialogWrapper order (the default last on
  macOS); `Answers(…)` for a question's answers as Rider's message dialogs lay them (the row reversed on macOS); what
  stands beside them (Don't ask again, a problem) goes at the footer's left through `DialogFooter.Bar`/`Row`. A list or
  table in a page is `Border.framed`; a rule inside a page is `Passenger.Separator` (`Passenger.Divider` is for edges
  between panels). PopupHost gives each popup room for its frame's shadow. A menu's value in force is `MenuChoice`
  (checked, never greyed); a tool window's tab is `ToolTabs.Tab` (`panelTab`), a choice among a few `segment`; popups
  take widths from `PopupFrame` and titles from `popupTitle`; every list, menu and popup row is `ListRow.Height` (24); a
  tool window's empty state is `TextBlock.emptyText`; a row that narrows is a `ShrinkingRow`; menus' shadow room and
  placement are `MenuShadow`, scrollbars `ControlThemes.axaml`, the focus ring `FocusRing`; every Window's ground is the
  chrome. `MainWindow.SmallestWidth` is 640: the layout works at half a 1366-wide screen, and the person's first window
  and New Window fit the screen's working area before they're shown (`MainWindow.FitToScreen`). A dialog's content is
  `DialogFooter.Page(page, DialogFooter.Bar(buttons[, left]))`; a rounded Border takes `Border.kindBadge` or
  `Border.tag` (the badge radius) or `Border.box` (the controls' radius), never a literal radius (UiRulesTests); a
  toolbar down a tool window's side is a `ToolColumn`, what has no room going behind More.
