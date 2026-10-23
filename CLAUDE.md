# CLAUDE.md

Guidance for Claude Code working in this repository.

## What this is

**Passenger** is an IDE for C# and modern .NET (CoreCLR only: no .NET Framework, Mono or Unity), written in C# on
.NET 11 with an Avalonia 12 UI, for Windows and macOS first. The MVP shipped as v0.1.0; the work to do is the issues
of the release in progress, a milestone and a branch of its name (0.2.0 now:
https://github.com/Zaneris/passenger/milestone/3), with the features waiting beyond it in the Backlog
(https://github.com/Zaneris/passenger/milestone/2), each issue saying what Passenger does now, what it should do and
when it is done (**Now:**, **The aim:**, **Done when:**). Read the issue before starting anything larger than a fix, and
close or tick it when it lands. A new feature is the best Passenger can make it from what
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
just unicode-widths        # rewrite src/Passenger.Terminal/WideCharacters.cs (the terminal's wide characters) from the
                           # Unicode Character Database at the version .NET's CharUnicodeInfo knows, its files in
                           # artifacts/unicode
just aspire-protocol       # rewrite the checked-in C# of Aspire's resource service protocol (src/Passenger.App/Aspire/
                           # Protocol) from its .proto, with protoc from a pinned Grpc.Tools fetched into artifacts/,
                           # so no build runs protoc
just app-icon              # Passenger.icns, Passenger.ico and passenger.png from Assets/passenger.svg, by sips; fails
                           # unless every size's corners are transparent (never write the three files any other way)
just package-linux [RID]   # artifacts/package/Passenger-linux-x64.tar.gz (or -arm64): one passenger/ folder (app/,
                           # the launcher, install.sh, the desktop entry, icon and file types from tools/linux), checked
just package-win [RID]     # artifacts/package/Passenger-windows-x64.exe (or -arm64), Velopack's per-user installer;
                           # signed on Windows by PASSENGER_SIGNTOOL_PARAMS or PASSENGER_ARTIFACT_SIGNING_METADATA
just container-tools [RID] [--archive]  # artifacts/container-tools/<rid>/, the container tools self-contained (linux-x64,
                           # linux-arm64, linux-musl-x64, linux-musl-arm64; none: the engine's processor, glibc), where a
                           # dev build finds them; --archive also the release's Passenger-container-tools-<rid>.tar.gz
just notices               # fail naming any package the solution, Roslyn's server, the container tools or a dotnet tool
                           # carries that the notices miss
just test [filter] [N]     # every test, or those whose name matches, in N test processes at once then the Alone classes
                           # in one (tools/test-run.py; N 1 is one dotnet test); results in artifacts/test-results/local
just test-times RUN [RUN...]  # rewrite TestTimes.json (each test class's seconds on CI, which deals the shards) from
                           # CI runs' results
just mcp [FOLDER]          # Passenger as an MCP server over a folder (this one by default), from a clone of the last build
just site-shots [SCENE...] # site/images/<scene>-<dark|light>.webp retaken (after just build; macOS, cwebp) from the
                           # sample in tools/site-shots/sample, copied under TMPDIR with a fixed history, its NuGet
                           # under artifacts/site-shots; SITE_SHOTS_FLAGS (--window-buttons by default) on every capture
```

**Use Passenger on itself.** `.mcp.json` starts Passenger's MCP server over this repository (`tools/mcp.sh`), so its
tools are the quickest way to ask Roslyn about Passenger's code (`diagnostics`, `find_symbol`, `outline`, `usages`),
to debug the IDE's own projects, and to look at the window (`app_screenshot` with `app` ide). It runs from an APFS clone of the last
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

Builds land under `artifacts/` (`UseArtifactsOutput`), and dotnet test's results in `artifacts/test-results/`
(the tests' `VSTestResultsDirectory`), never beside the sources. Package versions live only in
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
  group per child, which a watchdog shell kills once Passenger's pipe to it closes). Every command line it says (a child's
  start and end in the log, what it throws) and every console's echo of one go through `ChildProcesses.Shown`, which the
  IDE makes `ContainerSecrets.CommandLine` as it starts (`IdeErrors.Install`): `InSaid`'s mask on each word alone (a
  `--password=…` masks its value, never the words after it, as a whole line's connection-string rule would), also knowing the
  secret-named values of the child's own environment, so no program's secret reaches the log or a window, whoever started
  it; `ChildStart.Command` and `ChildEnd.Command` stay the command as it ran, for what watches the programs. A command line
  is any process's on the machine to read, so no command line Passenger starts holds a secret: one a program can take
  otherwise is given it so (the Migrations window's connection strings, as its environment), or not made (the development
  certificate exported for containers as PEM with no password, #442). A fork costs a large process tens of
  milliseconds (#233), so `RunAsync` and `StartAsync` start a child off a caller that has a context of its own (the UI
  thread's), on the caller's thread otherwise: every git command (`Git`) starts so, and every start an async caller
  makes (the project host, a test host, NuGet's changes and credential providers, dotnet ef, the container engine's
  commands); `Start` stays on the caller's thread for what needs the child at once (a console's `ProcessRunner.Start`),
  while `ProcessRunner.StartAsync` (an AppHost and its services) and a run started after a wait (`StartDotnet`'s `after`:
  every build) fork off it, held as background work, a Stop meanwhile ending the child as it comes. A child the IDE ends (`Kill`, or
  `Ending` before asking it to go: a language server's shutdown, the host's input closed) and an exit code the caller
  names as an answer (`RunAsync(answers:)`, git's 128 for something not there) are no failures: their end is logged
  only with Log every program. `RunAsync`'s limit covers a child's run to its exit; once it has exited, whatever the
  limit, its output is read for a 2 s grace (`OutputGrace`), then a job it left holding the output open (a git hook's
  `daemon &`) is ended with its group and the output let go; its `mostOutput` ends a child that writes more than its
  caller reads of it (`ChildOutputException`: a container tools' `processes` past 4 MB, `sources` past a few KB a
  method). `DotnetCommand` builds without
  MSBuild's server (which Stop could not reach) and in nodes salted to this IDE, which end with it. `Dotnet`,
  `Programs` and `ShellEnvironment` find the tools as the login shell would (the shell writes its environment to a file,
  its output to /dev/null, so a job its profile leaves running holds nothing up, and `Ready` waits out a slow profile
  once, 10 s from the read's start); `PathNames` makes paths canonical
  (`realpath`, or `GetFinalPathNameByHandle` on Windows), answers `IsUnder` for every "is this inside that folder", and
  `FoldersUnder` is every walk of a project's folders (the XAML core's, and the project host's stamps, which compiles
  PathNames.cs in): each folder once by its canonical path whatever links reach it, none a link goes up to, as MSBuild's
  globs walk them. `PathNames.Comparer` (with `Comparison` and `Same`) is every path comparison and every collection keyed by
  paths, the host's and the XAML core's too; `FilesUnder` is every walk of the open folder's files (each linked folder
  resolved once, never entered twice or outside the walk), and `IsSystemFile` (.DS_Store, Thumbs.db) the one list of the
  system's own files no walk or listing shows. `AtomicFile.Write(path, bytes, mode?)` is every write of a whole file: a
  temporary file of its own made new beside it, its Unix mode set from creation, flushed, then renamed over it.
  `SocketFolders` is where every Unix socket Passenger makes goes (Mcp below), and a socket made catches
  `ArgumentException`, a path too long for one, as it catches `SocketException`.
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
  Studio keymaps, and its VS Code keymaps for Settings' choice, `KeymapPreset`) and the highlighting contract
  (`IHighlighter`), with JetBrains Mono embedded.
  An inlay hint (`CodeEditor.Hints.cs`) is a parameter's name, which goes with the text after it, or a type, which
  goes with the text before it, or a value (`InlayHintKind.Value`: what a compose file's `${VAR}` comes to), which goes
  with the text before it as a type does and shows whatever Settings say of the other two; the hints at an offset are a
  caret place of their own, as IntelliJ's inlays are. A row's hints are kept while
  the text, the hints and how they are drawn stand (`rowGaps`), and a hinted line's text is drawn a run at a time, each
  run once in the piece it is in (one a hint stands inside clipped to each piece it spans), so a frame of hinted lines
  costs about what the same lines without hints do. A file's hints are a `HintList` (runs, as `FoldList`'s regions are:
  an edit moves the runs it reaches, the rest shifted whole), which the open file carries through each edit
  (`OpenDocument.InlayHints`, as its folds) and each editor carries too, ahead of everyone (`OnChangedFirst`, before the
  wraps, so the key's lines wrap with their hints where they now stand); the editor takes a host's list as it is when in
  order and, with soft wrap on, wraps again only the lines whose hints are drawn otherwise (`HintList.Differing`), so a key
  costs the hints it reached, never the file's. `CSharpDocument` places the server's hints off the UI thread.
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
  before through the nodes Roslyn reused, never from a walk of the whole tree, a range classified takes only the trivia
  it covers (`CSharpClassifier.AddTrivia`, found by halving: a run of thousands of `//` lines is the trivia of the one
  token after it), and the server's colours are asked for
  the screen's lines first and the whole file only once typing stops. Each line's colours read from the tree are kept
  and carried through the edits made until the next parse lands (`CSharpHighlighter`'s lines, by a frame's runs of whole
  lines; `CharactersClassified` counts what the UI thread classifies), so a key classifies again only the lines it
  changed, however far the parse falls behind (as while a solution loads); the parse classifies the lines last asked for
  on its worker and its landing keeps them as the lines' colours (`CSharpHighlighter.TakeLines`, carried through the
  edits made since), so the frame after it classifies none again. A finder never tells its answer inside the
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
  loads as loose XAML (`LooseXaml`: the root of its tag's type, x:Class, events, EventSetters, x:Code and a root
  window's Visibility blanked); App.xaml's resources become the application's; `pack://application:,,,/` is the
  project's through WPF's private resource-assembly fields. A WPF control with no code-behind has no constructor calling
  `InitializeComponent`, so it draws empty where it is used, in the app and its preview alike. A window is drawn as
  itself and never shown (`Surface`): WPF makes its window as it does before a Show (`WindowInteropHelper.EnsureHandle`,
  sized as asked even past the screens), Window's private `UpdateVisibilityProperty` makes it Visible as Show does
  first, and it is that window's root visual, so its content finds it as in the app (an ancestor binding of its type,
  `Window.GetWindow`, its own style, template and triggers); the frame is its client area under a drawn title bar as
  tall as the system's caption (none without one, or under a WindowChrome), a maximized or minimized window drawn
  normal. Its limits: it is of its tag's type, never its x:Class (an ancestor binding naming that finds nothing); it
  counts as not yet shown (`Activate` and `DragMove` throw as before a Show); one whose style sets its Visibility, which
  would show it, is closed unshown and refused. A page is the content of a frameless window of the previewer's own
  (AncestorType=Page and Window resolve, `Window.GetWindow` finds that window, the page's WindowTitle and sizes go to
  it), with no NavigationService.
  `Features` also has `XamlFormatter`/`XamlFormatSettings` (Rider's XML formatter, which is XAML's, at Rider's
  defaults, read from .editorconfig by Rider's `xml_*` keys or their `resharper_` forms and `max_line_length`; XAML
  Styler's attribute order and empty-element options as `xaml_enable_attribute_reordering`,
  `xaml_attribute_ordering_rule_groups` and `xaml_remove_ending_tag_of_empty_element`, off by default; otherwise only
  whitespace changes, and a second pass changes nothing), `XamlRefactorings` (Extract Resource, Extract Style, Surround
  With's targets) and `CreateFromUsage` (a binding path's property or command, and a missing converter; the MVVM
  Toolkit known by its attributes' full names). `CSharp/CodeAssist.XamlEditing.cs` is the window's side (dialogs in
  `Xaml/XamlRefactoringDialogs.cs`, surround templates in `LiveTemplates.Xaml`), each change's files applied through
  `ApplyFileChanges` as one command; Settings' Code Style: XAML page is `AppSettings.XamlFormat`.
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
  whole through a temporary file and a rename (`LibrarySources.WriteWhole`, over `AtomicFile`).
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
  and makes requests in the editor's offsets; whether a place is in a server's range is `LspRange.Holds`, and the symbols
  of an outline that hold a place `DocumentSymbol.PathTo`. A server that stops, restarts or refuses a request is one exception,
  `LanguageServerFailed`, which callers catch; StreamJsonRpc's own types never leave the project. `RoslynExtension` is
  Passenger's own extension to the server (`src/Passenger.Roslyn.Extension`, compiled against the server package's own
  assemblies through `RoslynServer.targets`, carried under `roslyn-extension/` with none of them): the server loads it
  with `--extension`, `server/_vs_activateExtension` registers its handlers, and `LspDocument.ExtensionAsync` sends a
  message through `textDocument/_vs_dipatchExtensionMessage` (`RoslynSession.ExtensionAsync` one about the whole
  solution, as `ConnectionReads`, which with `keys` also answers every configuration key a project reads or binds, and
  `WebRoutes` are). A handler reads the server's solution and answers with
  edits or what it found, never changing its workspace. Razor (.razor, .cshtml) is the server's own, cohosted in it: `LspDocument.IsRazor`
  opens such a file as `aspnetcorerazor`, its colours take the legend Razor registers as it starts (why the client
  capabilities take dynamic registration), its diagnostics are one pull, and `RazorHtml` answers what Razor asks the
  client of a file's HTML, through the HTML server (below; without Node.js, HTML's element names and global attributes
  alone). A request nothing waits on, sent as a window closes,
  goes through `LanguageServer.Tell`, never awaited on the closing window's thread. Requests resume off the caller's
  thread inside Passenger.Lsp (`ConfigureAwait(false)`), and every answer that grows with the solution or the file
  (completion's list, usages, definitions, implementations, code actions, inlay hints, the outline, a pull's diagnostics)
  is read into records in `LspDocument` on the thread pool before its caller resumes (`LspDocument.Read`, which forces the
  await off the caller's thread), so the UI thread never decodes it. `CountedMessages` notes each of the
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
  line now (its map of the built lines to the lines now and back kept as runs, `LineRuns`, which an edit splits only
  where it reaches them and moves whole past them, so a key in a debugged file costs what it changed), and Hot Reload's
  rude edits are listed at the editor's line. XAML's compilers count lines as an XML reader
  does (`ServerLines.OfXml`: a lone '\r' ends a line, U+0085, U+2028 and U+2029 do not), and `ServerPlaces.Compiled`
  gives any file's lines as its compiler counts them, which BuiltLines and a profile's hot spots read the PDB through,
  and a build's problems too (`BuildRunController.Placed`, through `ServerPlaces.PositionOfCompiled`: Avalonia's AVLN and
  WPF's MC errors in Problems, an agent's `problems` and the XAML editor's markers at the editor's line, the place their
  message repeats, "Line 8, position 6.", said there by `BuildOutput.Moved`). A console's link to a compiler's message or a
  stack frame converts the same way (`FileLink.CompiledLines`), in a XAML file only for a compiler's message (its
  severity and code after the place): Passenger's own failed-binding lines name the editor's. The previewers' errors and
  notes name the line of the text they were sent as their XML reader counted it, said at the editor's
  (`XamlPreview.Where`; the WPF previewer's `WpfPreviewNote.Line`, App.xaml's note no line, as WPF's names one of the
  dictionary text the previewer made).
  The places a running app reports of its XAML (each element's XamlSourceInfo, in the Live Visual Tree's details and Go
  to, a failed binding's in Problems, the consoles and its fixes, the Frames page's rows, `app_details` and `profile`)
  are its XAML compiler's lines too, WPF's BAML as Avalonia's XamlIl, and `LiveTree/AppPlaces` reads them at the editor's;
  the inspector never words a place, since the IDE could not convert it: an overridden setter's overriding style is
  `SetterReport.ByPlace` (protocol v7), a property breakpoint's setting style `Breaks.Changed`'s `setAt` (a place as the
  protocol writes one), and `$bindingError`'s Xaml, which the app words for conditions and log messages, is shown in
  Variables and `debug_variables` at the editor's (`DebugController.MembersAsync`, from the failure's place).
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
  through `CodeAssist.Browse`; the file an extends names read through `ComposeText`, as `ComposeNames` reads it), tried
  before the
  server's own (`CodeAssist.Compose.cs`) and shared with MCP's `definition`; a Dockerfile's FROM image that names no
  stage goes to its page too (`CodeAssist.FromImageAt`). `ComposeNames` is completion of the file's own names, from the
  same outline, where a service names one (the place read by indentation as typed, half a line too, `ComposeSpot`): its
  other services under depends_on (a list or a mapping), links, volumes_from, network_mode's `service:` and extends (the
  services of the file an extends names), its volumes under a service's volumes (a short entry's source, a long one's),
  its networks, secrets and configs under theirs; never the service itself nor a name its list holds on another line.
  `CodeAssist.ComposeCompletionAsync` lists them before the server's items, which are kept but for one of the same label
  or one that would replace another stretch than the name's. `ComposeRename` is Rename of the file's own service,
  volume, network, secret or config, from the same reading (what the place names is `ComposeDeclarations.Named`'s, or a
  declaration's key; where each file writes it `ComposeText` and `ComposeFixes.References`, an extends of it too): where
  it is declared and every use, in the file, the one Compose reads with it (`ComposeCheck.ReadWith`: a base and its
  override) and, for a service an extends names in another file, that file and the compose files beside it
  (`ComposeCheck.Unasked`), worked out again as it is made and applied through `ApplyFileChanges` as one command of
  global undo; a name its kind has in those files, or one Compose's schema refuses, is refused, saying why. Rename tries
  it before the YAML server's own (an anchor's), and MCP's `rename` takes it (`CodeAssist.RenameComposeAsync`). The
  Structure window shows the web servers'
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
  projects as a build sees them, with the properties their container run configurations are found by, `CarriedProperties`,
  each with whether the project's own files set it rather than the SDK or a package, `Workspace/ProjectModels`, cached in
  the folder's `projectModels.json`, whose `Upgrades` forget what a build that carried less kept), `build`
  (Roslyn's design-time builds, written as its build host writes ProjectFileInfo, `Workspace/ProjectBuilds`, cached
  Brotli-compressed in `projectBuilds.json`) and `cancel` (a request given up: no more projects started, the build's
  submissions cancelled, so a left folder never holds the host), `hold` (a build of the IDE's own: no design-time build
  that generates files a build writes too (WPF's markup compilation, Avalonia's resources: `DesignTimeBuilds.Generating`)
  starts until it is cancelled, answered once those running have ended, since Windows lets one writer have a file;
  design-time builds also run with `UseAppHost=false`, so a build's apphost is created and signed by the build alone) and `inspect` (one project evaluated for the code help
  in its MSBuild files, `Inspection.cs`: the open files' unsaved text fed to MSBuild in place of the disk's; properties
  with the places that set them, items, targets, imports, the SDK's frameworks and the evaluation's errors (a skipped
  import as MSB4019), places by a file table, answered from its cache while the inputs and the folders its wildcards
  walked hold, those stamped as `evaluate`'s are (`Evaluation.Walked` and `Folders`, several folders read at once: about
  15 ms to stamp and 8 ms to check a project of 5,000 files in 361 folders, 50 ms and 25 ms for 20,000 in 1,111, within
  the 400 ms typing pause)), and `properties`
  (`Properties.cs`: a few properties of one project evaluated afresh with the global properties given, which is how
  Publish reads a publish profile, the SDK importing it by PublishProfile, a launch finds its program (`LaunchPlan`) and a
  test run its assemblies (`TestAssemblies`), each given `ProjectModels.Properties(workspace, project)`: no cold dotnet
  msbuild but for a target's run). Off Windows a project
  that references a framework the SDK marks Windows-only and leaves EnableWindowsTargeting unset is loaded again with it
  as a global property (`WindowsTargeting.cs`), in evaluate, build and inspect alike; Passenger's builds follow
  (`BuildOutput.WindowsTargeting`, `-p:EnableWindowsTargeting=true`), and Roslyn's server has it in its environment off
  Windows, since its `dotnet restore` takes no properties; it is never written into a project. Each result carries stamps of what it read
  (`Stamps.cs`), so a reopen redoes only what changed: the files it imported, its folders, and what would change it by
  coming (`Evaluation.LookedFor`: each Directory.Build.props, .targets and Directory.Packages.props MSBuild looks for from
  the project's folder up, as far as the first found, and the restore's `obj/X.csproj.nuget.g.props` and `.targets`,
  which the window evaluates again for as a restore lands), evaluate's, build's and inspect's stamped by the same
  `Evaluation.Inputs` and `Folders`, over the same `Evaluation.Load`; the caches are read off the UI thread (`StateDocument.ValueAsync`).
  The shown file's project and what it references load first (`ProjectSession.ActiveProjectsAsync`).
  `Passenger.Roslyn.Extension/ProjectLoader.cs` subclasses the server's own `LanguageServerProjectLoader` so the server
  loads the host's builds (`passenger/loadProjects`, `passenger/projectInfo`, `passenger/projectLoader`), compiled
  against the server's publicized assemblies (`src/Publicize.targets`, which the debugger imports too); a server
  without the host builds its projects itself. The server loads a batch's projects at once (`RunParallelAsync`), each
  given build read as its load takes it (`TryLoadProjectInMSBuildHostAsync`), but puts each into its workspace under one
  gate (`_gate`), so a warm reopen's projects still land one after another there. A file in several projects (a multi-targeted project's) has the
  editor's target framework switcher (`CodeEditor.ContextLabel`, `MainWindow.TargetFramework.cs`), its requests naming
  the chosen context (`LspDocument.Context`, Roslyn's `_vs_projectContext`).
- `src/Passenger.App/CSharp`: the language server in the IDE. `CSharpService` runs Roslyn for the
  folder; `CSharpDocument` gives an open file its semantic colours and diagnostics; an open file's diagnostics from any
  server (C#'s, a web language's, ESLint's) are a `ServerDiagnostics` (one pull at a time after a pause, as background
  work, what each asks of its server its own; the reports carried with the text), drawn by `DiagnosticMarks`; `CodeAssist`
  is completion, hover, signature help and the code actions (`app.code.*`), its popups on a layer
  over the editor. Completion's list is narrowed off the UI thread (`Filter`: scored and made into rows through
  `BackgroundWork.Run`, shown as it lands unless a later key's filter was asked for since; what commits or moves the
  choice, a commit character, Enter, Tab and the arrows, narrows it at once should its filter not have landed,
  `FilterNow`), and the popup keeps its rows (`CompletionPopup`'s `CompletionSlot`s, each row's controls,
  `CompletionRowView`, changing its words in place as the list narrows, never made anew at a key); `WorkspaceEdits` applies renames and fixes across files (a code action through `ApplyAsCommand`, one
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
- Configuration known from what the app reads (#421). The extension reads a project's keys in the one reading of what it
  reads (`Passenger.Roslyn.Extension/Configuration.cs`, `ConfigurationSource`, from `ConnectionReads`' walk and cache, asked
  with `keys`): a key by string (an indexer, GetValue, GetConnectionString, a section's Value) under the GetSection chain of
  its receiver (through a local assigned once; a section passed in reads nothing), the sections (GetRequiredSection's
  wanting one), and the options classes bound to a section (Configure<T>, AddOptions<T>().Bind and BindConfiguration,
  ConfigurationBinder's Bind and Get) with their properties nested, collections and dictionaries under `*`,
  `[ConfigurationKeyName]` honoured, each a `ConfigurationKey` with its type, its place and whether a read has a default
  or is checked for null (`??`, `?.`, a comparison or pattern, IsNullOrEmpty, the ThrowIfNull guards, in its local too).
  What gives a key before the app runs is `Workspace/ConfigurationFiles` (`ConfigurationProject`, the session's
  `ConfigurationOf` and `ConfiguredBy`): appsettings.json and appsettings.{environment}.json for the environments the
  launch profiles run in (Development where none says), a launch profile's `A__B`, the user secrets (the evaluated
  UserSecretsId, else the file's), a compose file beside or above the project or an AppHost referencing it naming `A__B`,
  and Passenger's own environment; the files read off the UI thread, the open ones as the editor has them
  (`ConfigurationFiles.Reader`). A C# file whose text names configuration's API (`CSharpDocument.MayReadConfiguration`, a
  look through its pieces) has each unguarded read of a key nothing gives warned of in its pull
  (`CSharpDocument.Configuration.cs`), but where Aspire's or a compose file's warning stands or an Aspire service reads a
  connection string; pulled again as a settings file, launch profile or compose file changes or a settings file is saved.
  Its fix (`CodeAssist.ConfigurationFixes`, an `EditorFix` that `Asks`) sets the key in appsettings.json as nested
  sections (`JsonText.SetKey`), or for a secret's name (`ContainerSecrets.Secret`) in user secrets as `dotnet
  user-secrets` keeps them, giving the project a UserSecretsId first where it has none, one command of global undo. In
  appsettings*.json and a project's secrets.json (`ConfigurationFiles.IsSettings`, `SecretsIdOf`), where the caret is read
  from the text as typed (`Workspace/JsonSpot`), completion lists a section's keys with their types and a bool's or an
  enum's values ahead of the JSON server's items (alone without one, `Completion.Settings`), Go to Declaration goes to the
  options property or the read, and the hover card says the type; from a read in C# Go to Declaration goes to where each
  file gives the key and the card says each with its value, a user secret's always as dots
  (`CodeAssist.Configuration.cs`). Manage User Secrets (`MainWindow.Configuration.cs`: a project's row, Find Action on the
  file shown's project) gives a project with none a UserSecretsId and opens its secrets.json, made where missing.
- `src/Passenger.App/Editors`: `EditorArea` is a tree of splits, each an `EditorGroup` (tabs, find bar, editor); its
  `CodeEditor` and `Active` are the active group's, so code that acts on "the editor" keeps working, and wiring for every
  editor's events goes through `ForEachEditor`, since a split makes a new one. An editor window (Open Tab in New Window,
  or a tab dragged out) is an `EditorFrame` of the same `EditorArea`, never a second area, so a file shown in two
  windows is one `OpenDocument`; the folder keeps the windows in `WorkspaceState.EditorWindows`. Pinned tabs come
  first (`EditorTab.IsPinned`, kept as `KeptTab.Pinned`); `EditorArea.CloseTab` is the one close a person's goes
  through, which Reopen Closed Tab remembers. `EditorArea.Commands` is Rider's global undo: a command that changes
  several files or moves one (`Commands.Record`, as renames do), or something besides files with one (`Recording.Also`, a
  `CommandHistory.Part`: Write to Launch Profile's run configuration), is undone and redone whole, asked first. The tab
  actions are `MainWindow.Tabs.cs`, filling a tab's menu through
  `EditorArea.TabMenuOpening` with `ActionContext.Tab` naming the tab they act on. `MainWindow.CloseSolution` ends the
  folder's session and shows the Welcome screen; `LeaveFolder` keeps or closes editors and `EndSession` ends the
  session, so new per-folder state belongs in `ProjectSession`, made with it and ended in its end. A folder opens with
  its own kept tabs whatever was open before it (#444, `EditorArea.Restore`): the main window's tabs open with no folder
  (a file opened alone, a licence About opened, What's New) stay, after the folder's in its current group, the one shown
  still in front, and are the folder's from then, so its saved tabs are never lost to them. An image (PNG, JPEG,
  BMP, GIF, WebP, ICO) or a TTF/OTF font opens as a read-only preview: `OpenDocument.Load` gives it an `AssetPreview`
  (`OpenDocument.Asset`, shared by its tabs, disposed with the document, followed by its bytes through a folder moved
  elsewhere) and an empty text stand-in, and each group shows it through an `AssetPreviewControl` of its own; it is
  never saved, converted or written. The asset is a stand-in (`IsPending`, the tab saying it is loading) until its
  decode lands off the UI thread through `BackgroundWork` (`OpenDocument.Decode`, one at a time); a first decode that
  fails closes the tab and the status bar says why (`EditorArea.OpenFailed`), a later one keeps the picture under the
  banner. Only the folder watcher's word decodes it again (`FollowDisk`): `ChangedOnDisk`, an agent's lookup and Git's
  reload of open files read nothing of it. Its `Language` is Image or Font, which the status bar names; its `HoldsText`
  is false, as a workbook's is, so the status bar's text widgets, Find and Replace in Files, Local History's look at an
  opened file, a line's actions and agents' `editor_text` and `edit` pass it over. Release notes the app carries take the
  editor's place the same way, rendered (`ReleaseNotesView`, What's New under Updates below), their text held but not
  `HoldsText`.
- `src/Passenger.App/Docking`: the tool windows' frame, in IntelliJ's six places. `DockHost` is the layout and what
  it keeps; `Stripe`, `ToolPane` and `FloatWindow` (a window of its own that knows nothing of tool windows, its keys and
  popups the IDE's through `DockHost.WindowOpened`) are its parts. A window moves by dragging a stripe button or a header (`DockDrag` says where it lands); `DragGesture` and
  `DropOverlay` are the one drag and drop zone, which editor tabs share. The layout is kept in the state store as a
  `DockLayout`, with the folder open and as the one a folder without its own opens on; `DockHost.RestoreDefault` puts
  each window back where `Dock.Add` put it. A new tool window is a view and one `Dock.Add` with an id of
  `ToolWindowIds` (what a kept layout records it by, so a value there never changes, and what every show, hide and
  `ToolWindow.Id ==` names): the shell's own (Explorer, Structure, Run, Debug, Unit Tests, Build, Problems) in
  `MainWindow.AddToolWindows`, a feature's in its `Add…` beside the view's wiring (Git and Commit in `AddVcs`, the
  profiler in `ProfilerCommands`). A window's start builds no view it hides (#233): a `ToolWindow` given how to build
  its view (`Func<Control>`, its header's `Tools` too) builds and wires it as it is first shown, by its layout kept too
  (`IsBuilt`, `Built`), and what reads or acts on it asks through a property that builds it (`StructureView`, `Find`,
  `Hierarchy`, `LocalHistoryWindow`, `Endpoints`, `NuGet`, `ContainersWindow`, `TerminalPanel`, `GitWindow`,
  `CommitView`: a route's request sent, a package installed or a project file's completion asking the feeds, a container
  action, a program run in the terminal, Show History, the branch widget's menu, Commit), while what only follows the IDE
  (a caret moved, a session bound, the routes read, Settings applied, a folder's favourite branches) passes over one not
  built (`structure?.`, `endpoints?.`, `terminal?.`, `gitView?.`); what only
  looks among the views made (which window holds the keys) asks `IsBuilt` first. A window the IDE writes to and reads
  as it runs keeps that apart from its view, which builds from it as first shown (#441): Problems' lists are a
  `ProblemsModel` (`ToolViews.Problems`: every build's, server's, binding's and container's problems, the tab shown
  and the row chosen; its view `ProblemsWindow`). The Build, Run and Debug consoles are made up front, their costly
  part made as first shown: an `OutputView`'s editor (`HasEditor`, over the text and colours it kept), the Unit Tests
  window's details, a `MergedLog`'s Find. Still made up front: the Debug window (each session's tab), Aspire's view,
  made again at each session bind (`BindAspire`: its resources, selection, consoles and telemetry are `AspireView`'s
  own, which the Aspire actions' checks read, so a lazy one needs it split first), the profiler's (each profile's
  start, run and end) and the Live Visual Tree's (its controller reads its view).
- `src/Passenger.App/State`: the IDE's own state, in one `StateStore` under `PASSENGER_SETTINGS` or the user's local
  application data, kept only by the person's window, shared by every window of the process that keeps state there
  (`StateStore.Open`), with `layout.json` only the default for a folder with no layout of its own: the app's documents at its top (`settings.json`, `layout.json`,
  `recentSolutions.json`, `updates.json`, `find.json`, `refactorings.json`, `containers.json`)
  and each folder's under `workspaces/<name>-<hash>/` (editors and recent files, layout, breakpoints, bookmarks, run
  configurations, Find in Files' scope, each project's last publish profile (`publish.json`), `localHistory/`, the
  profiler's `snapshots/`), with the debugger's decompiled
  symbols in `cache/symbols/` at the root. A Passenger that Run, Debug or
  Profile launch (Passenger.App, or any program named Passenger) keeps its state in the launcher's `sandbox/`
  (`LaunchPlan.SandboxedState`, as IntelliJ's DevKit sandbox), unless its configuration sets `PASSENGER_SETTINGS`. A
  `StateDocument` is a record that is an `IStateDocument`: its `Upgrades` from each older version and its `Clamped` value;
  it is written debounced off the UI thread through a temporary file, merged over the file so values it does not know
  stay (and an entry another window removed stays removed; a list its `Sets` names merges as what each side added and
  took away), and a file that does not read is set aside as `.bad`, logged and said once in a window's status bar
  (`StateFile.TakeUnsaid`). A folder's open reads the documents its restore takes in off the UI thread first
  (`ProjectState.ReadAsync`, through `ValueAsync`), as the folder left ends, and begins its session once they land
  (`OpenFolder`, at once for documents read already), so a person's window opens a folder in that order with no read on
  its thread; a test that acts on a keeping window's folder right after `OpenFolder` settles first. New state is a
  document there, read ahead with the rest when the open restores it, never a file of its own.
- Logs: one folder, `logs/` under the state root, written by `IdeLog` off a queue with every file rotated by size:
  `passenger.log` (IDE errors, the language server's warnings and errors, each folder's startup phases with times),
  `roslyn/` (the server's own logs), `lsp.log`, `dap.log`, `vstest.log` and `mcp.log`. Settings | Diagnostics turns on protocol
  traces (`TraceProtocols`) and a line for every program started (`LogEveryProgram`; otherwise only failing or slow
  ones, each command line's secrets masked, `ChildProcesses.Shown`); Help | Show Log opens the folder. What the language server says to show reaches the status bar. A rotation
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
  A package version's dependencies are `IPackageFeed.DependenciesAsync` (a v3 feed's nuspec in its flat container, else its
  registration's catalog entry; a v2 feed's Dependencies field; a folder's nuspec), by framework (`PackageDependencies`).
  `NuGetConfigEditor` changes one nuget.config in place, every other character kept;
  `PackageManager` changes projects through `dotnet add`/`remove package`. `ToolWindows/NuGetView` is the window, its
  Packages and Sources tabs, and every package change's one path (`InstallAsync`/`RemoveAsync`: one at a time, under
  `Changing`, answering a `PackageOutcome`), which MCP's `nuget_install` (with `remove` too) takes. A run's NuGet is its own on macOS and Linux (DOTNET_CLI_HOME under the run's folder).
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
  redirect is followed by hand: to another origin (scheme, host or port) without the request's headers, and not when it
  would carry the body there, either said in the answer's notes. A body is read to `MostRead` and shown to `MostShown`, a request's
  variables filled in to `MostFilledIn`; a row's last outcome is kept by its name, else its method and URL. Tests send
  only to `EndpointsTests`' stand-ins on loopback (its `localhost` name the other host, a second stand-in the other port),
  and `WebRoutesTests`' shop on loopback.
  The window also lists each web project's routes under it (#417), read from code by the extension's `WebRoutes`
  (`Passenger.Roslyn.Extension/Routes.cs`, `RouteReading`: no build, no running app): minimal APIs (`MapGet` and the rest,
  `MapMethods`, `Map`; a `MapGroup` prefix followed through a local assigned once, a convention that gives back its
  builder, and every caller of the method that maps them when all give one prefix, else the route left out, never
  guessed), controllers (`[Route]` with `[controller]`/`[action]`/`[area]`, `[Http*]` and `[AcceptVerbs]`, an `Async`
  suffix dropped, conventional routes from `MapControllerRoute` and the default for those with no route of their own),
  Razor Pages (`@page` under Pages or an area's, Index its folder's, a code-behind's `On{Verb}{Name}` handlers, a named
  one as `handler=Name`), Blazor's `@page` and `@attribute [Route]` (both read from the server's additional documents),
  SignalR's `MapHub<T>` (its methods) and gRPC's `MapGrpcService<T>` (each override a POST to `/package.Service/Method`,
  the name from the generated base's `__ServiceName`, else the project's one .proto declaring it). Each `WebRoute` has its
  methods, template, parameters with their constraints and source (route, query, body, header, form, services, as
  ASP.NET Core binds them), handler and its place, the body's type and a JSON body shaped from it (`JsonSample`), and its
  file's `TextHash`. `ProjectSession.Routes` (`RouteCatalog`) keeps them by project; `MainWindow.Routes.cs` reads them
  through `BackgroundWork` once the server is ready, every project's again as it loads more (`CSharpService.ProjectsLoaded`)
  and a project's whose .cs, .razor, .cshtml or .proto changes (typed, or the watcher's), only while the window shows or
  such a file is open. Each route's line in an open file whose
  text is the one it was read from has an icon (`OpenDocument.RouteMarks`, drawn as the run icons, `RunMark`; a key takes
  the file's icons away until the routes are read again, the text hashed then, never at each key) whose menu
  sends it: the first request of the project's .http files that calls it (`RouteRequests.Calls`: a method it takes and a
  path that fits its template, after a leading `{{variable}}` or host), else an entry `RouteRequests.Entry` writes at the
  end of `<Project>.http` beside the project (its `@<Project>_HostAddress` from the first launch profile, a variable per
  route value and query parameter, each query parameter in the URL as its `{{variable}}`, the JSON body; one undo step),
  sent by `EndpointsView.TestAsync` to the running app's address (`RunningAddressAsync`: Run's program, else Debug's, by
  `ASPNETCORE_URLS`; else the project's .NET service in the container run under way, by the port the engine published for
  its 8080, `ContainerRun.ServiceAddressAsync` through `ContainerPort.LinkOn`; else its project resource's URL in a
  running AppHost, HTTP's first), else the file's; a gRPC method has no icon. Go to Declaration in an .http file goes to
  the routes its request calls, Find Usages on a route's declaration lists the requests that call it
  (`CodeAssist.Routes.cs`), and an agent's `workspace` with `routes` lists them (`RouteInfo`). `WebRoutesTests`' gRPC
  service restores Grpc.AspNetCore and Google.Protobuf at the versions tests/AspireGraphs' AppHost puts in the package
  cache (Aspire's dashboard client brings them).
  The Requests window (#418, `RequestsView`, `MainWindow.Requests.cs`, `ToolWindowIds.Requests` at the bottom right, beside
  Run) lists the live requests of the web apps running from the folder and the database commands each issued, with no change
  to the apps: Run's program, Profile's, each live Debug tab's on this machine (once `DebugSession.ProcessStarted` says it
  started) and each started .NET service of a run of services under way (`ServiceRun.ProgramAsync`: an AppHost's on this
  machine, a container's through Passenger's tools there), all at once, one program once (`RequestTargetsAsync`), in passes
  a change during one runs again. `Passenger.Profiler/LiveRequests` starts an EventPipe session at a program's own diagnostic
  port (in a container, the tools' `trace --requests`, `InContainer.TraceAsync`, its stream on their output, stopped by
  closing their input) asking the DiagnosticSource bridge (`Microsoft-Diagnostics-DiagnosticSource`, its
  `FilterAndPayloadSpecs`) for ASP.NET Core's `HttpRequestIn` Start and Stop and unhandled exceptions (each with
  `HttpContext.TraceIdentifier`) and EF Core's `CommandExecuted`, `CommandError` and `DataReaderDisposing`, each with
  `*Activity.RootId`, which ties a command to its request; `Microsoft-Extensions-Logging`'s `FormattedMessage` for EF Core's
  log of each command (20101, the app's own logging, tied by its `ActivityTraceId`), which names its parameters; and the
  runtime's counters once a second, which keep the stream's reader handing on what it holds. Only while someone looks: the
  window shows, or an agent asked (`output`'s `requests`, `RequestsForAgentAsync`, until another folder opens); it follows
  from the moment it shows or a program starts, and lets go as it hides or the program ends. `RequestLog` takes the events
  on the UI thread a batch at a time (500 requests kept, each with the program that served it) and tells what the batch
  changed (`RequestChanges`: the requests that came, changed and went), which alone the list puts in, draws again in place
  or takes out, every other row keeping its control; it marks a command its request ran `LiveCommand.RepeatedAt` times or
  more (counted by its text, `LiveRequest.ByText`) as repeated (the request as N+1) and one over `SlowMilliseconds` as slow (a
  non-query with the rows it changed, a query with its reader's reads, EF Core's ReadCount), gives each command the
  parameters its log names (a log matched to its command by the command's text trimmed, as EF logs it, SaveChanges' ending
  in a line break; `CommandParameters`: each value as `ContainerSecrets.Shown` shows a value of that name, or as a
  secret's where the column the command's text has it fill or meet is named as one (`CommandParameters.Columns`: an
  INSERT's column list against each row of its VALUES, SQL Server's MERGE source's columns, a SET's or a WHERE's
  `column = @p` either way round), since EF names an INSERT's and an UPDATE's parameters `@p0`, `@p1`…; a secret's as dots
  with its size left out, '?' where the app does not log values; a log heard before its command kept masked; the row's
  tooltip, the command's tab and an agent's `output` all show these), and each request
  the route it reached in its program's project (`RouteRequests.Fits`, a literal template before one with parameters,
  looked for again as routes are read, `Reroute`). A request opens where its exception was thrown in the folder's code,
  else at its route's handler; a command's text opens read-only, its parameters as a comment over it, from a copy under the
  state's `cache/requests/` (`RequestLog.IsCommandCopy`, `MainWindow.OpenSql`, as an EF Core query's SQL the debugger read
  opens, its bar saying so: `OpenDocument.Origin`). `LiveRequestsTests` run a web app on EF Core and SQLite
  (tests/EfGraphs' packages, `ShopApp.Version`, its sensitive data logging on) and send it requests from loopback (a
  sign-up writes, changes and looks up a customer's Password column, its values dots in the row's tooltip, the command's
  tab and, in `McpRequestsTests`, the agent's answer), and hold
  a request heard with 500 listed to what one costs the UI thread with 5 (the app's events stood in for by the profiler's);
  `LiveRequestsDebuggingTests` (Alone) debug it, `McpRequestsTests` have an agent read it, and `ContainerServicesTests`
  follow a Compose run's services in their containers.
- A run of several .NET services (#404) is one model, `Run/ServiceRun.cs`, which a container run (`ContainerRun`) and an
  Aspire AppHost's run (`AspireAppRun`) both are: it lists its .NET services (`IRunService`: name, project, whether it
  starts) and keeps for each what it is held at its start for (`HeldFor`, `HoldAsync`: the debugger, quietly for Hot
  Reload, or unheld), the sessions that reach it from here (`Debugged`, by `SessionName`; `AttachedRunning`) and the one
  the profiler records (`Profiled`, `EndProfiling`); `EndSessionsAsync` is Stop's order, written once, every session and
  recording ended before the programs go (a recording that cannot end logged, never Passenger's error), which every stop of
  a service or a run takes: a container run's Stop, an AppHost's (`StopAppAsync`), one service's (`StopResourceAsync`, as
  DCP stops it), a restart, a program's own end (`Finished`) and the folder's end (`CloseAsync`, its `closing`: each session
  closed, the recording the folder's profiler's to let go); a kind ends a service's session its own way (`EndSessionAsync`:
  by name, or an Aspire start's own). The profiler records a service of either kind one way, held at its start by its run's
  Profile or as it runs (the window's and an agent's Profile of a service, Attach Profiler with one to profile):
  `ProfilerCommands.RecordServiceAsync`, given the program `ProgramAsync` found through `BuildRunParts.ProfileService`,
  which sets `Profiled` and `EndProfiling`, so Stop, a restart and Rerun read its snapshot first. What is done to a service is written once over it (`BuildRunController.Services.cs`,
  each action given its run): the held start's attach (`AttachAllAsync` as a container run comes up, `AttachStartAsync` as
  DCP starts an Aspire service), Debug, Attach, Run without Debugger, Restart and Profile (`DebugServiceAsync`,
  `AttachServiceAsync`, `RunServiceAsync`, `RestartServiceAsync`, `ProfileServiceAsync`, under `OnServiceAsync`), the
  person's choice kept by configuration for the run's restarts and a run that brings it up again (`chosenDebugged`; an
  AppHost's first start of a service asks it, `AspireController.Chosen`), a restart's attach again and a service held that
  no debugger reached made anew unheld (`RestartServicesAsync`, which says a failure its kind says, a command refused or a
  time limit, as the restart's refusal in words, whoever asked), and the restart for an edit Hot Reload cannot apply, of
  only the services whose project holds it, in the run whose sessions the edit names (`RestartServicesForEditAsync`). The
  window's service rows (the Containers window's and the Aspire window's), the gutter's menus and an agent's `resource` (an
  Aspire resource, or the container run's compose service, on run, debug_start, debug_attach, profile and stop:
  `PassengerTools.Services.cs`; with no run under way that has a compose service, run, debug_start and profile start the
  configuration that has it as the gutter's icon does, `MainWindow.RunTarget`) all reach those. A kind supplies only what is its own: how its services restart
  (`RestartAsync`, built between their stop and start where `build` asks it of a kind that builds then), how a hold chosen
  takes effect at their next start (`HoldsChangedAsync`), where a service's program is for the debugger and the profiler
  (`ProgramAsync`, a `ServiceProgram`: its id there, the adapter's `AdapterPlace`, the profiler's `ProfilePlace`, its
  inspected app; `ServiceUnreachedException` saying why not), whether an edit's restart builds here first
  (`BuildsBeforeRestart`), which failures are its own to say (`Refused`), its sessions' names and group (`SessionName`,
  `Group`), whether each start of a held service is attached to by the start itself, once, a restart then waiting for that
  attach rather than attaching again (`AttachesItsStarts`: an Aspire service's, whose start DCP asks for), whether Run
  without Debugger restarts it unheld rather than letting its session go (`RestartsToRunWithoutDebugger`, Aspire's; only
  one held or debugged, one running unheld that nothing debugs left as it is, said), and what an attach tells it
  (`Attached`: an Aspire start keeps its tab and session). A held start the debugger cannot reach runs on unheld, said, in
  either kind (a container run's service made anew unheld; an Aspire start let go at its diagnostic port, its runtime's
  diagnostics off said as `ServiceProgram.WhyNot`), never left waiting at its start. Both kinds share too how a run is
  started (`RunStart`: Run, Debug, Run with Hot Reload, Profile), its certificate (`BuildRunController.Certificate.cs`) and
  one start at a time of what it starts (`TakeStartAsync`, a `StartUnderWay` in `ProjectSession.Starting`, which the
  folder's end ends): a start of a container configuration, or of an AppHost a start under way also starts, ends that one
  first, as Stop does (Debug pressed as Run brings it up), and goes on once it has. Debug or Run of a configuration that
  runs ends that run and starts it again, an AppHost's (`RunAspireAsync`, its group stopped first) as a container run's and a
  plain program's in the Run console (`TakeConsoleAsync`); "Already running" is said only of what shares the Run console,
  naming it (`AlreadyRunning`), an AppHost running beside anything; each start is under its own action's work (Debug's,
  Profile's, Run's) and says itself in a container run's words (`AspireStarted`: "Debugging X: Y under the debugger"); and a
  start of one resource (the AppHost gutter's, an agent's `resource`) keeps its configuration's Don't start, setting only
  its target's entry (`MainWindow.Targeting`). Rerun follows the configuration chosen
  (`BuildRunController.RerunAsync`): its AppHost's when it runs (what changed restarted), else its container run in fast mode
  (the services whose build changed restarted), else what the Run console has stopped and the chosen run again, another
  configuration's AppHost running on; what changed is read by one stamp of a build's output (`BuiltInputs.Stamp`, a file
  gone as it is read left out) and `BuiltInputs.ChangedSince`, and a refusal is said; what came of it
  (`BuildRunController.Reran`) and a start or restart Stop ended (`StoppedStarting`, `StoppedRestarting`) are worded once
  for every kind.
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
  (`MainWindow.Containers.cs`) has the Aspire window's layout (`ToolWindows/RunWindows`: its toolbar's Refresh, then the
  service actions' group, then a container's own; a service's or container's row's menu beginning with the service
  actions, every item greyed with why; its tabs Log, Stats, Files, Details and Telemetry in the shared order, Log at
  first), asks the engine only while shown, its lists again as events say, its rows drawn as every
  list of what runs draws them (`ToolWindows/RunRows`, the Aspire window's too: a chevron, the state's dot, red for a
  failure in both, a container's one restarting after each crash, dead, or exited with an error, a stop's 137 and 143
  aside, `ContainerInfo.IsFailed`; the health's
  tag, `ContainerDetailsPane.HealthTag`, addresses as links; kept by their keys as a list is read again, `RunRows.Put`, each
  row's control shown anew in place only when what it stands for differs (`RunItem.Take`: an AppHost's update keeps every
  resource it did not change, as the Aspire window's Details does), `RunRowView`, never the list made anew, whose controls a click's press and release
  would straddle while the engine's events or an AppHost's updates stream, the click then lost), a port's link on the
  engine's own machine (the host of a `tcp://` or `ssh://` endpoint, else localhost, `ContainerPort.LinkOn`); a
  container's Log (`docker logs -f` through `ProcessRunner` into an `OutputView`, its links the
  consoles'; a stopped container's kept as it was read, its scroll and Find with it, until one of its containers runs
  again), a project's merged one (a toggle a service), an AppHost row's its containers' (a toggle a container, a `docker logs -f` each under its name, `ContainerLogPane.Merge`, with their Stats), each the merged log every such console is (`ToolWindows/MergedLog`,
  the Aspire window's too) with the editor's `FindBar` over it (Find's keys while
  the window has them; an earlier run's lines are not folded yet: the console has no folds), Stats (`docker stats`,
  streamed only while its tab shows, its errors read as they come and the last said in its place once it ends, each
  reading changing its container's line in place, the table laid out again only as a container comes),
  Details (`ContainerDetailsPane`, a service's its container's: `docker inspect` read
  as it is chosen and as the window reads the engine again, in the framed tables every Details tab has, one column of
  names (`ToolWindows/DetailsTables`, the Aspire window's too; tables shown again that show what they showed stay,
  `DetailsTables.Show`, so a press on a link in them lands): state, restarts and
  last exit, the health check's last outputs, ports as links, Compose's `com.docker.compose.depends_on`, environment,
  mounts, networks) and Files (`ls` through `docker exec` as a folder opens, a file copied by `docker cp
  -L …:path -` under the state's `cache/containers/`, which `OpenDocument.IsReadOnly` takes as read-only, as Inspect's JSON
  there; what a container says is never trusted: a listed name with a slash, `.` or `..` is no file's and is dropped, a
  copy's place is refused unless it is under the container's folder of the cache (links resolved), the tar is read as it
  comes and a file over 16 MB refused at its header, the copy written beside its place and renamed over it, and a copy
  opened again, as Inspect's is, takes its new text in the tab it has, `MainWindow.OpenCopy`). A container's secrets
  (`ContainerSecrets`, Passenger's one rule and mask for secrets, which a compose file's hints follow too: a variable
  `ContainerSecrets.Secret` names one, a URL's password, a connection string's part so named) show masked in Details and
  in Inspect's JSON, whose copy is written masked; a copy an earlier Passenger wrote unmasked (one its masking would
  change) is deleted off the UI thread once a process for each state folder, as the window starts and at its Containers
  window's first use, a tab showing it closed and the status bar saying why (`ContainerSecrets.ForgetUnmasked`). What a
  CLI says of a failure is shown and logged through one helper, `ContainerSecrets.InSaid` (`Masking` for a stream), the
  docker CLI's and Compose's (`ContainerEngine.Said`, ComposeCheck's error, a run's line of what failed and Compose's own
  output in the Run console, `ConsoleSink.PumpAsync`'s `shown`, a whole line at a time) as the aspire CLI's
  (`AspireClient.SanitizeDiagnostic`), and so is every command line Passenger logs or echoes (`ContainerSecrets.CommandLine`,
  through `ChildProcesses.Shown`): each value it knows to be a secret's (given, this process's environment's
  secret-named variables', the .env's in the folder Compose ran in, which it interpolates and echoes: `parsing
  "••••••"`; four characters at least) as the dots wherever it stands, then on each line a `NAME=value` or `"NAME": value`
  whose name is a secret's, an Authorization header, a URL's credentials and a connection string's part by the rule. Open Shell is `docker exec -it … sh` in a Terminal tab, an image with no shell said. Prune and a volume's
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
  reading of block YAML, finds), else on the first line, or listed as the other file's; a name a service uses that the
  file does not declare (`ComposeFault`: a volume, network, secret or config, or a service depended on through
  depends_on, links, volumes_from or network_mode) is marked on the name where it is written, with Passenger's own fixes
  there (`ComposeFixes`, through `EngineChecks.Fixes` and `CodeAssist.EditorFixes`, first in Show Context Actions and an
  agent's code_actions): Declare X at the top level (under its key, made at the file's end when it has none, as the file
  indents; a secret or config read from `./X.txt`), Remove X from depends_on, Change to Y for a declared name an edit or
  two away; each one edit, worked out again on the text as it stands when chosen (`EditorFix.Again`) and refused, said in
  the status bar, once it no longer fits. A .NET service (its build's Dockerfile building one of the solution's projects,
  `ComposeDeclarations.Built`) whose project reads a connection string its environment never sets, as Compose's model has
  it (environment, env_file, what it interpolated; `ConnectionStrings__name` or with a colon, letter case aside, set to
  something) and its own settings never give either (appsettings.json, or appsettings.{env}.json for the service's
  ASPNETCORE_ENVIRONMENT or DOTNET_ENVIRONMENT, Development when unset; its build carries them into the container) is
  warned of on the service's name (`ComposeConnections`); one they give at localhost, 127.0.0.1 or ::1
  (`BackingService.LocalHost`, in `Reached`'s shapes), which in the container is the container itself, more softly
  (`Finding.Weak`, an Info marker), saying what to do; one they give elsewhere, not at all. What the project reads is asked of the Roslyn
  extension (`Handlers.ConnectionReads`: Aspire's one reading, `AspireSource.ConnectionUses`, by each project's version
  once, through `ComposeCode`, the folder's projects and its loaded language server, taken as a check starts; the open
  compose files checked again as the server loads, a project is evaluated or a C# file is written,
  `EngineChecks.CodeChanged`). The fix is Set ConnectionStrings__name in X's environment (`ComposeFixes.SetVariable`, one
  edit as the environment is written, made under the service when it has none, worked out again as chosen): to the
  connection string of the backing service the file runs that it reaches (`BackingService.OfImage`, the service of the
  name read, else the one of the kind its settings' value, name or packages point to, `ConnectionString`'s
  `host`), else, for one never given, a value left for the person to fill in (a localhost one of a kind the file does
  not run gets no fix). Each such warning is also a `ComposeGap` (`EngineChecks.Gaps`, with the files the same
  ConnectionReads answer has read it in), and while the compose file is open the service's code is marked where it reads
  it, with the compose file's warning (`CSharpDocument.Compose.cs`: the places asked of the same handler in the file's own
  pull, so they are the text's as it stands, pulled again as `GapsChanged`), its fix the compose file's, made there as one
  edit (`CodeAssist.ComposeReadFixes`, `SetInCompose`, worked out again as chosen). Each `${VAR}` gets a hint of
  what Compose's own interpolation makes of it (asked as an `x-passenger-values` list in the folder, never worked out
  here), and a base file's scalar its override changes the value Compose ends with, where the base's own model has the
  scalar written (a value it normalizes gets none). A secret's value shows as the same dots whatever its length: one
  whose variable, the key or list item's `KEY=` it is given to, or the name just before it (`Password=`) is a secret's
  (`ContainerSecrets.Secret`: PASSWORD, PASSWD, SECRET, TOKEN, CREDENTIAL or CONNECTIONSTRING anywhere in it, `_`, `-`, `.`
  and case aside; or a word of it, split there and at case turns, PASS, PW, PWD or KEY, or its plural, or ending in KEY
  but for an ordinary word's, as MONKEY's; never the shell's own `PWD` and `OLDPWD`), or that stands as a URL's password
  (`postgres://u:${PGPASS}@db`); a value holding such a URL shows it with its password as the dots, and one holding a
  connection string its part so named (`ContainerSecrets.InValue`). Their problems are Problems'
  Containers tab by file
  (`ShowContainerFiles`); with no engine or no buildx nothing is shown or said per file. A check costs well under a
  second (buildx's 0.1–0.6 s, a base image's metadata read from its registry the first time; Compose's about 50 ms a
  run).
  Its tests (`ContainersTests`, `ContainerDetailsTests`, `McpContainersTests`, `EngineChecksTests`; not
  `ContainerConfigurationsTests` nor `ContainerDetailsTests`' test of Inspect's unmasked copies, which need no engine,
  and `EngineChecksTests`' agents' fixes, which need Node.js too; its connection strings' tests restore a web project for
  the language server) skip saying why unless `docker info`'s
  OSType is linux (`EngineChecksTests`' Dockerfile one without buildx too; its stopped engine is `StandInCli`'s), use
  busybox and Compose files they write, and never prune or start or stop Docker Desktop: an engine that stops and starts
  again, or has no Compose v2, is `ContainersTests.StandInEngine`'s, a shell script (so skipped on Windows) whose `state`
  file says up or down; stand-in container tools are a script copied to `ContainerTools.Copied` in a busybox container. `DockerFilesTests` check the
  Adds' files without an engine, and where a Linux one runs also have Compose read the compose.yaml written and build the
  Dockerfile Add Docker Support writes for a console app, only where the engine already holds the SDK and runtime images
  (no test pulls the SDK's 800 MB; it skips saying which to pull), its image labelled with the run's label and removed.
  Container run configurations (#343): a `RunConfiguration`'s `Kind` (Project, Compose, Dockerfile, SdkContainer), made as
  the folder opens by `ContainerConfigurations` (compose files beside the folder, the solution or a project, overrides with
  them; a .dcproj's properties and DockerCompose profiles, read as XML; a runnable project's Dockerfile, its evaluated
  DockerfileFile else beside it; a project that asks the SDK for its container, `AsksForSdkContainer`: EnableSdkContainerSupport
  evaluates true, which the SDK makes it for every executable, and its own files set it or ContainerRepository,
  ContainerImageName or ContainerBaseImage; a Docker or SdkContainer launch profile is its project's), a project's
  properties always its evaluation's (`ProjectModel.Property`, `OwnProperty`), never its text's. `ContainerPlans.ResolveAsync`
  reads Compose's own model (`ComposeModel`, `config --format json`; a Dockerfile or SDK container configuration is a
  compose file of one service Passenger writes) and the project each service runs (its Dockerfile's ENTRYPOINT assembly
  or dotnet publish, `ComposeDeclarations.Built`, else the configuration's choice, else the .dcproj's), and every command
  takes Passenger's override from the folder's state (`workspaces/…/containers/<config>.override.yaml`, JSON, written
  after the host's build, the person's alone to read on macOS and Linux from the moment it is made, as a single service's
  compose file is (`OwnFiles`), since it holds the configuration's environment): labels on
  every service, image build and network;
  in fast mode each .NET service it starts its runtime
  stage (`DockerfileStages.FastStage`), `dotnet /passenger/app/X.dll` as PID 1 (`init`) over the build's output mounted
  read-only, its static web assets' folders mounted and its manifest rewritten (`ASPNETCORE_STATICWEBASSETS`); in images
  mode BUILD_CONFIGURATION=Debug where the Dockerfile declares it, every image it builds tagged `<project>-<service>:passenger`
  (fast mode's `:passenger-fast`), never the name its files give, which stays the person's; on Linux a .NET service runs as
  the person (`user: uid:gid`), in fast mode always and as deployed where it takes the certificate or user secrets (said
  after the status), since the engine keeps the host's owners and modes and those are the person's alone; the dev certificate (`DevelopmentCertificate`) for a
  service that serves HTTPS (`ContainerPlan.Https`; `BuildRunController.CertificateAsync`, every HTTPS run's one path, an
  Aspire AppHost's too, `BuildRunController.Certificate.cs`, before the override: `dotnet
  dev-certs https --check --trust`, and missing or not trusted the person asked once, `CertificateQuestionDialog`, Create
  and Trust running `--trust` or Run over HTTP; a run an agent's call started (`Mcp.AgentCall.Current`) or an agent's own
  IDE (`MainWindow.AgentsOwn`, `--mcp`'s, not `--no-mcp`'s) asks no one and runs over HTTP, saying so in the status bar and
  the call's answer (`ContainerRun.Notes`); a start stopped closes the question; then exported, again once the check names
  another certificate than the one exported (its thumbprint kept beside it), as PEM with its key unencrypted (`-ep
  aspnetapp.pem --format Pem --no-password`, the key `aspnetapp.key` beside it: dev-certs takes a password only on its
  command line, #442) into a folder only the person enters, made so before dev-certs writes there, its files theirs alone
  to read (`OwnFiles`), an earlier Passenger's .pfx and password deleted there, the run's `ContainerRun.Certificate`,
  mounted read-only at `/https` and given to Kestrel as `ASPNETCORE_Kestrel__Certificates__Default__Path` and `__KeyPath`
  (an Aspire AppHost's run exports nothing, its services on this machine reading its store); with none, `OverHttp` turns its https addresses and HTTPS ports to HTTP, and a
  failure is said in the status bar, a click from the Run window's line of what dev-certs said; the wait for the answer
  holds no `BackgroundWork`; `DotnetWords` is the dotnet it runs through, a test's stand-in by `MainWindow.DevCertificates`)
  and in Development the project's own user secrets (its UserSecretsId's folder alone, mounted at
  `/passenger/appdata/Microsoft/UserSecrets/<id>` with APPDATA set to `/passenger/appdata`, which .NET reads before HOME on
  every system, so any user the app runs as finds them; the folder is `PASSENGER_USER_SECRETS`'s in place of the person's
  when set, read for that alone, as the tests' run sets it); a fast .NET service's ports the file leaves to the engine
  chosen free once for the plan (`Pinned`, the same at every override written after: a hold, a restart) and put in the
  file's place in Compose's long syntax, its host address, mode, name and application protocol kept (`!override` on
  JSON's flow list, Compose 2.24 on), since a
  restart otherwise publishes another. A configuration whose every service is marked Don't start runs nothing, saying so
  (`up` given none would start them all); a .NET service marked Don't start that a started one depends on is left out of
  the override's .NET part, Compose starting it from its own image, which the run says (`ContainerPlan.Notes`). A .NET
  service marked Don't start is started later in its run, as an AppHost's resource is (`ServiceRun.LeftUnstarted`,
  `StartLater`; `BuildRunController.StartLaterAsync`): the Containers window lists it under its project, no container yet,
  as Not started (`ContainersView.UnstartedOf`, `ContainerRow.LeftUnstarted`, every container action greyed saying so but
  Start), and its Debug, Run without Debugger, Profile and Start (the window's rows, the gutter, an agent's `run`,
  `debug_start` or `profile` naming it as `resource`) start it held as chosen (for the debugger, unheld, or once for the
  profiler, `ContainerRun.heldOnce`), Attach refusing: its project built here first in fast mode (fast mode's refusal of
  it, `DotnetService.Unfit`, kept from the plan until then), the person's own containers of it asked about as `up`'s are,
  then the plan runs it from Passenger's build (`ContainerPlan.StartingLater`: the override's `Writer` given the plan's
  .NET services, so its stage, entrypoint, mounts, certificate and hold are the others') and the run's restart makes it
  (`up --detach --no-deps --force-recreate`, its image built first as deployed by `--build`, its dependencies left as the
  run has them), attached to where held as a restart attaches. From then on it is the run's: Stop takes it down, Rerun
  builds and restarts it, and a Rerun run whole or a start of its configuration that ends its run starts it again
  (`ContainerRun.StartedLater`, carried by `StartContainersAsync`). Before `up`,
  containers of its project of the services it makes (`ContainerPlan.Touched`: those it starts and what they depend on)
  that lack this process's labels are the person's own, which `up` would make again and Stop then remove: the person is
  asked (`ReplaceContainersDialog`: Replace Them, after which they are the run's, or Cancel), and a run an agent's call
  started, or an agent's own IDE, refuses saying why. An SDK container as deployed is the SDK's own image (`dotnet publish
  -t:PublishContainer` into the engine, `ContainerRun.PublishAsync`, named `passenger-<config>-<folder>:passenger`, which
  carries no label of Passenger's). `ContainerRun` (`ProjectSession.ContainerRun`) is `up
  --detach --wait` streamed into the Run console, then `logs --follow` through the session's `Runner` (so Run's Stop,
  `IsRunning` and its end are the log's; a log that ends with the run not stopped is followed again from its end,
  `logs --since`, said in the console, while its containers still run once the restarts under way have ended,
  `ContainerRun.StillUpAsync`, `BuildRunController.FollowContainersAgainAsync`, Stop kept meanwhile), Compose's own watch for other services that declare one, Rerun's restart of the
  services whose build changed (`Changed`, by the output's stamps, a restart stamping only those it restarted), and
  `StopAsync` (`FolderContainers.TakeDownAsync`: the containers and networks of its project carrying this machine's, process's and
  folder's labels, the .NET services' alone when backing services are kept, which then go with the folder; never `down` or
  `--remove-orphans`, which would take the person's own of the project). A service's name follows `--` in every command.
  `ContainerPaths` maps an image's paths (its Dockerfile's COPY of the context into WORKDIR, and `/_/`) to the workspace's
  and back (a container's path never to a file outside the folder it maps to: a `..`, a `//` or a rooted rest gives
  none), for `ConsoleLinks.Paths` (a Compose log's "web-1  | " prefix passed over) and `BuildOutput.Parse(line, paths)`;
  the debugger takes it at the protocol's edge (`AdapterPlace`, a service's `DotnetService.Paths` in images mode,
  `ContainerPaths.None` in fast mode). A run is started one of three ways (`RunStart`, every run of services' one enum): Run; Debug, its services
  marked Debug (else every .NET service it starts, `ContainerPlan.Debugged`) held at their runtime's start
  (`ContainerPlans.HeldVariable`, DOTNET_DefaultDiagnosticPortSuspend=1, with DOTNET_MODIFIABLE_ASSEMBLIES=debug, in the
  override); and Run with Hot Reload (Settings' Hot Reload when running, or `run`'s `hotReload`), every .NET service held, since
  dotnet watch would need the SDK in the image. `ContainerRun.UpAsync` runs `up --wait` while each held service is attached
  to alongside (`BuildRunController.AttachAllAsync`, each program reached through `ContainerRun.ProgramAsync`: its container from `compose ps -q` once Compose starts it, the tools
  copied in for its image's system, never mounted, since the system is known only once the image is and an engine shares
  only some of this machine's folders; its program from the tools' `processes` by its assembly), so a healthcheck or a
  dependent's `service_healthy` is not waited on before the attach lets it go. Debug gives each service a Debug tab named for
  it, of the run's group (`DebugTab.Group`, the configuration's name: Hot Reload's Apply goes to each tab of the group),
  its console the container's `docker logs --follow --since` its start (`AdapterPlace.Output`); Run with Hot Reload attaches
  quietly (`DebugController.AttachForHotReloadAsync`, `DebugSession.Quiet`: no breakpoint sent, no exception stops it, the
  store never told), Apply Hot Reload Changes and a save applying the edits through it (`ApplyRunningAsync`) whatever else
  is debugged, a session debugged with Hot Reload taking them too (`ApplyAllAsync`). An edit Hot
  Reload cannot apply builds the service's project again (fast mode) and restarts it (`RestartServicesForEditAsync`: only
  the services whose project holds a rude edit, since Roslyn names one in another project rude to each session; images mode
  `up --build --no-deps --force-recreate`), its session ended first and attached again, as Rerun's restart of a held service
  does. A Release build (the tools' `optimized`), a runtime with DOTNET_EnableDiagnostics=0 (not held, and not attached: it
  runs on) and an image of another processor than the engine's are said in the status bar after the run comes up. Stop ends
  the run's sessions (`ServiceRun.EndSessionsAsync`) before the containers go, since a container taken from under its adapter
  would end it as a failure. A start stopped (Stop, the window's Kill, an agent giving up on `run` or `debug_start`,
  `PassengerTools.Started`, an agent's `stop`) ends Compose's `up` and waits for its CLI to go before the take-down looks, which waits for it
  (`ContainerRun.upEnded`) and looks again 2 s later, since the engine can finish a container it was making as `up` ended;
  `StopAsync` cancels the run's own token whoever asks, so its `up`, publish and restarts end with it whatever began them;
  an attach whose container was reached after the Stop, or after its folder was left, starts no session (nor does the
  debugger once disposed). One container start at a time (`ProjectSession.Starting`, a `StartUnderWay`, the model's `TakeStartAsync`): a start
  ends one under way first (Debug pressed as Run comes up) and waits for its end and its run's take-down, a start that fails
  takes down its own run alone (`StopContainersAsync(run)`), and the folder's end ends it, its `up` waited for before the
  folder's containers are taken down (`FolderContainers.EndAsync(upEnded)`); a stop as the certificate is checked, made or
  exported, or as the person is asked about it (the question closed), is the start stopped too. Profile of a container configuration (`RunStart.Profile`, `ProfilerCommands.LaunchInContainersAsync`,
  `BuildRunController.ProfileContainersAsync`) brings it up as Run does, its log in the Run console, with one .NET service held
  at its start (`ContainerPlan.Profiled`: of those marked Debug, else every one it starts, the launched service, else the
  first; the profiler window follows one program), without DOTNET_MODIFIABLE_ASSEMBLIES, which would measure code built for
  changing; the one recording of a service (`ProfilerCommands.RecordServiceAsync`) has the tools' `trace --resume` record it
  there, which lets it go, the profiler window following it from then (a start that fails after takes its containers down
  with Stop's order, its snapshot read). Get Snapshot ends the recording and the containers run on; Stop ends it first
  (`ServiceRun.Profiled`/`EndProfiling`, the snapshot read) so its trace keeps its rundown. Attach Profiler with a container
  run up (and nothing debugged) records that service as it runs, through its run (`ProfileServiceAsync`, the place
  `ContainerRun.ProgramAsync` gives, `ProfilePlaceOf`). The override's held and Hot Reload services are given apart (`ContainerPlan.WriteOverride(held,
  modifiable)`), and the run keeps what each service is held for (`ServiceRun.HeldFor`, `HoldAsync`) and which containers
  were made held: a restart makes one anew (`up --no-deps --force-recreate`) whose hold changed since, so none is held with
  no debugger to let it go; one restarted held that the debugger then cannot reach is made anew unheld, its hold let go and
  said, rather than waiting at its start for good. A .NET service of the run under way is acted on alone, the run left up,
  by the model's actions (above), one set for every kind of run (`MainWindow.ContainerServices.cs`, `AppActions.Service*`:
  Debug Service, Attach to Service, Run Service without Debugger, Profile Service; the ids each window had before still
  name them, a person's shortcuts and an agent's `ide_action` alike, `AppActions.Formerly`), acting on what `AimOf` finds:
  a gutter menu's service (`ActionContext.Service`, a `ChosenService`), else the Containers window's service or container
  row or the Aspire window's resource, the window with the keys, else the one of the two shown, else the one chosen in
  last; greyed with why where they cannot act (no run, a resource that is no .NET project, an AppHost Passenger does not
  run), an AppHost's project that does not run now started by Debug (held for the debugger, as an agent's `debug_start`
  starts it, one its configuration left unstarted too) or Run without Debugger. The gutter's icon beside a .NET service of
  a run that reads its file (the container run's compose file, a running AppHost's `AddProject`, `RunOf`) offers the same
  four under the same titles (`FlyoutEntry` with that context); a
  container's Run without Debugger lets its session go, not restarted, unheld from its next restart, and its Profile is
  Attach Profiler's path in its container (`ProfilerCommands.AttachServiceAsync`). The window's Restart, Stop and Remove of
  such a service, and an agent's `container_action`, go through the run (`MainWindow.ContainerActAsync`): its session and
  recording ended first, Restart attaching again as the run's restart does; so do those of the run's own project
  (`ProjectActionAsync`): Restart ends its sessions, restarts its other services as they are and its .NET ones as the run's
  restart does, Stop and Remove are the run's Stop.
  Edit Configurations' Compose page lists the services from Compose's model
  (`ContainerPlans.ServicesAsync`, `Run/ComposeServicesTable`: each .NET service's project, what Run does with each; its
  Aspire page is the same table, `RunConfigurationsDialog.AspireTable`), and
  the gutter's run icons (`RunMark`, `MainWindow.RunMarks.cs`) stand beside a used compose file's services and a used
  Dockerfile's final FROM, their menu running or debugging that one service (`BuildRunController.RunContainersAsync`). `ContainerRunsTests`
  run them against the engine on aspnet:10.0 and busybox (with a fast-mode run up, a build twice and a configuration's
  plan each end as without one, #433); their HTTPS test serves a certificate made for the test and thrown away, its PEM
  and key written by a dev-certs stand-in whose check answers trusted (the machine's store never read, so nothing asks the
  person), and checks the service serves that one, their certificate question's test answers with a shell stand-in for
  dev-certs that trusts nothing on the machine (`DevCerts`, which AspireStandInTests' and McpAspireTests' HTTPS AppHost
  take too), both hold every command line the run started (`ChildProcesses.Started`) to naming no password, and their SDK container image (unlabelled, the SDK's) is removed by the test that
  made it.
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
  numeric id passed over, an answer past 4 MB refused). `ContainerProcesses` is what Attach to Process lists of the running containers, each inspected
  on its own so one that ends meanwhile leaves the rest listed: the tools' `processes` where they are in, else `docker top` from
  outside (the dotnet host, a .dll, or in a .NET image any program but a shell), their ids there 0 until the tools,
  copied in as the person attaches (the dialog's heading says so first), name them.
  Podman (#403), on rootless Podman 5.7 in the Ubuntu VM, both ways a person sets it up: Docker's CLI and Compose v2 on
  Podman's socket (DOCKER_HOST or a context, as Podman Desktop does), and Podman alone, a `docker` that is Podman's own
  command (podman-docker, or a link ahead on the PATH where it conflicts with Docker's CLI) whose `podman compose` runs
  Docker's Compose v2 as its provider (Python's podman-compose does not serve: said as no Compose). On Docker's CLI,
  Compose builds through buildx, whose builder there is a BuildKit container it starts and keeps (buildx_buildkit_default,
  Compose's and buildx's own doing); Podman's own Compose builds with Buildah through the API. The state knows it
  (`EngineState.IsPodman`: the server's Podman Engine component, or `PodmanCli`, a client that words its system as
  OsArch) and whether it is `Rootless` (info). Podman's own command answers in its own JSON, which every reader takes
  beside Docker's (`ContainerInfo.IdOf`, `First`, `ReadLabels`, `ContainerPort.Read`: ids as Id or id, names and tags as
  lists, labels and ports as objects; events' Status, ID and Attributes at the top), has no server apart in its version
  (the client's is the engine's, its OsArch the processor), no -L on cp (it follows a link itself), stats over several
  lines (asked as a line of Docker's names), its info over many lines (read whole), a start time in Go's words (asked as
  JSON), and its compose needs the API socket (said when it is not running), names its provider on every command
  (`PODMAN_COMPOSE_WARNING_LOGS=false`) and adds a line of its own after the provider's error, which every reader of
  Compose's error passes over (`ContainerEngine.ProviderFailed`).
  Through Docker's CLI, Podman's list words no health and gives no address on a published port, so Docker's CLI shows
  none (both read through inspect, `ContainerSnapshot.InspectedAsync`), and its stats cut each name's first letter (each
  reading named by the id asked). A .NET service run as the person on rootless Podman takes `userns_mode: keep-id`, without
  which its user is a subordinate id that reads none of what is kept for the person (the build under a 0750 home, the
  certificate, user secrets: exit code 155, the dotnet host finding no app). Podman has no BuildKit, and Docker's buildx
  on it would start a BuildKit container of its own: a Dockerfile goes unchecked, said once in the status bar and
  Problems' Containers tab as one opens with the setting on (`EngineChecks.Limited`, `EngineState.Limit`). A stopped
  Podman is said as Podman, its socket or machine to start. Compose's `logs --follow`, the run's log, crashes (Compose
  2.40 and 5.0: a nil State read for a die whose container is gone, as Podman's die comes) when a service is made anew,
  which the run follows again from its end, its containers still running (`ContainerRun` above). So a compose file or a Dockerfile runs, debugs (two services at once, or one service of a run),
  Hot Reloads and profiles on Podman as on Docker: the ten container classes pass both ways (but for Ubuntu's own
  fault below). An AppHost's container resource runs on the same Podman (DCP takes Docker's CLI on its socket, or Podman's
  own `docker` as Podman), and #397's cleanup reads Podman's bare container names; one whose DCP is told Podman beside a
  docker that reaches Docker's engine is followed on Podman, its own command (#412, Aspire below). On Ubuntu 26.04 the pasta AppArmor
  profile refuses podman's SIGTERM, so a container's removal can fail ("rootless netns: kill network process: permission
  denied"), Ubuntu's to fix (`signal (receive) peer=podman` in /etc/apparmor.d/usr.bin.pasta). Not handled: SELinux (Fedora, RHEL), whose bind mounts need
  relabelling (`:z`) or `label=disable`, untested on Ubuntu's AppArmor; rootless Docker, whose user mapping is the same
  with no keep-id. Docker's CLI 29 `docker run` on Podman 5.7's socket makes a container of one process (pids.max 1;
  Compose's get Podman's 2048), so the tests' own runs take `Docker.AnyPids`, their own builds `Docker.Load` (buildx's
  BuildKit container keeps a build in its cache), and they compare an image Passenger tagged by its short name
  (`Docker.Short`: Podman names one built here docker.io/library/…) and a command without Podman's init
  (`Docker.WithoutInit`), read a published port however Compose words its address (":port" there), take the
  container's own log line over Buildah's echo of the CMD it builds, and read the engine's processor from info's
  Host.Arch where version has no server (`ContainerPrograms.Processor`). Every `docker rm -f` waits Podman's 10 s for a
  container that ignores SIGTERM, where Docker kills at once.
- `src/Passenger.App/Aspire` (#338): Aspire's AppHosts run with Passenger as their IDE. `AspireController`
  (`ProjectSession.Aspire`) owns the folder's AppHosts and the project resources their orchestrator asks the IDE to start,
  through `AspireExecutionServer`, Aspire's IDE execution protocol (2024-03-03) on loopback: each orchestrator its own
  bearer token, kept hashed and compared in constant time and checked before anything is admitted, one ordinary request a
  connection (`AspireExecutionConnection`) with ten seconds for its headers, body and writes, its notifications over
  WebSockets, and a create given up by its connection cancelled on its own; different resources' creates run at once, a
  held start's attach included, one resource's (by the OTEL_SERVICE_NAME DCP tells it) creates and deletes in the order DCP
  sent them, and a session's delete waits for its own create alone (#436), its instance's end for every one under way. An AppHost is a project whose evaluation sets
  `IsAspireHost` (`ProjectModel.IsAspireHost`, carried by the host's `evaluate`; an older `projectModels.json` forgotten), or a
  single-file `apphost.cs` (a C# file whose head names `Aspire.AppHost.Sdk`, `AspireAppHosts.IsSingleFile`), found among the
  files of the folder's one walk (`FileIndex.Walked`, `ProjectSession.AspireFiles`; none under a hidden folder), each C#
  file's head read as a walk first lists it and again only as the watcher says it changed, each a run configuration
  (`RunConfiguration.Aspire`, `LinkedAppHosts`). An AppHost whose launch profile serves HTTPS on this machine
  (`AspireController.ServingHttps`) has the development certificate checked once before it starts, through the container
  runs' path (`BuildRunController.CertificateAsync`, the person asked by `CertificateQuestionDialog`, nobody for an agent;
  no export, what runs here taking the store's own); over HTTP (`HttpsChoice.OverHttp`, `AspireAppRun.OverHttp`, said in
  the status bar and `AspireAppRun.Notes`, which an agent's `run` answers with) its own addresses serve HTTP, unsecured
  transport allowed, and the services it starts are told HTTP as a container's are (`DevelopmentCertificate.OverHttp`).
  `AspireInteractions` is one
  authenticated connection to an AppHost's resource service (gRPC, `Protocol/dashboard_service.proto`, its C# checked in beside it): its resources,
  their consoles and commands and the AppHost's prompts (`AspireInteractionDialog`, closed with their window or the IDE's quit, still answered, so none holds the window and the AppHost open), the display's streams followed only
  while the Aspire window shows them (`SetWatching`), the prompts' kept while it is hidden, and a wait on what the
  resources become (an agent's `run`, `debug_start` and `debug_attach` with `resource`, a service's restart for a tool)
  following one resource stream for the whole wait (`Follow`, `MainWindow.FollowAspireAsync`: woken by each batch, each
  change of the runs and each service's attach, never on a timer), so `run` with `resource` answers once it is up; `AspireTelemetry` reads the
  dashboard's bounded recent logs and spans and puts traces together by their ids; `AspireClient` is the `aspire` CLI's
  JSON (13.x from 13.6.0), for an AppHost Passenger did not start, its errors said with their secrets masked as the engine's are (`ContainerSecrets.InSaid`). The Aspire
  window (`AspirePane`, `AspireView`, `ToolWindowIds.Aspire`, its stripe icon `Icon.Aspire`) is the Containers window's
  parts over an AppHost's resources, the same controls and one layout (`ToolWindows/RunWindows`, which UiRulesTests holds
  both to): its toolbar the AppHost chosen and icon buttons of registered actions (`MainWindow.AspireActions.cs`, each
  bound by `ActionButtons.Bind`, its ring while its work runs: Discover, Refresh, then the service actions' group as the
  Containers window's toolbar has it after its Refresh, then the resource's commands as a menu, its address, source and
  container), the resources as `RunRows` (a parent's children under its chevron, the state's dot, the health's tag only
  for a resource with health reports, its http and https addresses as links through `Browse`; a row's menu the service
  actions first, as a container's row's menu begins, then its commands, each item greyed with why, `MainWindow.RowMenuItem`),
  and the tabs both windows name alike in one order (`RunWindows.Tabs`), Log first and shown at first: Log, the resources'
  consoles merged with a toggle each and Find (`MergedLog`, a toggle turned or Refresh reading the shown ones again from
  their start; a resource that comes or restarts, another generation, streams on its own after the lines shown while the
  others' streams go on, #435), the chosen one's Details (`AspireDetailsPane`, `DetailsTables`: the resource, its health reports, URLs,
  relationships, environment, properties and volumes), Telemetry, the dashboard's structured logs and traces (the
  Telemetry view's two pages under it, as the Containers window's Telemetry tab has them, below), and AppHost Log, the
  AppHost's own console. What a resource says is masked as a container's
  is, by `ContainerSecrets`' rule (a property the AppHost marks sensitive whatever its name; a health report's exception by
  `InValue`; an external AppHost's properties, whose sensitivity the CLI does not say, only those known to name no
  secret), the rest shown. An AppHost the
  window starts is a run of services (`AspireAppRun`, the model above), its services the IDE runs on this machine as DCP
  asks (`AspireController.StartResourceAsync`, each start an `AspireResourceRun` under a `ProcessRunner`, its console its
  own): a service held for the debugger (debugged, or quietly under Run with Hot Reload) starts with its runtime suspended
  and DOTNET_MODIFIABLE_ASSEMBLIES=debug and is attached to through the model as it starts, before DCP is answered, so a
  restart's own start is the one attach (a restart waits for it, `AspireAppRun.NextStart`); its session is named by its
  AppHost and resource (`SessionName`), so a restart takes its ended tab. Its restart is its AppHost's own resource
  commands (`MainWindow.RestartAspireCommandAsync`: restart, or stop, its project's build here and start for an edit), the
  service found by its name and project whether its program still runs, its program gone before its build; the Aspire
  window's and an agent's Restart command of a .NET service goes through the run (`MainWindow.ResourceCommandAsync`,
  `RestartServiceAsync`), so one attached as it ran is attached again, as a container's Restart does; Run without
  Debugger restarts it unheld when held or debugged. Profile of a configuration holds the chosen service at its start through the model, no code
  made changeable, and the one recording of a service records it from there (`ProfilerCommands.RecordServiceAsync`,
  `ProfilingSession.AttachAsync`'s `resume`), letting it go once recording, Stop reading its snapshot before the programs go
  (`ProfileAspireAsync`, `AspireServiceDialog` when several run); the start waits as long as the AppHost takes to ask for
  that service (Stop ends the wait, its hold on what is drawn let go meanwhile), then `AspireController.ProfileLimit` (a
  minute) from DCP's ask, past which it is given up as the time limit it is. A service Profiled as it runs (the Aspire
  window's Profile, Attach Profiler with it chosen) is recorded the same way, so Stop reads its snapshot first too. Rerun reads, before anything is built, what changed since
  the build each program runs began (`BuiltInputs.ChangedSince`: code, project, XAML and settings files under its folder and
  those it references, never what a running program writes beside them, as DCP's `.dcp.json`): the services changed are
  stopped, built and started again through their restart, the rest left running, and an AppHost whose own folder changed
  is stopped with its services, then built and run again as it ran, so nothing is built over a running program's files
  (Windows locks them). A single-file AppHost is built in the Build window as a project's is (`BuildStep.Output`, into the
  state's `aspire/<hash>/output`), Stop and Problems reaching its build. What the controller reads of the disk (a
  single-file AppHost's head, folder and launch profile, a service's project and launch profiles, its build's stamp) and
  the container ownership manifest (`AspireContainerOwnership`, read by `Peek`, written at once, its leftovers' removal
  through `StateDocument.ChangeNow`) are read and written off the UI thread. An AppHost's containers (#397): a run follows
  its resource stream from its connection until it stops, whatever the Aspire window shows (`AspireController.Follow`,
  `AspireAppRun.Following`), so each container DCP makes is known as it is made (`AspireAppRun.KnownContainerIds`, the
  Containers window regrouped only as they change, `ContainersChanged`, never at each health check's update) and a
  session one recorded at once in the state's `aspire-containers/ownership.json` (`AspireContainerOwnership`, the
  controller's) with `ContainerLabels`' state, machine and process, since DCP's containers carry none of Passenger's
  labels, and with the session network it is on (DCP's one network for the AppHost, `aspire-session-network-…`, which
  its labels say is DCP's and not Persistent: `SessionNetworksAsync`); the run's Stop takes them down once its AppHost
  has gone and waits for it (a killed AppHost leaves DCP running with them, and DCP does not take down a network its
  containers were taken from), the folder's end once its AppHost and services have (`TakeDownAsync`), and the watchdog
  and the next start as the Rules say, each the network after its containers. A Persistent container
  (`container.lifetime`, DCP's `com.microsoft.developer.usvc-dev.persistent` label) is the person's to keep: never
  recorded, never removed; so is the network of an AppHost that has one (DCP's `aspire-persistent-network-…`). DCP runs
  them on its own runtime (`AspireContainerOwnership.Runtimes`, docker or podman: the AppHost passes it
  `--container-runtime` from ASPIRE_CONTAINER_RUNTIME, DOTNET_ASPIRE_CONTAINER_RUNTIME or DcpPublisher:ContainerRuntime,
  else DCP looks itself, Docker first unless only Podman runs), which neither its resources nor its containers' labels
  name: a container the engine Passenger's docker reaches does not hold is looked for on the runtime the AppHost was told
  (`AspireAppRun.ContainerRuntime`, from the environment it was started with), then the other, each a `ContainerEngine`
  over that runtime's CLI on the PATH (`RuntimesBesides`, one a process, `RuntimeEngine`, none whose CLI is Passenger's
  docker's), and recorded on the one that holds it with the runtime's name (`Entry.Runtime`; Podman's own command with no
  API socket reached with no words, `ReadIdentityAsync`), its Stop, the folder's end, the watchdog's command
  (`ContainerLabels.MadeElsewhere`, after the labelled ones') and the next start's removal there; the Containers window
  reads that engine's DCP containers beside its own (`ContainersView.OtherEngines`, `AspireController.ContainerEngines`,
  each `ContainerInfo.Engine` that engine, its events followed while the window shows), so every pane and action on such a
  row goes there. One no engine Passenger reaches holds is said in the AppHost's console and the status bar
  (`AspireController.ContainersElsewhere`), never recorded. The Containers window
  lists an AppHost's containers under it in an Aspire heading (`ContainerRowKind.AppHost`, `ContainersView.AppHostOf`, by
  the run's resources; any other DCP made, by its labels, under the heading), and the engine's state during an Aspire run
  is said where a container run's is (`MainWindow.ShowAspireEngine` through `ShowEngine`, an engine with no Compose taken
  as serving it), followed by its events while an AppHost runs (`FollowEngineForAspire`). Add > Aspire Orchestration… (`AppActions.AddAspireOrchestration`, the solution's row or Find Action;
  `AspireOrchestration`, `AspireOrchestrationDialog`) adds an AppHost and ServiceDefaults from Aspire's templates and
  references them, each service a local of the AppHost, with the backing services Add > Docker Compose's detection finds
  for it (`BackingService.Needs`: a server once by its service's name, a database of Postgres, SQL Server or Mongo by
  each connection string's name, its database kept, Redis and RabbitMQ by the connection string's name, else the server
  itself), each `WithReference`d and `WaitFor`ed by the services that read it and its hosting integration referenced at
  the AppHost's own Aspire version (its SDK's, under central package management in Directory.Packages.props), prepared
  under the state's `aspire-staging/` before any file is written, as one command of global undo, its refusals and outcomes
  in the status bar's words (`Refusals.NoSolution`, no closing periods), a staging folder it cannot remove logged. The Roslyn extension reads an AppHost's resource graph from its code (`AspireCodeHelp`, a bounded straight-line
  reading, never resource names guessed): a service that reads a connection string (`AspireSource.ConnectionUses`, the one
  reading of them, a compose file's check's too: GetConnectionString's or a client integration's connectionName constant,
  a `ConnectionStrings:` key) or service its AppHost never gives
  it is marked (`CSharpDocument.Aspire.cs`, in the file's `ServerDiagnostics`) with a WithReference fix, one of Passenger's
  own (`CodeAssist.AspireFixes`, through `EditorFixes`, an `EditorFix` made `Elsewhere`) listed first beside the server's
  actions and worked out again by the extension as it is chosen (`AspireReferenceFix`); a connection string read by a
  name no resource has (`AspireUnknownConnection`; none for a service whose environment the AppHost sets by hand,
  WithEnvironment) and that its appsettings.json or appsettings.Development.json do not give (`BackingService.Configured`,
  a compose file's check's reading too) is marked on the name, its fix Change to the declared name near it (Compose's
  rule, `Spelling.Near`), one edit worked out again as it is chosen (`CodeAssist.ConnectionFix`); resource names complete in a
  string literal, `AddProject` goes to its project (`AspireProjectDeclaration`) and a resource's name a service reads (a
  connection string's, by GetConnectionString, a client integration's connectionName or a `ConnectionStrings:` key; a
  service's it discovers, an `https+http://` address's host or a `services:` key) to the AppHost line declaring it, one
  declaration path with MCP's `definition` (`CodeAssist.AspireDeclarationAsync`), Rename on such a name, or on the name
  where the AppHost declares it, renames the resource there and wherever its AppHost's services read it (`AspireRename`,
  worked out again as it is made, one command of global undo, MCP's `rename` too; `CodeAssist.RenameResourceAsync`), an
  AppHost's `AddParameter` with no value of its own and none in its configuration (`Parameters:NAME` in its folder's
  appsettings*.json or its user secrets, `AspireParameters`, whose values are never read out) is marked on its name in
  the AppHost's pull (`AspireUnsetParametersAsync`), its fix asking the value (`EditorFix.Asks`, the window's
  `AskValueAsync` masking a secret as typed and holding no background work while it waits, an agent's
  `apply_code_action` giving it as `name`) and setting it in the AppHost's user secrets (`Workspace/UserSecrets`, the
  UserSecretsId's own, `PASSENGER_USER_SECRETS`' in a test run) for a secret, else in its appsettings.json, the file's
  other characters kept (`JsonText`, launchSettings.json's editor too), one command of global undo, worked out again as
  it is chosen, and Edit Configurations and the gutter's
  `AddProject` icons read the same resources (`AspireRunResources`; Edit Configurations' Aspire page the services table a
  Compose configuration's is, `ComposeServicesTable.SetNamed`: each .NET resource's project as its AppHost names it, not
  chosen there, and Run, Debug or Don't start, every resource's choice kept, since one kept as Run is not debugged when
  the AppHost is; a resource marked Don't start is refused its first start in a run, `AspireAppRun.LeftUnstarted`, a start
  asked later from the Aspire window running it unheld; DCP then calls it FailedToStart, which Passenger says as Not
  started, never a failure, with why and how it runs, in its row (a muted dot), its Details and an agent's `workspace` and
  `run` answers, `AspireAppRun.StateOf`), an AppHost's icons asked for that AppHost alone as an
  open file of it opens or its text pauses, and standing while they were read from the text the file has (its hash). A
  key costs what it changed: a C# file's role (`AspireFiles.RoleOf`: an AppHost's, a service's, of a project an AppHost
  references, or none, kept until the run configurations are made again) decides what Aspire's help asks of it, so only
  an AppHost's and a service's ask the extension, a service's pull adds its Aspire warnings and an AppHost's its
  parameters' (`CSharpDocument.AspireFindingsAsync`, which an agent's `diagnostics` adds too with the configuration
  warnings, `PassengerFindingsAsync`, and whose pull its `code_actions` and `apply_code_action` make first, `PullNowAsync`,
  as they do for a file that may read configuration), an AppHost's edit pulls its open services' again,
  and any other file's typing pause, completion, actions and declarations cost Aspire nothing (AspireCodeHelpTests holds a
  pause to it by what it allocates; McpLanguagesTests has an agent go to, set and rename as the window does). An agent's
  `run`, `debug_start`, `debug_attach`, `profile` and `stop` take `resource`, a resource's name or `AppHostPath::name`
  (`PassengerTools.Aspire.cs`; a compose service's name too, the run of services'), `output` with it reads a service's
  own console (a compose service's log), `workspace` lists the AppHosts running with each resource's target (one whose resource connection has
  ended, or that sends no resources within 30 s, with that as its state and none listed:
  `AspireInteractions.GetResourcesAsync` says either in words, never as a cancellation) beside the container run, each
  resource in one shape with what reaches its .NET service from here (its container's id too), `container_action` given a
  resource runs one of its advertised commands, and `telemetry` reads their logs and traces (Mcp below).
- `src/Passenger.App/Telemetry` (#405): a run's structured logs and traces, one implementation for every kind of run.
  `ServiceRun.Telemetry` is where a run's are read (`TelemetrySource`: an Aspire dashboard's telemetry API on this machine
  and its key), an AppHost's its own dashboard (`AspireAppRun`), a container run's the dashboard it runs beside its
  services when its configuration collects them (`ContainerRun`); `AspireTelemetry` reads either, a read that fails said
  in the same words by the view and an agent's `telemetry`, as the dashboard's failure, never Passenger's
  (`TelemetrySource.Unread`, `NoDashboard`). A record costs what it adds (#437): the reader counts each resource's records
  and their bytes as they come and go, measures a record once as it writes it (`Masked`), makes a trace again only when an
  envelope touched it (`RecentTraces`, each span's start and end kept as numbers) and tells the view of logs what each
  envelope added and evicted (`AspireLogBatch`), each log numbered in order of arrival off the UI thread
  (`AspireStructuredLog.Sequence`). `TelemetryView` is the
  one view of them (logs, traces put together by id, a span tree, each list kept by key so a click survives a landing:
  the logs by their numbers, added after those listed and evicted from the front, a stream's first batch listing them
  anew, so a landing costs the UI thread what came, as AspireStandInTests hold it with a thousand kept against ten; the
  traces through `RunRows.Put`, the chosen one's tree walked again only when it changed; a log's trace and a span's logs a click apart, chosen as
  the other page streams them), streaming only the page its host shows: the Aspire and Containers windows' Telemetry tab
  (`TelemetryView.Both`, the two pages under tabs of their own, `ShowPage` choosing one), the Containers window's shown for
  a Compose project of the window's container run that collects telemetry, a service's or container's row its own
  records alone (`TelemetrySource.Of`), there because the Containers window is where a run's services are chosen and
  their logs read already, where the Run window shows one console and chooses no service. An agent's `telemetry` (not
  named for Aspire: `PassengerTools.Telemetry.cs`) reads the same: a Compose service of the container run that collects
  telemetry by its name, an AppHost's resource or AppHost, or the one run there is; a container run that collects none,
  named by a service of it or the only run, is refused saying to turn its configuration's Collect logs and traces on.
  A container run collects telemetry when its configuration's Collect logs and traces is on (Edit Configurations'
  Compose, Dockerfile and SDK container pages, `RunConfiguration.Telemetry`, off by default since its image is pulled
  the first time; the image is never pulled otherwise): `ContainerTelemetry` puts Aspire's standalone dashboard
  (`ContainerTelemetry.Image`, MIT) in Passenger's override as the service `passenger-dashboard`, Passenger's labels on
  it (Stop and the folder's end take it with the run, Stop with the .NET services when backing services are kept), on
  the networks of the .NET services it hears from, its OTLP endpoint reached on them alone and sent to with a key of the
  run's (`x-otlp-api-key`), its frontend's telemetry API published on this machine's loopback alone and asked with
  another (`Dashboard__Api__*`); each .NET service the run starts is told `OTEL_EXPORTER_OTLP_ENDPOINT` (its gRPC port, or
  its HTTP one for a service whose files say `http/protobuf`), the headers, `OTEL_SERVICE_NAME` (its Compose name) and
  one-second batches, and depends on it, unless its own files name an endpoint, whose name and headers stay theirs too.
  Once `up` has brought it up the run reads its API's port (`compose port`, `ContainerTelemetry.ReadAsync`), said in the
  run's notes when it cannot (an engine on another machine, whose loopback this one cannot read).
- `src/Passenger.App/Migrations` (#419): EF Core's migrations, Passenger's own Migrations window over the folder's
  projects that reference EF Core (`MigrationsController`, the window's over each session it binds; `MigrationsView`,
  `MainWindow.Migrations.cs`): the project, the startup project (those that reference it, the runnable ones first) and the
  context chosen across its top, never an error naming a flag; the context's migrations, oldest first, each with whether
  the database has it; Refresh, Add Migration… (its name asked), Remove Last (asked), Update Database to the latest or the
  chosen row's (rolling back asks first, naming what goes), Script Migrations… (`MigrationScriptDialog`: from, to,
  idempotent, into a tab of the state's `cache/migrations/`; SQLite's refusal of idempotent scripts offers a plain one) and
  Bundle (`bin/Migrations/efbundle` of the startup project), each a registered action (`AppActions.*Migration*`, the
  Tools/Migrations menu) under a `Working`, one command at a time (one asked for as the window lists by itself waits for
  the listing). Shown, the window lists by itself once for each choice, saying what is missing first, never asking. Every
  command is found ready before it runs: the restore read (`EfRestore`, the startup project's `project.assets.json`: the
  EF Core runtime resolved, its Design package, each provider and the range of EF Core its own dependency gives, the one
  place "the right version" is read, never EF Core's newest), the project built first when it has none, then each gap
  (`EfGaps.Before`: no `dotnet ef`, one of another version than the runtime (blocking only across majors), no Design package
  in the startup project, a provider whose range leaves the runtime out, said naming both versions) asked about with its fixes (`ChoiceDialog`, a
  question saying it downloads what the cache lacks), each fix one step of global undo: the local tool pinned to the
  runtime's version (`EfTools`: the nearest manifest inside the folder, else a `dotnet-tools.json` Passenger makes at its
  root; `dotnet tool install`/`update --allow-downgrade --tool-manifest`), the global tool's update only when asked, the
  Design package through the NuGet window's one path (`NuGetView.InstallAsync`: CPM, the package's own PrivateAssets, the
  lock file), a design-time factory from a template (`EfTemplates`, the provider's own `Use…` and the startup project's first
  connection string), and a provider behind brought into step (`EfProviders.AlignAsync`, from the feeds' own metadata,
  `IPackageFeed.DependenciesAsync`, read before anything changes: the provider moved up to the newest version whose range
  holds the runtime, else EF Core's packages (`EfProviders.InSet`) moved to the newest version the provider supports, every
  package of a project moved together by `NuGetView.InstallSetAsync`, each written with `--no-restore` and then one restore,
  so none is refused for a downgrade the others take back). A fix's button asks first too when it reaches the network. The
  NuGet window's own Install or Update of one of EF Core's packages is weighed first (`NuGetView.BeforeInstall`,
  `MigrationsController.WeighUpdateAsync`): one that leaves the project's provider behind says so and offers the set's
  update with the provider that supports it, or the update as asked. Then Passenger's own build of the project and the startup project (the Build window and Problems as
  any build's), then `dotnet ef … --no-build --prefix-output` through `ChildProcesses` (`EfRun`, the tool the manifest names
  run by `dotnet tool run`, else the person's global one by its program), its lines in the Build window, run in the folder
  and environment of the run configuration that runs the startup project (`LaunchPlan`, read with a run of services up as
  without: the database the run uses), a multi-targeted startup project given its framework. While a compose run or an AppHost runs the
  startup project, its database is the run's (`EfDatabases`, said in the window and the Build window as Against …, and in
  an agent's answer): a compose run's .NET service's resolved connection strings (Compose's model) given again at the port
  Compose published their database's service on (`compose port`, on the engine's machine, `ContainerPort.LinkOn`), the
  rest (database, user, password) as the service has it; an AppHost's resource's own, which this machine reaches already;
  given as the environment the app's configuration reads (`ConnectionStrings__X`), never on dotnet ef's command line, which
  any process on the machine can read: EF takes a connection string from outside only as `--connection`, an argument (a
  response file dotnet ef expands onto ef.dll's command line), so a context that reads none from its configuration
  reaches the database its own code names, as the run's app does; a manifest's tool never restored whose package the cache holds is
  restored and the command run again. After a listing, a command that changes the migrations, and any build the window's
  commands did not start (once the window has listed, then listing again with no build of its own), the context is checked
  for model changes no migration covers (`dotnet ef migrations has-pending-model-changes`, EF Core 8 on; `Pending`): each is
  a warning in Problems' Build tab (code `Migrations`) at the context's class, whose Show Context Actions offers Add
  Migration… (`MainWindow.MigrationFixes`, one of `CodeAssist.EditorFixes`), and a gap of the window with the same fix.
  A command fails by its exit code alone (`EfOutcome.Succeeded`): an error EF only logs is one it went past, as Npgsql's
  read of the history table a new database lacks, which `migrations list` takes as none applied.
  What it says when it fails is read into one plain line with its fix where one is
  known (`EfGaps.After`: the Design package, a context the tools cannot make, several contexts, no tool, tools not
  restored, an applied migration removed), shown in the window under the gaps with its fix as a button; EF's warning that
  the tool is older than the runtime is read as the gap it is. Without a person to ask (an agent's call or its own IDE)
  a blocking gap refuses the command, naming its fix. The gaps' fixes are Passenger's own fixes
  (`MainWindow.MigrationFixes`) on the startup project's file and on each context's declaration (`Declared`, read from the
  project's C# files), so Show Context Actions and an agent's `code_actions`/`apply_code_action` offer and make them (an
  MSBuild file's code_actions list its own fixes where it has any); an agent runs the Migrations actions by `ide_action` with
  `input` for what they would ask (`MainWindow.MigrationsForAgentAsync`: Refresh's context, Add's name, Update's or Update
  to's target, Script's FROM..TO), each answering what it came to with the context's migrations and every gap with its
  fixes' titles and where code_actions finds them; `app.migrations.fix` makes the first fix shown.
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
  macOS menu key equivalent; in-window menus show it as text. Its drop-down is `AppSettings.Keymap`: Visual Studio's (the
  default, which a settings.json without one keeps) or VS Code's (`DefaultKeymaps.VSCode`, `AppActions.VSCode`), the
  person's shortcuts over each kept apart (`Shortcuts`, `VSCodeShortcuts`; `OwnShortcuts()` is the chosen one's), put in
  force for the process by `AppActions.Choose`. A tool window's button whose tip names its action's keys takes it through
  `ActionTips`, said again as the window follows a changed keymap (`MainWindow.Follow`, with the stripes' `FollowKeys`).
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
  What's New (#416) is the running version's release notes, carried, never fetched: the repository's
  `docs/release-notes/X.Y.Z.md` beside the app as `release-notes/X.Y.Z.md` (`ReleaseNotes`; the site takes none), which
  `tools/release-notes.sh` also makes the GitHub release's body, so the release and the app say the same (a paragraph on
  one line, since GitHub's release page breaks lines where the file does; notes still holding the stand-in's
  `<!-- Stand-in` line fail a published release, `PUBLISH`, and only warn in a dry run). They open in an editor tab of
  their own (`MainWindow.WhatsNew.cs`, "What's New in X.Y.Z", `OpenDocument.IsReleaseNotes`), read-only and holding no
  text, drawn in the editor's place as an asset's preview is (`Editors/ReleaseNotesView` over `Editors/Markdown`:
  CommonMark's common blocks and inlines as Passenger's own controls, styled in Shell.axaml, images left out, an http or
  https link opening through `Browse`). Only the person's window shows them by itself (App's `ShowWhatsNewOnce`: never
  `--mcp`, `--headless`, a screenshot, a test's window unless the test asks, nor a Passenger an agent's `--mcp-attach`
  started, which `PASSENGER_STARTED_FOR=agent` tells, `McpProxy.StartedForAgent`, taken out of its environment as it
  starts, so the person's next start shows them): once, when `UpdatesState.NotesShown` names an older version or is
  missing from a state an older Passenger kept (`StateStore.UsedBefore`: its documents there as the process opened the
  store), at once as the version starts and recorded as shown then, a folder the start is still opening coming back with
  its own tabs behind them (`EditorArea.Restore`, Editors above); a fresh state records its version and shows nothing,
  the Welcome screen greeting a first start. Help,
  What's New (`AppActions.WhatsNew`, Find Action's Release Notes too, an agent's `ide_action`) shows them whenever asked,
  greyed for a version that carries none (a build at Directory.Build.props' version before its notes are written).
  `WhatsNewTests` hold it, each window the version whose stand-in the build carries (`MainWindow.Notes`).
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
  its own (off Windows a socket in Passenger's socket folder, `SocketFolders`, as `PID-….profiler`) and streams EventPipe
  to a .nettrace; `Snapshot` reads it (TraceEvent) into call trees and hot spots.
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
  begins, their input's end stopping the session with its rundown; the snapshot's file made before they start, so one that
  cannot be written starts no recording there; `resume` lets a program held at its start by
  DOTNET_DefaultDiagnosticPortSuspend=1 go once recording), the bytes written to the trace here as they come and read by
  `Snapshot` as ever, which keeps the `Place`. Attach Profiler takes the debugged container session first
  (`DebugSession.Place`, by its id there and the debugger's path map), else, from the action with nothing of the
  folder's running, one chosen among the running containers' programs in Attach to Process's dialog
  (`AttachPurpose.Profiler`, `MainWindow.ChooseProfiledAsync`; resumed), the tools put in by `ProfiledInContainerAsync`
  as the debugger's are, under a `Working`; MCP's `profile` takes `container` with `attach`, and profiles a container
  configuration as Profile does (Containers above). Hot spots' sources
  (`MethodSources.FindAsync`) are read by the tools' `sources` in the container, where the modules are, each document
  made this machine's by the place's map (as it is for a build of this machine's). A container's run has no inspector,
  so the Frames page (`FramesView.InContainer`) and MCP's `frames` of an app with Avalonia's modules say why it is empty.
  `MemorySnapshot` is a heap dump read with ClrMD into `HeapGraph` (roots by dotMemory's kinds, shortest paths, a
  dominator tree for retained bytes); `HeapHistory` follows a run's snapshots' objects through every GC (an EventPipe
  session of the GC's survival and movement events), so `SnapshotComparison` gives new, dead and survived objects;
  `App/Profiling/MemoryView` is the Memory page. A container's snapshots are taken there (`ContainerHeap`, one given up (an
  agent's `profile` with `memory`) or past 10 minutes ending the tools, said as a `ProfilingException`: the tools'
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
  the person's history. Off Windows the shell is told its folder as its PWD, never the IDE's inherited one, which a shell
  keeps where it names that folder through a link (Passenger started from a shell there) and dotnet builds by: a build
  through another spelling of a project's path after one by its real path, the IDE's, has MSBuild's IncrementalClean
  delete what the first wrote and it did not write again, runtimeconfig.json and deps.json among them, so the program no
  longer starts (exit code 131, #413). `MainWindow.Terminal.cs` is its
  tool window and the keys it takes ahead of the IDE's (a Ctrl+letter always, the shell's control character, whatever
  action a keymap gives it).
- `src/Passenger.App/Run` and `ToolWindows`: build and run through the .NET command line
  (`ProcessRunner` streams a process into a document; `BuildOutput` reads MSBuild's error lines),
  and the bottom panel's Build, Run and Problems windows (Problems' File tab lists markers moved by keys once typing
  pauses, 250 ms, and fresh markers for the text already shown at once; its solution-wide list keeps each report by its
  URI's path, made once off the UI thread as it comes with its problems placed, and lists again only the files a pull
  changed, an open file's as its own pull lands (`CSharpService.SolutionProblemsChanged` names them,
  `ProblemsModel.ShowSolutionFile` makes or moves only their rows), so a typing pause costs what it changed; it asks a refused
  workspace pull again after a pause, 1 s doubling to 30 s, and `LanguageServer.RefuseNext(method, n)` refuses one for a
  test; Roslyn holds a workspace pull open until the next change, so an answer lost after it was sent waits for another). Every console (Run, Build, Debug, the profiler's) is written
  through `ConsoleSink`: a frame's output at a time, colour codes stripped whole across reads, the head trimmed past
  Rider's 1024 KB, and what is typed into it sent to the program; a program's output and errors are read into it by its
  one pump (`ConsoleSink.PumpAsync`: a read at a time, colour-code state of its own, the stream ended there as it ends,
  a last '\r' let go; a build's, a run's, a container's log and Compose's, the profiler's through its `ProgramPump`).
  A run configuration is data (`RunConfiguration`: the
  folder's own in `runConfigurations.json`, the rest made from the executable projects' launch profiles, those that
  cannot launch here saying why: a project whose framework references Microsoft.WindowsDesktop.App runs only on
  Windows (`ProjectModel.WindowsOnly`), so off Windows the Solution view marks it, `RunConfiguration.WhyNotHere` greys
  its configurations and `LaunchPlan.ResolveAsync` refuses it before its build; a container kind's are run by
  `BuildRunController.Containers.cs`, Containers above, the store's version 2 giving older ones the Project kind). A
  generated one is changed in place in Edit Configurations but for its name, project and launch profile, its link to its
  source (a copy changes those): what differs from what its source makes is kept with the folder as an edit over it
  (`ConfigurationEdit`: fields by the store's names, a dictionary's entry by entry, null for one taken out; the store's
  `edits` by the generated one's name, version 3, kept while its source is gone), so a field left alone follows its
  launch profile, compose file or Dockerfile as it changes; `RunConfiguration.Source` is what the source makes, which the
  dialog marks changed rows against (`TextBlock.changed`, the Keymap page's colour) and Reset to Launch Profile (Compose
  File, Dockerfile, Project) puts back. Write to Launch Profile, for one made from a project's launch profile (Project,
  Executable, Docker or SdkContainer; never a compose file, Dockerfile or .dcproj, which Passenger does not rewrite),
  applies the dialog, then writes what the profile has a place for into launchSettings.json as text (`LaunchSettingsEdits`:
  a value replaced where it stands, a property added indented as the others, the rest byte for byte) as one command whose
  Part puts the folder's edit back on Undo (`MainWindow.WriteToLaunchProfile`, `RunConfigurations.Rebase`); what the file
  has no place for (the framework, console, building first, a variable its application URL gives) stays the folder's.
  `LaunchPlan.ResolveAsync` works one out once, in one request of the project host, into what Run,
  Debug, Profile, Hot Reload and a debugged test host start (its build, `BuildStep`, by the project's real path, with
  `--framework` only for a project of several frameworks or a configuration that chose one), and `RunConfigurationsDialog` is Run > Edit
  Configurations. While a run of a configuration is up (Run's program or container run while the Run console or the
  terminal has it, `ran` forgotten as it ends, a session Debug launched for it, Profile's program:
  `BuildRunController.IsRunningConfiguration`; an AppHost's by its own run), the toolbar, its chooser and Edit Configurations' row say
  "(running)" after its name (`MainWindow.Shown`), the toolbar following as Stop does (`ShowStop`). A build says what started it (`BuildCause`): explicit builds open the Build window as they start,
  the build before Run, Debug or Profile only with Settings' implicit-build option, a test run's never, and its end
  brings the Build window forward as Showing build results says (errors only by default). Every build holds the host's
  generating design-time builds off while it runs (`ProjectBuilds.HoldAsync`, released in `FinishBuild`), its dotnet command
  starting once the host answers (`ProcessRunner.StartDotnet`'s `after`), so a test reads `Builder.ProcessId` only once
  it has started; dotnet watch's own builds aren't held, and Run with Hot Reload is never refused for the working folder
  its build makes (`LaunchPlan.WorksWhereBuilt`: a project never built gets its output folder made before dotnet watch
  starts there). A test project's first Project launch profile gives its variables to its tests. A configuration that
  uses the terminal (Rider's Use external console) runs in a Terminal tab of its name (`TerminalPanel.RunAsync`, a
  `TerminalRun` with its process, exit code and Stop); Debug answers the adapter's runInTerminal the same way
  (`DebugController.RunInTerminal`), and `debug_input` and `output`'s debug use that tab. In a program's tab, Run, Debug,
  Rerun, Stop and the debugger's keys stay with the IDE. Console links come from `ToolWindows/ConsoleLinks` (URLs, .NET
  stack frames, compiler messages; in the terminal also paths to any file, hidden until Cmd/Ctrl), drawn by
  `CodeEditor.Links` and `TerminalView.Links`; `MainWindow.Browse` is the only way the IDE opens a browser (recording
  `Browsed` for tests; the update notice's link too), and Run and Debug open a plan's `BrowserUrl` once its port answers
  (`Run/BrowserLaunch`). `MainWindow.ShowInFileManager` is the only way it opens the file manager (recording
  `ShownInFileManager`): `FileManager.Show` opens a folder through the system's launcher and chooses a file with macOS's
  `open -R` or Explorer's `/select` through ChildProcesses, and the headless platform, which has no launcher, opens nothing.
- Publish (`Run/Publishing.cs`, `Run/PublishDialog.cs`, `Run/BuildRunController.Publish.cs`, `MainWindow.Publish.cs`): a
  project to a folder through `dotnet publish` with a publish profile, the SDK's own `Properties/PublishProfiles/NAME.pubxml`,
  so `dotnet publish -c CONFIG -p:PublishProfile=NAME` does the same (the CLI takes no configuration from a profile, and
  a multi-targeted project's outer build never reads one, so Passenger always gives `-c`, and `-f` for one with several
  frameworks). Publish... (the Build menu, a project's menu in the Solution view, Find Action) is greyed with why by
  `PublishProfiles.WhyNot` (a test project, a library that does not set IsPublishable true, IsPublishable false, a Windows
  desktop app off Windows) and opens `PublishDialog`: a profile there (one that publishes otherwise than to a folder is
  listed, never chosen) or a new one, its options read through the project host's `properties` with the profile as a
  global property (`PublishProfiles.EvaluateAsync`), never from the file's text, the folder MSBuild's default until
  named, and the command that does the same. Save and Publish write the profile through `FileText.Save` as one command of
  global undo (`MainWindow.SaveProfileAsync`), `PubXml.Apply` changing only what changed and keeping the rest of the file,
  after asking (`Confirm`) before changing a file there or putting a new profile in one's place. The publish is a build
  of the Build window's (`BuildKind.Publish`, its `Working` under `AppActions.Publish`, Stop ending it, Problems listing
  what it found, the Build window's Rerun publishing again), its folder resolved by MSBuild before it starts
  (`PublishProfiles.ResolveAsync`: PublishDir; a Visual Studio web profile's PublishUrl, where Visual Studio copies the
  app on with DeployOnBuild and dotnet publish does not, is only said in the dialog), said in the Build window and
  the status bar, whose word opens it (`MainWindow.ShowInFileManager`, recorded as `ShownInFileManager` for tests). Publish Again publishes a project with the profile it last published with (`PublishMemory`, the folder's
  `publish.json`); an agent's `build` publishes given `publish` (a project) and `profile`, folded into build since a
  tool of its own took the list past McpTests' budget, and answers the folder. Remote targets (Web Deploy, FTP, Azure) come as
  kinds of `PublishProfileFile.Remote`: a page of their own in the dialog and a deploy after the folder's publish, their
  credentials in the system's store, never in a .pubxml.
- `src/Passenger.App/Testing`: unit testing works in Passenger's own `TestItem` (its id the project and full name, plus
  a data row's name) and `TestOutcome`, behind `ITestRunner`; `VsTestRunner` is the only file that knows VSTest's
  object model. `TestFinder` shows a solution's tests from code as it opens, before any build: test projects' files
  parsed off the UI thread, an open file's from its own parse (`TestsAtCaret.Read` is the one reading of a file's
  tests), changed ones through the folder watcher; discovery and runs merge into those rows by id. A read of an open
  file that only moves its tests' lines (`FileTests.OnlyMoved`) moves the tests told and the rows showing them in place
  (`TestFinder.Moved`, `TestTree.Move`): no project told again, no tree made again, the gutter's index kept.
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
  UI thread, so `FuzzyMatch` allocates nothing and first rejects a name without the query's letters in order, and
  `FileIndex.Search` keeps only the best as it goes, never sorting every match; completion's `Shown` does the same),
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
  another process over the same state (an agent's `--mcp`) writes to it too: each append, each prune (from its read of the
  log to its last delete) and each reused blob's touch holds `localHistory/changes.lock` against that process (waited for
  on the history's queue, 10 s at most). `Workspace/FolderWalk`, beside `IgnoredFolders`, is the open folder's one walk
  (Go to File's index, whose walks the single-file AppHosts are found among, Local History, a folder's projects,
  `TestFinder`, the XAML class lookup), passing over hidden
  folders only where it looks for what projects build, as MSBuild's globs do. `LocalHistoryView` is the tool
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
  Add > Reference (`MainWindow.References.cs`), and sets a property (`SetProperty`, one in a comment passed over, over
  `WithProperty`'s text, which a fix's command takes as an edit; `PropertyIn` what the file's own text sets) for
  Add > Docker Support's SDK route and Manage User Secrets' UserSecretsId. `MainWindow.DockerSupport.cs` is Add > Docker Support on a project (a Dockerfile
  beside it and a .dockerignore at the context, the solution's folder; or EnableSdkContainerSupport and
  ContainerRepository) and Add > Docker Compose on the solution (compose.yaml, a Dockerfile for each project chosen
  without one, the backing services its packages call for with healthchecks and connection strings, listed in Solution
  Items; `Workspace/BackingServices`, the detection Add > Aspire Orchestration shares), the text from
  `Workspace/DockerFiles`, each Add one `Commands.Record` with every file watched before it is
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
  a second Postgres database leave to the app. The Explorer's row menu is the window's actions run on that row
  (`ActionContext.ExplorerRow`, as a tab's menu has `ActionContext.Tab`; `MainWindow.ExplorerActions.cs`), its own keys
  only Enter and the arrows: the keymap's run there on the row chosen, Rider's one key for two actions told apart by where
  it is pressed, so the editor's Cut, Copy, Paste, Delete (Backspace too), Undo and Redo act on the row (`ExplorerEdits`),
  Rename renames it (Rider's RenameElement), and Compare Files takes the editor's Duplicate keys in Visual Studio's
  keymap off macOS (`IdeAction.KeysOf`, which menus show; Cmd+D of its own on macOS). `MainWindow.FileMoves.cs` is the Explorer's Cut, Copy, Paste and drags,
  every move through `EditorArea.MoveFile` so open files, breakpoints and bookmarks follow.
  `EditorConfig` reads the .editorconfig files over a file (its own parser and globs); `FileIndent` picks a file's
  indent (its .editorconfig, else `Indentation.Detect` on its lines, else its language) and names it in the status bar;
  `OpenDocument.Save` applies the save-time properties. Anything Passenger itself writes to a project, solution,
  nuget.config or Directory.Packages.props saves that file first if it's open (`MainWindow.SaveBeforeWriting`, or
  SaveAll before `dotnet add package`) and then loads what it wrote (`FollowWritten`); a delete saves the open files
  under it first, and holds off design-time builds under it (`ProjectBuilds.HoldOff`). Every write of a workspace file
  goes through `FileText.Save`: a temporary file and rename (`AtomicFile.Write`, the file's own metadata kept on top), through symlinks, permissions kept, on Windows its
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
- `src/Passenger.App`: the shell. Explorer, editor tabs, files on disk. Each action is an `IdeAction` (id, title,
  group, menu places, `Run`, `WhyNot`, whether its keys work while the terminal has them) that its feature adds to the
  window's `ActionRegistry`: `MainWindow.Actions.cs` for the window's own, a partial's or controller's `Add…Actions`
  for the rest, and a `ToolWindow`'s `Action` for its show action (`IdeAction.Words` are other names Find Action finds
  it by, as Rider's older names). `AppActions.cs` holds the ids and each platform's
  keymap. Keys, the main menu (`MainMenu.Build`), Find Action, context menus and MCP's `ide_action` all run actions
  through the registry, so one that cannot run says why (a greyed menu item on hover too); editor actions go to the
  focused editor. A menu, a toolbar's tip and macOS's application menu take an action's title from the registry
  (`TitleWhere`), never typing it again; a label that asks more before it acts ends in the one ellipsis, "…", as Rider's
  titles do (UiRulesTests, `DialogButtonsTests.Check`); and what is missing is said in `Refusals`' words (No file is
  open, No folder is open, No solution is open, `NeedsFile`, `NeedsLines`), the same want worded the same everywhere. The keys are the main
  window's one keymap in every window the IDE opens but a dialog, each taken in by `IdeKeys.Attach` before it shows (the
  main window, an editor window through `EditorArea.WindowOpened`, a floating or windowed tool window through
  `DockHost.WindowOpened`, a diff's or changes window through `MainWindow.ShowOwn`, the merge window, a test's frame
  through `TestFramesStrip.ViewerOpened`; `MainWindow.OwnWindows.cs`): a keymap chosen runs in each at once, an action
  pressed there runs in its context (`IdeKeys.Pressing`, which `Context` reads), an editor action goes to that window's
  editor with the keys (the shown file's, or a diff's or merge's side), the terminal keeps its keys wherever it floats,
  and a popup its keys open goes over it (`PopupHost.Attach`, which wraps the window's content with its layer), no wider
  than the window (`PopupHost.Fit`), the keys going back to what had them as it closes. A part with keys of its own (a diff's F7, a merge's Accept keys, a test
  frame's zoom) is an `IOwnKeys`, which the keymap leaves them to; a dialog (Settings, Conflicts, a question) keeps its
  own keys and gets none of this. An action that takes a
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
  Live Visual Tree's details for an agent (value sources; styles with why each applies or not); `breakpoint_set` (with
  `remove` too) takes `control` and `property` for Break When Value Changes, waiting for
  `LiveTreeController.Watching`; `debug_state` carries `bindingValues`, and with several programs debugged lists them
  (`sessions`): the debug tools and `stop` take `session`, a Debug tab's name, else act on the one paused, else the only
  one, and are refused naming them when they cannot tell (`PassengerTools.TabFor`; `stop` naming none is Stop All,
  `BuildRunController.StopAllAsync`, the window's own); `hot_reload` is the window's Apply Hot Reload Changes
  (`MainWindow.ApplyHotReloadAsync`: `DebugController.ApplyAllAsync` under the action's work, given a `HotReloadRestart`, the
  window's `SettingsRestart`, an agent's `RestartForAgentAsync`, which never asks; under dotnet watch, `SaveForWatchAsync`'s
  last word); the headless `--mcp` IDE
  starts on the default settings, so `run` takes `hotReload` and `output` reads the Run console; `debug_start` on a
  container configuration debugs each service in a session of its own, answering with them (`sessions`). An agent gets as much
  from a container run's services as from an AppHost's resources (`PassengerTools.Services.cs`): `workspace` lists the
  container run beside the AppHosts (`ContainerRunInfo`: its configuration, Compose project, how it was started, state),
  every resource of either in one shape (`ResourceInfo`; for a .NET service `Reached`: what it is held at its start for,
  the live Debug tab debugging it and its state, a quiet Hot Reload session, the profiler's recording). A compose
  `resource` no run has is started with the configuration that has it, the one named, else the chosen one, else the one
  Compose configuration whose model has it (`ComposeTargetAsync`, refused naming them when several do, and for attach and
  stop refused saying no run has it). `run` of a resource of either kind answers once it is up (running, and not unhealthy
  or still starting its health check), has ended or waitSeconds pass, with its run's resources one line each, its notes
  and its own console (`ResourceAnswer`, `Standing`: an AppHost's waited on through its resource stream, a compose
  service's through the engine's events), one its run runs itself (a backing service, an AppHost's container) run by
  nothing and answered as it stands. `output` of a compose service is its log (`ComposeLogAsync`, `compose logs`), and
  `telemetry` of a container run that collects none says to turn its configuration's Collect logs and traces on.
  `container_action` given an AppHost resource runs one of its advertised commands by its name, or start, stop and
  restart as Aspire's `resource-…` ones (`AspireCommandOfAsync`, `MainWindow.ExecuteAspireResourceCommandAsync`, through
  the window's `ResourceCommandAsync`, a .NET service's Restart through its run; asking no one, as an agent's run asks no
  one about the certificate, a command that asks the person for input refused): there,
  since the Aspire window's rows are the Containers window's, rather than in a tool or a run tool's parameter of their
  own, which every session's list would pay more for. `McpTests`' actions
  test holds `ide_actions` to every `AppActions` id. `settings`/`settings_set` (`PassengerTools.Ide.cs`) read and change
  the settings by their settings.json names through the window's `ApplySettings`, as the dialog's OK does, a number's
  range found by asking `AppSettings.Clamped`, an unknown name or choice refused and Enable MCP Server never changed;
  `find_in_files`/`replace_in_files` are the window's own search in its scopes and masks once `FileIndex.IsWalking` is
  false; the `local_history` tools wait for `LocalHistory.IsRecording`, their revert `RevertLocalHistoryAsync`;
  `problems` is Problems' Toolset, Build and Solution tabs (after `CSharpService.IsAnalysing`), `output` of build the Build
  console, and `profile` takes `attach` (and `container`, a container's program). The tools reach the
  folder through the window's `ProjectSession` as each call finds it, and the
  window only for editors, the terminal, actions, opening and the picture; `McpHost` hosts them three ways: `--mcp [FOLDER]` (a headless IDE on standard input and output, whose window no other
  agent joins, its server answering before the IDE is up and each call waiting for it), a window's own socket in the state's socket folder (`McpHost.SocketFolder`, `Core/SocketFolders`, the one
  folder of every Unix socket Passenger makes, an inspected app's and a profiled program's port too: in the user's own temporary
  folder, macOS's by `confstr` whatever TMPDIR an agent has, else XDG_RUNTIME_DIR or /tmp, named by a hash of the user and
  the state folder, mode 0700 and used only once its mode is the user's to set, so a socket's path stays within macOS's
  104 bytes whatever TMPDIR or PASSENGER_SETTINGS is, each socket named for its process first and only a window's ending
  in .sock, and `TestRun` removes the run's) (`AgentSocket`, open from the window's start but for
  `--no-mcp` and a screenshot without `--agent`), and `--mcp-attach [PID] [FOLDER]` (`McpProxy`: the agent's server for
  its whole session, answering initialize and tools/list from `McpHost.ToolMethods` and joining, at a tool call, only a
  window whose folder holds FOLDER or the agent's working folder; with none it asks the running Passenger to open it
  through the process's `PID.open` socket (`AppSocket`, `PassengerApp.OpenProject`), else starts Passenger (`open -a` on
  the app bundle on macOS, else `ChildProcesses.StartApart`) and waits for that folder's window). Each mode ends on
  SIGTERM as on the end of its input (`McpHost.EndOnSignal`, gone within `EndLimit` at the latest), since an agent's
  client may send it a moment after closing the input (OpenCode 1's, 20 ms after), and `--mcp-attach` ends the test
  run's headless window by closing its input, so it takes its socket away before the watchdog would kill it. Each of their
  connections, the proxy's to its window too, is a `PeerTransport`: an answer or notification to an end that has gone is
  dropped as the connection's ordinary end, never an internal error, since the SDK sends some unawaited (a call's error
  answer after its answer failed, a request given up by its token); a request's failure still reaches its sender. Every call on a
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
  (`debug_evaluate`, its `value` set too, and `debug_input`) and `breakpoint_set` (whose condition runs in later sessions
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
  nothing) suits better than `--mcp-attach`. `McpHost.Instructions` tell an agent the parameters
  several tools share, to begin with `workspace`, find its way by `find_symbol`/`outline` and `refactor`'s describe, and what a person's window
  asks; every answer is written by `McpHost.ToolCreateOptions` (relaxed escaping: code reads as written, its quotes and
  angle brackets never unicode escapes), and every tool is `McpHost.Lean`'s (no null default or null type, no title in its annotations). An
  agent pays for the tool list every session (69 KB before #236's trim, 55 KB after it, held under 60 KB by McpDebuggingTests until
  Aspire's resources and telemetry took it to 60.6 KB, and under 62 KB since, the owner's call, which 0.2.0 reached at 61,990
  bytes; its folds and trim below took it to 46,512, so 15.5 KB are free), so it says each thing once: the element query
  (`control`, `filter`) in `app_tree`'s description, and every app_ tool's `app` and `window` in `app_windows`'; the wait
  for an agent's own write, and the parameters several tools share (`configuration`, `arguments`, `waitSeconds`,
  `session`, `resource`), in the instructions; a tool's title once (`Lean` drops the SDK's copy in its annotations); a
  parameter described only where its tool's description does not already name what it takes; lines and columns from 1
  never per parameter. Nor does it hold a tool another covers (the
  owner's call, 0.2.0): `open` shows a file at a line and column, the app_ tools with `app` ide picture Passenger's own
  windows and type into its editor at the caret, `hierarchy` has implementations, `debug_continue` runs to a line,
  `debug_evaluate` sets a `value`, `breakpoint_set` and `nuget_install` take `remove`, and `output` reads the Run, Debug
  and Build consoles. `save_all` stays a tool of its own, not `ide_action`'s `app.saveAll`: it is never asked about on a
  person's window (#278), where `ide_action` is, and its answer names the files a save left unsaved (a read-only one).
  `edit` replaces text in one file as the editor does (found once as written,
  else refused saying where it is; one Undo step, saved, the changed lines said back); a tool naming an open file the
  editor has not changed takes the disk's text first (`PassengerTools.DocumentAsync`), since an agent's own file tools
  write there and ask at once: its length and time looked at off the UI thread and the file read and compared there only
  once they changed since the editor last read it (`OpenDocument.TakeDiskTextAsync`, `DiskStamp`, with the text's version
  then; a time within 2 s of its read trusted to show no second write), so a question of an open file costs the window
  what it asks, not the file's size (#438, McpWindowTests holds it with 20,000 lines against ten). A call's wait for what
  the IDE does wakes on the event or task of what it waits on (`When`, `On`, `Ends`: the C# server's state, a debugged
  program's or an inspected app's, a file's checks, a profiled program's end, a run's end), `Until`'s 50 ms looks left only
  where what it waits on raises nothing, each saying why; `refactor`'s project and solution scopes take their files from
  the folder's index off the UI thread (`CodeFilesAsync`), and the tools are made once a process, off the UI thread
  (`McpHost.Tools`, each call reaching its connection's `PassengerTools` through `McpHost.Serving`). `run`, `debug_start` and `hot_reload`'s restart return once an inspected app's UI is up
  (`AfterLaunch`, which watches 2 s more for a stop in its first layout); each run of a stop's frames outside the
  folder's code is one line of their modules (`Frames`); a run, debug, test run or profile refused for a failed build
  names that build's errors (`WithBuildErrors`, by the `BuildOutcome` of the build it ran, `LaunchBuild`; a refusal for anything
  else names none); `build` gives MSBuild's output tail only when it failed; `tests_list` takes `filter`, `state` and `limit`; `find_symbol` puts the name asked
  for first. `nuget_install` (and its `remove`) answers a `PackageChange`: the version installed and the files that changed
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
  lines, Hot Reload and inspected app, `DebugTab.App`, #352), and `DebugController.Session` is the chosen tab's, which the window shows and every action and
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
  this machine's, fast mode), crosses as it is. An adapter there that ends with a failure no one asked for while its
  place has stopped (`AdapterPlace.Running`, Attach to Process's and a container run's `AdapterPlace.Runs`: the container stopped, taking
  docker exec with it) is the program's end, said so (`DebugSession.Gone`), never the debugger's. A tab's program output
  followed from outside (`AdapterPlace.Output`) ends with its session, its failed start, its tab's close and the folder's
  end. Hot Reload's apply is its own tab's whichever is chosen when it lands (another program's stop takes the window):
  its built lines, breakpoints, console and resume are that program's. A stop's values are a `VariablesTree`'s rows in a
  `VariablesList`, the one list the Variables view and the debugger's hover card both are (its keys taken ahead of the
  ListBox's, which swallowed Right and Left; its menu; a row's members read under `BackgroundWork`). Rested on while
  paused, a value with members is its row in the hover card (`DebugController.HoverAt`, #340; one without stays words),
  evaluated in the chosen tab's frame, each row it opens into asked of that stop alone, its rows' actions the view's own
  handlers; the card (`InfoCard.AddValues`) takes the narrowest of the popups' widths that holds it, never narrowing while
  shown, scrolls past 16 rows, stays while the pointer, its keys (a click, or Quick Documentation pressed again) or its
  menu are on it, and goes 300 ms after they leave, on Escape, and with its stop (`DebuggerHover.Gone`, which
  `NextShown` cancels); a value set in it shows the value read again with what was open still open
  (`VariablesTree.ShowAgain`). An EF Core query (an IQueryable whose type, or a type it derives from, is EF Core's own)
  lists first a SQL row (#420), the engine's (`ManagedDebugger_QuerySql.cs`), so Variables, the hover card and an agent's
  `debug_variables` read the one row alike: the protocol's lazy row (`DapPresentationHint.Lazy`, kind virtual, its name
  and words `DapRows`'), whose value is read only as it is opened (`VariablesTree.Read`, the row read put in its place,
  read again as rows are by `ShowAgain`), the program then running `ToQueryString()` through the debugger's own evaluation,
  as one asked for by name, under its time limit and aborts; what keeps it from SQL (a query its provider cannot translate,
  an evaluation refused or past its time) is its value, and a row shows a value's lines as one (`VariableNode.Shown`). Its
  SQL opens whole in a read-only tab from a copy under the state's `cache/requests/`, as a command's text from the
  Requests window does (`MainWindow.OpenSql`; a double-click, Enter or its menu's Open in Editor). The engine decides exception breakpoints (`ExceptionRule`)
  when the runtime reports a handler found or an exception unhandled, the stack still at the throw ("user code" is a
  Just My Code frame); they cross the protocol as DAP `exceptionOptions` with Passenger's own scenarios and conditions.
  An evaluation the runtime won't start where the thread is stopped (a pause in optimized or native code) is
  `EvalRefusedException`; a value shown by its ToString then shows its type. An evaluation past its time is aborted only
  where the abort takes as its thread goes on (`ManagedDebugger_Aborts.cs`): through the abort's window the program is
  held a moment every 25 ms (`Extensions.LookEvery`) and the abort asked only of a thread caught in managed code at a
  safe point, outside any catch or finally (`WhyNoAbortTakes`: no `USER_WAIT_SLEEP_JOIN` or `USER_UNSAFE_POINT`, no frame
  in a handler); never a rude abort. An abort asked anywhere else (a wait on a lock a paused thread holds, a sleep, a
  read, a finally) stays pending until the thread gets there, after the program has gone on, inside what it waited for:
  the lock was then never let go, after a normal abort as after a rude one (#238). Keeping the thread suspended instead
  cannot help, since the runtime withdraws a func-eval's abort only as the evaluation ends, which a suspended thread
  never reaches. One never caught so runs on with the program unaborted, to its own end, its end let go. `Extensions.EvaluationTimeout` (5 s, or
  `PASSENGER_EVAL_TIMEOUT_MS`: `Dap.AdapterEnvironment` names it and `PASSENGER_ENGINE_LOG` once for the IDE and the
  adapter, and a malformed value is fatal as Passenger starts, as it is to the adapter) also sets that window (`AbortWait`, 0.4×). Data breakpoints (Variables' Set Data
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
  Changes, for one session, the one debugging its element's app (`PropertyBreakpoint.Session`, sent to that session alone,
  its app's inspector told by `LiveTreeController` as `LiveApp.Watched` keeps): the inspector watches that element's
  property (protocol v3 `watch`, `PropertyBreaks.cs`)
  and calls `Breaks.Changed`, on which the IDE sets one function breakpoint per property breakpoint (condition
  `breakpoint == Id && (…)`, the hit count kept in the app); the stop says its `change` and, from `setAt`, where the
  style that set it is written at the editor's line. The inspector is never optimized (the debugger reads that
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
  once its time is up and where the abort takes, `AbortWhenDueAsync`), since `ICorDebugEval::Abort` answers only after half a second, the program
  running or stopped, every other call into the runtime waiting meanwhile. The window shows the program running as a
  continue or step is asked (`DebugSession.Resume`; refused, the stop is shown again), asks nothing more of a stop once
  it is gone, and writes each stop's time to its frames and locals to passenger.log (`DebugController.LastStopTimes`).
- The debugger, Passenger's own, started from SharpDbg and ICorDebugSharp (MIT; `THIRD_PARTY_NOTICES.md` says what came from where):
  `src/Passenger.Debugger.Interop` (ICorDebug, IMetaDataImport and dbgshim for the COM source generator),
  `src/Passenger.Debugger` (the engine), `src/Passenger.Dap` (the Debug Adapter Protocol, both ends; `DapConnection` reads a
  header to 64 KB and a body to 256 MB, `MostHeader` and `MostBody`, past which the conversation ends as at a message that
  does not read, its requests failed) and
  `src/Passenger.DebugAdapter` (the engine behind the protocol, its own process, carried under `debugger/`; its flags
  and loop are `AdapterProgram`, which runs it in the invariant culture, so every value reads as C# writes it whatever the
  machine's, and setExpression gives a real literal its target's suffix, a float's f or a decimal's m, so a value shown
  sets back). `src/Passenger.ContainerTools` is `passenger-tools`, what runs inside a Linux
  container: `dap` (`AdapterProgram` and `DebugAdapter.cs` compiled in, the one engine), `processes` (the container's
  .NET programs as JSON, `DotnetProcesses.List` plus those mapping libcoreclr.so with no debugger pipe, each with
  whether it was started for Hot Reload, built optimized, and why it cannot be attached to), the profiler's `trace
  --pid PID [--resume]`, `heap --pid PID` and `sources` (`Passenger.Profiler`'s `InContainer`, ClrMD and its Azure
  packages carried; `ContainerTools.ToolsCommand`) and `version`. Self-contained and InvariantGlobalization, never
  carried by the app (the release's `Passenger-container-tools-<rid>.tar.gz`, or `just container-tools`); a musl
  publish keeps its own libdbgshim.so
  (`KeepMuslDbgShim`: the glibc package's matches a musl system too, and won). They carry `LICENSE.txt` and notices of
  their own, `THIRD-PARTY-NOTICES.txt`, which their build writes (`WriteToolsNotices`, below).
  The engine's `SourcePaths.Match` takes a
  PDB document that exists where the adapter runs while the breakpoint's file does not (the IDE's, outside the
  container) by what they share from the end, and compares a Windows path (C:\…, \\server\…) ignoring case on any
  system; the engine gives a Windows document's path back with forward slashes. A module matches a breakpoint's file once
  (`BestDocument`, kept until an update brings documents) and binds it among the methods with a point in that document
  alone (`MethodsWithPoints(document)`), never every method of the module. Before
  letting a program go (Disconnect without terminating), the engine runs it off any patch or single-step stop and stops
  it again (`ManagedDebugger_LetGo.cs`): under CET a thread resumes from a breakpoint only through the debugger's
  out-of-process SetThreadContext, which mscordbi's Detach waits for only once it has answered one such flare, so a
  first stop after an attach, detached at, would fault (0xC0000409); and it waits out an evaluation the stop's read left
  to the program (`EndAbandonedEvaluations`: each to the end of its own time, `EvalStatus.DueAt`, one that could not be
  stopped not at all, never aborting one, whose abort could land in the runtime's own work and end the program), since
  the runtime refuses the detach while one runs and the thread it returns on waits for good for a debugger that went; a
  thread that wait ran off its stop is not run off it again (places are measured before), and the whole let-go keeps
  within the client's time for it (`DisconnectArguments.LetGoWithin`: Detach waits `DebugSession.LetGoLimit`, the
  evaluation limit and 5 s, never killing the adapter under a let-go; a window's close gives it 2 s, within its
  `EndLimit`). An attach that ends before it takes hold of a program held at its start (no configurationDone, a failed
  attach, the session ended) lets that program's runtime go (`ReleaseHeldTarget`, `DiagnosticClientHelper.Resume`), so
  none is left waiting at its start for a debugger that is not coming. Symbols for assemblies without their own are
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
  text, checked against the build PDB's checksums), the runtime's capabilities read from the program's CoreLib; its
  projects the program's (`Session.Projects`: those whose modules the debugger says it has loaded, with what they build
  on, taken in at every emit, so a module it loads after an update, a plugin's, takes its project's edits from then on),
  every other project given to the service as last committed (`HotReload.FrozenAsync`), so another program's edits are
  never this one's rude edits and wait, as built, for its module to load; the
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
  standing. MCP's nuget_install has open project files take the disk's text before answering
  (`MainWindow.FollowWritten`).
- `src/Passenger.Inspector`: the Live Visual Tree's helper inside an Avalonia app being debugged (Passenger's own, MIT,
  net8.0, carried under `inspector/`, never put in the app's build): a startup hook `LiveApp.For` gives a launch whose
  build has Avalonia.Base.dll beside it, Debug's, Run's and Profile's alike (a WPF app gets the WPF counterpart, below),
  through `DOTNET_STARTUP_HOOKS`, which it takes back out of the environment. It compiles against Avalonia 12's
  implementation assemblies (the reference ones leave out [PrivateApi]
  value-store diagnostics) and runs in a load context of its own that takes the app's Avalonia whatever its 12.x; it
  never touches `Dispatcher.UIThread` before the app makes it, and answers on the app's UI thread over a Unix socket in
  Passenger's socket folder (`Core/SocketFolders`, as `PID-N.inspector`; one that cannot be made, a path past a socket's
  104 bytes included, is logged and the app launched uninspected rather than its launch failed). `Protocol.cs` is compiled into both ends, deeper than System.Text.Json's
  default 64. Its wire's types are made ready as it connects, before the app's Main
  (`Connection.Configure`): System.Text.Json readies a type on first use under one lock for all of a context's types,
  which a thread the debugger stopped can hold from an evaluation at the stop (the binding values), which then waits on
  it until the app runs on. `Send` never waits on Passenger and never leaves out an answer: answers queue apart, written
  before any event; an event waits merged with its kind's (a failure's newer count by its id, one `treeChanged`,
  `valuesChanged`, pick, hello, `untold`), and only the inspector's own errors queue unmerged, up to 1024 waiting, the
  rest left out, counted and said in protocol v6 `untold` (`Untold.Messages`, which the IDE logs).
  `App/LiveTree/LiveApp` is Passenger's end (held by its Debug tab, `DebugTab.App`, ended with its debugging, so each
  app debugged at once keeps its own; `ProjectSession.LiveApp` is the chosen tab's, which the Live Visual Tree, binding
  values and the Debug window's overlays follow; what the app tells unasked is heard on the UI thread a batch at a time); `LiveTreeController`
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
  one test a run inspects Passenger in the test process (`McpAvaloniaAppTests`' person's window). Binding values
  (`BindingValues.cs`, protocol v4 `values`): each element by its XamlSourceInfo place (a root by x:Class), of several
  instances the one nearest the element chosen in the Live Visual Tree, each binding's path walked from Avalonia's
  binding expression nodes. Paused, the IDE evaluates `Wire.ReadValues` on the UI thread (the stop's, else the main
  thread, known by the engine's mark in its threads answer, Passenger's own `DapThread.IsMain`, whatever the app named it;
  the startup hook's copy forwards to the inspector's own), the answer an array whose slots the debugger shows
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
  socket, protocol v7, `Hello.Framework`), runs in the default load context, and retries a main-thread dispatcher WPF is
  still making. Value sources are `DependencyPropertyHelper.GetValueSource`; places `VisualDiagnostics.GetXamlSourceInfo`
  turned into files through the assembly's PDB; binding errors come from `PresentationTraceSources` and
  `BindingDiagnostics.BindingFailed`, and `Breaks.BindingFailed`/`Changed` keep their names so one function breakpoint
  serves both frameworks (XAML Binding Errors; a WPF stop has no user frame, so the editor shows the XAML). Its
  `Driver.cs` clicks through UI Automation patterns in-process and sends WPF's own key, text and wheel events, never
  activating a window. `PASSENGER_INSPECTOR_TRACE=FILE` traces its start and answers. A stop whose ids came in an unanswered setFunctionBreakpoints waits for
  that answer (`DebugSession.TakeStopAfter`), so a property or binding-error stop is known as one.
  The inspector also takes Avalonia's logger before the app's Main (`Bindings.cs`, put back over any sink the app sets,
  which is still fed) and tells of each failed binding (`bindingFailed`: control, property, path, message, XAML place,
  and for a member the source's type lacks its nearest members) once, then its count at most every 250 ms; past its
  first 1000 distinct failures (`Most`, both inspectors) it lists none, counting their every failure as `Untold.Bindings`,
  said with the counts, which Problems' Bindings says under its list, the console once and `app_binding_errors` as a
  last entry of level Untold, so every failure is listed or counted. Run gives an
  Avalonia app the inspector too (`ProjectSession.InspectedRun`, `RunApp`; none under Hot Reload, whose dotnet watch
  would load it); the Live Visual Tree follows only the chosen Debug tab's `LiveApp`. Problems' Bindings lists Run's app's
  failures and every Debug tab's, each kept while its tab is (`ProjectSession.InspectedApps`), and an AppHost's services',
  listed once for all their apps told at once (`AspireController.BindingsChanged`, never the AppHost's whole state at each
  failure). Agents' app tools take
  `app`: run, debug (the chosen tab's app while it is debugged, else the only one), a Debug tab's name, or ide
  (`PassengerTools.DebugApp`, `NamedApp`). `App/LiveTree/BindingErrors` is how a
  failed binding shows: a console line with its place first in a compiler message's shape (so `ConsoleLinks` links it),
  Problems' Bindings tab (`BindingProblem`, by file, each once with its count, placed as it first fails in its XAML read
  once for all told in that turn of the window's thread, `BuildRunController.FailureText`), and fixes through
  `CodeAssist.EditorFixes`, Passenger's own fixes that Show Context Actions lists before the server's.
- `tools/Passenger.Notices`: writes the app's `THIRD-PARTY-NOTICES.txt` (beside `LICENSE.txt` in every build and
  publish, through `WriteNotices` in `Passenger.App.csproj`) from the packages the build copies, the self-contained
  runtime pack, the debugger's and Roslyn's server's `.deps.json`, each package's licence and copyright, and the part
  of THIRD_PARTY_NOTICES.md after its marker line; a licence it cannot write fails the build. Given `for=tools`, it
  writes the container tools' (`WriteToolsNotices` in `Passenger.ContainerTools.csproj`, straight into their output once
  their `.deps.json` is written, which a publish then takes) from the packages that `.deps.json` names, their runtime
  pack and dbgshim for their system included, and only the sections of the debugger's copied source (`section=`);
  `tools/container-tools.sh` and the release check the archive has them. `tools/check-notices.py` (`just notices`) reads
  what the tools carry on each of their systems from their restore, so THIRD_PARTY_NOTICES.md's "In the container
  tools" names it all.
- `src/Passenger.App/MSBuild`: code help in project, .props and .targets files. `MSBuildService` is the session's
  (`ProjectSession.MSBuild`), one `MSBuildDocument` per open MSBuild file, evaluated through its project (itself, the
  workspace project whose evaluation imports it, a Directory.* file's nearest project, or the one Go to Declaration
  came from) once typing pauses, on the text as typed, and again as a file comes into or goes from a folder that
  evaluation's wildcards walked (`MSBuildInsight.Walked`: the folder watcher's `Structure` changes, and
  `FilesCameOrWent` for what the IDE itself made, moved or deleted, a new file of which the watcher takes for a save).
  Completion reads the text as typed (`Passenger.Xaml`'s XmlTree)
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
  names the site and the updater link to, its body the version's `docs/release-notes/X.Y.Z.md` (What's New's, above). Its Run workflow with dry run builds it all and publishes nothing. ci.yml
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
  (through ChildProcesses) on macOS, `StartApart` on Windows. So is the file manager a reveal reaches: `open -R` and
  Explorer's `/select` run through ChildProcesses and hand the file to the Finder or Explorer already running (`FileManager`).
- **Containers are ended by label**, since no child holds one and Lifeline cannot end it: everything Passenger has an
  engine make carries `ContainerLabels.Arguments` (`net.passenger-ide.state`, a hash of the state folder,
  `net.passenger-ide.machine`, a hash of the machine's name and id, `net.passenger-ide.process`, the process's id and start
  time, and the folder's), its folder's `ProjectSession.Containers` removes what it started as the folder ends (of a
  Compose project only the containers and networks this process's labels are on, never `down`, which would take the
  person's own of it, as a run's Stop takes its own: `FolderContainers.TakeDownAsync`), the watchdog's last command (`ChildProcesses.AtIdeEnd`) removes the process's own on macOS and
  Linux however it ends, reaching the engine the IDE's commands reach (the CLI's DOCKER_* variables as the IDE has them,
  the context by name, told again when it changes), and the next start (`ContainerLabels.LookForLeftovers`, once a state
  a process: the process's start its own, a window keeping its state elsewhere, a test's, as it first shows;
  `RemoveLeftoversAsync`) removes those of its state and machine whose process is gone (`ContainerLabels.IsLeftover`, the
  one rule), once the state has labelled anything (`ContainerLabels.EverLabelled`): another machine's on a shared engine
  are never judged by this one's processes. What an engine makes for Passenger that carries none of its labels (an Aspire
  AppHost's session containers and session network, DCP's) is recorded with the same three (`AspireContainerOwnership`)
  and ended by the same rules and commands, with no rules of its own: the watchdog's command names them beside the
  labelled ones, the containers by id and then the network by name, on the engine that holds them, the one the IDE's
  commands reach or the runtime DCP chose (`ContainerLabels.MadeUnlabelled`, `MadeElsewhere`, `EndedUnlabelled`, the
  CLIs' variables, Podman's CONTAINER_HOST too, as the IDE has them), and the next start removes a gone process's on the
  engine it recorded them on (a runtime's by its name, found on the PATH again), each still the container recorded and
  not Persistent, then their network while DCP's labels say it is not Persistent, forgetting one the engine no longer holds. Tests
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
  Visual Studio OSX (macOS) keymap gives it, and in the VS Code keymap the one its VSCode or VSCode OSX keymap gives it
  (`plugins/keymap-vscode`), each resolved as IntelliJ does: a keymap's own list (its XML, then the shortcuts plugins'
  descriptors declare for it), else its parent's ($default, or Mac OS X 10.5+ taking $default's Ctrl as Cmd). A key
  IntelliJ gives two actions, told apart by where it is pressed, goes to one, said where it is bound. Rider's keymap
  files are in its jars for reference.
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
`BackgroundWork` until its console has its last output and exit code, or, while a job it left holds its output, for
`ChildProcesses.OutputGrace` before that job is ended with its group (`OutputEndsAsync`, as `RunAsync` does), and a
landing the dispatcher drops lets its hold go (`UiThread.Post(work, dropped)`); the folder watcher's gathering and delivery,
Local History's queue and a console's batches were background work already. Left out by design: a program still
running and its output, the state store's half-second write delay (a test that reads a state file flushes it), the
templates' read ahead, and the system's own delay before the watcher hears a change (a test that needs it heard waits
for the watcher's `Changed`). A wait that gives up lists each hold by the file and line that took it (`InFlight.Watched`,
set by `TestRun`, names `BackgroundWork`'s holds, `UiThread`'s crossings and each `Debounce` by caller, at no cost
outside tests) and each piece in flight, with since when (`Ui.Busy`). A wait's words are made as it is called, so what
they say of the window (its status, a model's state) is how the wait began; words of what stood as it gave up are a
`Func<string>` (`Ui.WaitFor`'s other overload): #433's hang was a status read as the wait began. No fixed sleeps: `Thread.Sleep`, `Task.Delay`
and `Screenshot.Pump` are banned in the tests (RS0030) outside `Waiting` and `Ui`. A wait for something is `Ui.WaitFor`,
`Ui.PumpUntil`/`Ui.Pumped` (the window's loop run, not settled) or `Waiting.Until`/`UntilAsync`/`Within` (off it);
time a test needs to pass (a debounce it measures, a moment in which nothing may happen) is `Ui.Hold(time, why)`,
`Ui.Stall` (the window's thread blocked) or `Waiting.For`. Every wall-clock limit is written as the time on a
developer's machine and multiplied by `TestRun.TimeScale` (`PASSENGER_TEST_TIME_SCALE`, 1 when unset, a value that is no
number above 0 failing every test with why, from the run's fixture; `Limits.Scaled`): Ui's and Waiting's waits scale what they are given, and the settle,
folder, process, restore, git, debug adapter and MCP call limits go through it; the IDE's own waits on what it started
(an Aspire service's start for the profiler or a Debug tab, a restart's wait for it, an agent's wait for a resource) are
`Waits.Of`, as much longer as `PASSENGER_WAIT_SCALE` says (a malformed value fatal as Passenger starts), which the run sets
to its time scale. CI sets it per system in `test.yml`
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
ended with its input) as the proxy's child, in the proxy's own PASSENGER_SETTINGS (`McpAttachTests`). No test
reaches a registry through a Dockerfile it opens: the run sets `PASSENGER_BUILDKIT_CHECKS=off`, the default of Check
Dockerfiles with BuildKit (`Containers.BuildKitChecks`), which `EngineChecksTests`' one test of it turns on in its window's
Settings. Nor does a container run mount the person's user secrets: the run points `PASSENGER_USER_SECRETS` at its own
folder (`TestRun.UserSecrets`), where a test writes a project's. No test reaches GitHub: the run points `PASSENGER_RELEASES` at a dead loopback
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
(`DebugSession.Collection`) and `Beside`'s classes, one after another: a class that opens no window, waits on nothing of
the headless session's, changes nothing the process shares (its PATH, its environment) and holds nothing to a time, so a
busy machine only lengthens it (`AspireExecutionServerTests`, `McpAspireStandInTests` and `McpLanguagesTests`, whose
Passengers are processes of their own; not `McpTests`, which holds a git status to ten seconds). A class with a test that holds what it tests to a time (a key's, a frame's, how soon the
window answers, a profile's samples, the debugger's evaluation limit), or that debugs through the window, goes in
`Alone`, which runs after the rest with nothing beside it. A class whose tests share one project derives its fixture
from `SharedProject`: its windows share one warm language server (`MainWindow.WarmServer`), the project is put back as
written before each test, and a file a test adds goes through `project.Write`, which ends the shared server: a file
every test opens is the project's from the start (`LongFileAssistTests`' 20,000 lines). A file a test only rewrote is put
back, with the time it was written at (the IDE refuses a server's edit to a file not open written in the last 2 s,
`WorkspaceEdits.Catching`), and the server kept once it holds it as written again, which it reads through its own watcher
(`WarmServer.HoldsAsync`, 10 s at most, else it is ended); a file that came or went, or one the server holds no document of
(a project file), ends it. A window test that asks nothing of C# opens its window without the language server
(`Ui.OpenFolder(folder, languageServer: false)`, a shared project's `Open(file, languageServer: false)`,
`MainWindow.StartsLanguageServer`), so it waits for no server's start, load or restore (nor, for a project whose packages
the package cache lacks, nuget.org's); an agent's `--mcp` Passenger likewise (`McpRig.Start(…, languageServer: false)`,
which sets `PASSENGER_MCP_LANGUAGE_SERVER=off`, `McpHost.LanguageServerVariable`, a value other than off or on fatal). A
shared project none of whose windows starts a server, and none of whose tests reads what a restore writes, is never
restored (`SharedProject.Prepare(restore: false)`: `FindInFilesScopeTests`, `UiRulesTests`). A class whose tests each restore a fresh folder takes it from `RestoredFolders`, which makes and restores
as many at once as the class has tests yet to take one, four at most; one whose tests mostly leave the project as written shares it and takes a
fresh folder only for a test that changes it (`DebuggerTests.Sum` and `Fresh`), since `RestoredFolders.Take` may come in
the test itself, its batch being no test's own (`TestFolders.Unowned`). A shared project that its first test builds
makes its bin first (`DebuggerTests.Sum`): bin's making changes the folder's own time, which the next reset takes for a
file added and ends the server for. A `SharedProject` leaves a repository's `.git` alone (a class whose tests change the
repository puts it back itself) and keeps bin and obj between tests, so a window that never saves launches on an
up-to-date build (`HotReloadEditsTests`); its `Dispose` is virtual for a fixture that holds more (`ProjectFileHelpTests`'
stand-in feed). A rule held under both looks is checked in one window, the look switched in place through the window's
settings (`UiRulesTests.UnderEachLook`), a failure naming the look. `RestoredFolders` saves nothing for classes whose
tests change their folder throughout (`ExplorerFilesTests`, `PackagesTests`, `FileNestingTests`): share a
`SharedProject` where the tests mostly read. `just test` runs the suite on one machine as CI's shards run it on several
(`tools/test-run.py`), since a process's window tests use little of a machine while they wait on servers, MSBuild and
programs: each test process keeps to a run folder, state, NuGet home, MSBuild node salt, agent sockets and container
labels of its own (`TestRun`), so the classes outside `Alone` are dealt to N processes at once (by the most any system's
runs took for each in TestTimes.json, else its budget; N a quarter of the processors, at most 4 and one per 8 GB, else
`PASSENGER_TEST_PROCESSES` or the recipe's second argument), those beside the window tests (`DebugSession.Collection`,
`Beside`) dealt apart, the heaviest of their hands with the lightest hand of window tests, since a process takes about as
long as the longer of the two, their limits scaled by 1.5 unless PASSENGER_TEST_TIME_SCALE
says otherwise, the classes that use the container engine together in one (`LANES`), and once every one has ended the
`Alone` classes run in one process with nothing beside it. A class is Alone by its declaration's `[Collection(Alone.Name)]`
(or its base class's), which the runner reads from the sources; one it cannot find runs alone. So a class never counts
on another having run in its process, and what of the machine a class uses beyond its run's folder (a fixed port or name,
the engine's processors) runs alone or joins a lane. `just test "" 1` is one `dotnet test` in xunit's own order, and
`--plan` shows the deal and runs nothing; each process's .trx and console log go to artifacts/test-results/local, the last
run's removed as the next begins, and the failed tests are listed as each process ends and again at the run's end.
CI splits each system's suite into shards, a runner each (#206), so a class
never counts on another having run before it: each shard lists the suite (`tools/test-shards.py list`, listed again while a
listing names no test or says discovery failed: xunit 3.2's runner can spoil its answer to the adapter's -assemblyInfo,
xunit #3576) and `tools/test-shards.py` deals the classes
by their seconds on that system in `tests/Passenger.Tests/TestTimes.json` (one it lacks by its budget), the same deal in
every shard, checked by the first shard before any test runs to put every test in exactly one (a listing of each
hand, a VSTest discovery each, which the others need not repeat); inside a shard the collections keep their
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
tests' sessions and the IDE's alike (an adapter in a container keeps it there, `DebugSession.ContainerEngineLog`). `Debugging/Helpers/StartHold` holds a launched program's runtime at its start by
pointing `DOTNET_DiagnosticPorts=…,connect,suspend` at a port of the test's own, which comes before the runtime starts
its debugger (LaunchTests, DebuggerTests, and McpDebuggingTests through a launch profile); a window test with a held start waits
with `Ui.PumpUntil`, since the start holds BackgroundWork. Attach to Process's search matches text, so tests choose a
program's row by its id (`ChooseById`). LoopingConsoleApp's `waiting` and `waiting first` modes give an async method a builder whose
`ObjectIdForDebugger` waits on the paused passing thread, `elsewhere` awaits something that goes on on a thread of
its own, and `race` races a step's end with another thread's breakpoint, the hit first in most turns (`race throws`,
with its exception's handler found, which an exception breakpoint's condition is tried at). `LiveTreeTests` debug `tests/Debuggees/LiveTreeApp`, headless, with no project file
of its own (the test writes one on the tests' own Avalonia version); "click X,Y" typed into its Debug console stands in
for the pointer. `BindingErrorsTests` run and debug `tests/Debuggees/BindingErrorsApp` (written the same way), whose
caption binds a misspelt Name; "rebind" typed into its console makes it fail again. Its `flood` argument (`Flood.cs`)
adds 3,000 rows binding members the view model lacks, failing as it opens and every 50 ms until "calm" is typed, which
writes how many failures Avalonia logged (a log sink of the app's own, which the inspector's feeds); the test runs its
waits with `Ui.PumpUntil` meanwhile, since the failures keep the window busy. `XamlBreakpointsTests` debug
`tests/Debuggees/XamlBreakpointsApp` the same way ("paint", "warn" and "rename" typed into its console change Save's
background and width, the buttons' style class, and the caption's binding). `XamlEditingTests` drive each XAML editing
tool in an Avalonia app and a WPF app of their own (`AvaloniaGallery`, `WpfGallery`), the MVVM Toolkit stood in by its
attributes' full names. `ProjectFileHelpTests` drive the MSBuild code help on an SDK-style project and a
Directory.Packages.props with `StandInFeed`. `ConfigurationTests` work a web project's configuration (`TestProjects.Web`,
its options classes and reads, nothing from nuget.org): completion, declaration both ways, the hover cards, the warning
and its two fixes with an agent's, and Manage User Secrets, every user secret in the run's own folder
(`TestRun.UserSecrets`), each test removing the ones it made. No test sees the system's window frame: `TitleBarTests` checks the roles
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
`UiRulesTests` holds the window-wide rules (menu marks and placement, tool window tabs, rows and toolbar buttons, popup
widths, titles and rows, scrollbars, the editor tab's ×, the focus ring, the half-1366 layout and empty texts), and
`DialogButtonsTests.Check` also holds dialogs to the plain button look, a disabled default, the chrome page, one
drop-down width per column and Don't ask again's place.
A click is `Ui.Click`: a real press and release at a control's centre (or a point in it) in the window or popup showing
it, failing unless the control is shown, enabled and what the pointer finds there (an open menu's light-dismiss layer
passes it on), then settled unless told not to (a click during work that holds background work); `RightClick`,
`DoubleClick`, `ClickButton` (by its words), `ClickRow` (an Explorer row, scrolled to by `Ui.Row`) and `ClickLink` (a
console's link) are its kinds. A menu item is chosen by clicks, through its submenus, by `Ui.Choose` (an open menu),
`ChooseFromMenu` (the menu key), `ChooseFromRowMenu` (an Explorer row's right click) and `ChooseFromEditorMenu`; a
raised `Button.ClickEvent` or `MenuItem.ClickEvent` is banned (RS0030), allowed only where no pointer could reach, saying why. A console's input is `Ui.TypeLine`
(`TypeToRun`, `TypeToDebugged`), a caret's place `Ui.PlaceAfter`, a window on a folder `Ui.OpenFolder`. A project a test
writes is `TestProjects.Console`, `Library` or `Web` (`.With` for its item groups), every one on `TestProjects.Framework`;
a file of a test's folder `TestFolders.Write`; a container run's Stop and take-down `Docker.Stopped` (`TakenDown` for the
wait alone); an MCP answer's words `McpRig.Text`, an agent's going `Gone`.
A test waits for a Run, Debug, Profile or test run with `Ui.Launched`, and says the status with `Ui.Said`, both naming
the build's errors (`Ui.BuildErrors`: Problems' rows, else the Build window's last lines) when it failed, so a CI-only
build failure says why. A test's stand-in that holds a file a target writes leaves it as it found it: a file it creates, newer than the
target's inputs, makes MSBuild skip the target as up to date (an empty `obj/…/Avalonia/resources` was embedded:
AVLN9999). A test's own build of a project the IDE builds too names it by its real path (`PathNames.Canonical`), as the
IDE does: the run's folder is under macOS's TMPDIR, a link (/var), and a build through it after the IDE's deletes what
the IDE's wrote (the terminal's paragraph; `StandInAppHost.BuildApi`). Read any log the IDE or a server may be writing with sharing (`Ui.Shared`), and a `WaitFor` condition returns false,
never throws, while what it waits for isn't there yet; `WaitFor`'s limit also covers background work held all along (a
build, a test run), where `Settle` alone gives up after 90 s (scaled). A chord goes through one `Ui.Press(window, "control M ,
control S")` or `Ui.Run`: the window forgets a chord's first key after 2 s (a dispatcher timer that fires during a
settle), so a settle between its keys can lose it. Find Usages, Safe Delete and every list of the server's locations
make each URI a path once (`DocumentUris.ToPath` asks the disk), never once per location on the UI thread. A test that opens a dialog holds it to `DialogButtonsTests.Check`
(centred words, one height and radius, Rider's order). `HotReloadTests` and `HotReloadEditsTests` (Alone) edit debugged
programs (`KeyTextInput` drops `\n`, so multi-line edits go through `Typing.Replace`; HotReloadTests' `Plugins` loads a
plugin no project of it references, from the plugin's build, once "load" is typed into its console); `WpfLiveTreeTests` (Alone,
Windows only) debug `tests/Debuggees/WpfTreeApp` in a real window, and `McpWpfAppTests` drives it with `--mcp`. `WindowsOnlyProjectsTests` open a dotnet new wpf and winforms app
(`WpfApp.Create(asMade: true)`, `WpfApp.CreateWindowsForms`) never restored: Passenger restores and builds them itself.
`PublishTests` publish a console app, an ASP.NET Core app and a self-contained app in a folder whose name has spaces,
the self-contained one at the tests' own target, whose runtime pack for this system the tests' project downloads
(`Microsoft.NETCore.App.Runtime.$(NETCoreSdkRuntimeIdentifier)` at the SDK's bundled version), so its restore finds it
in the package cache. `DecompiledSymbolsTests` and `DecompiledSymbolsTimingTests` (Alone: the session's answers during a
decompile) give each adapter a cache folder of their own; PassengerCases' `corelib` case stops in a callback from
List.ForEach, CoreLib's frame below it. `DebuggerHoverTests` (Alone) open the hover card of a stop's
values by the pointer and the keys (`People`: an object holding another, and a list), holding it to the popups' rules
(`UiRulesTests.HoldsToPopupRules`, for a popup PopupHost does not show); its helpers (`Hover`, `Toggle`, `Member`) are
`QuerySqlTests`' (Alone) too, which debug a console app on EF Core and SQLite they write (`Shop`, a `SharedProject`, its
packages from the package cache through the run's local feed, as `LiveRequestsTests`' shop's), stopped holding a query EF
Core translates and one it cannot: each one's SQL row read in Variables and the hover card, its tab opened, and an agent's
`debug_variables` reading it as it opens the row. `BindingValuesTests` debug `tests/Debuggees/BindingValuesApp`
("rename" typed into its console renames the customer on the UI thread, which it names UI). `DebuggedAppsTests` (Alone)
debug BindingErrorsApp and LiveTreeApp at once (a solution over both), choosing each one's Debug tab in turn, and
`McpAvaloniaAppTests` has an agent's app tools name one by its tab's name. `AvaloniaFramesTests` (Alone) profile,
debug and run `tests/Debuggees/FramesApp` (its SlowPanel spins 30 ms a measure). `DebugSessionsTests` (Alone) debug two
programs at once (`Pair`: First and Second, which share Shared/Counter.cs), and `McpSessionsTests` has an agent name
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
snapshots compared; `McpContainerProfileTests` has an agent profile it with `attach` and `container`. `ContainerRunDebuggingTests`
(Alone) debug ContainerRunsTests' Compose solution (`ContainerRunsTests.Shop`, its Program.cs stopping in Main and in a
request's handler), run it with Hot Reload, and in images mode build its apps here with `-p:PathMap=…=/src/` copied into
aspnet:10.0 by Dockerfiles of their own, so no SDK image is pulled; a request is asked again while nothing listens yet,
since the attach lets the service go a moment before Kestrel listens. `ContainerRunProfilingTests` (Alone) profile that
solution, web spinning from its start (`Spinning`, which `McpContainerProfileTests`' agent profiles too): web's hot
method leads web's own (a web app's idle threads wait in the runtime's code). `ContainerServicesTests` (Alone) act on one
service of that solution's run from the Containers window's rows and the gutter, the others never restarted (web published on
the loopback address alone, kept there and on its port through its restarts), Stop and Remove a debugged one there and
Restart and Stop the run's project, and Debug one whose tools cannot be found (`ContainerToolsFrom` an empty folder and the
dead release feed), which then runs on unheld, and start both marked Don't start later from their rows (web's Run, api's
Debug stopping in its Main), Rerun restarting web's changed build as the run's (`McpContainerDebuggingTests`' agent restarts
and stops one with `container_action`, reads the run in `workspace`, runs a service as a resource and reads its log, and, a
second compose file in Web/ making web two configurations', is refused a start of it until one is chosen, then starts web
and api alone as resources; and its `debug_start` of web, marked Don't start by a .dcproj's launch profile, starts it later
in that run, stopped in its Main, Stop leaving no container);
`ContainerRunStopsTests` (Alone) press Stop during its first build and during `up` as held services attach, press Debug as
Run brings it up and open another folder as Debug does (its cache answering its health check only after 25 s, so `up` waits),
and `McpContainerDebuggingTests`' agent is refused a run that would replace the person's own cache, stops a run as it
comes up, gives up on `debug_start` once its containers are made and ends its `--mcp` IDE with a
run up, each leaving no container of the project. Aspire's tests (#338): the run gives every `aspire` CLI and AppHost it
starts a home of its own (`ASPIRE_HOME`), telemetry, version checks and certificate making off (`TestRun`), and since an
AppHost and the CLI keep their record of running AppHosts in `$HOME/.aspire/cli/bch` whatever `ASPIRE_HOME` says, a HOME
of the run's own too (`AspireCli.Home`, `/tmp/passenger-aspire-<pid>`, short for macOS's 104-byte socket paths, removed
with the run): the fixtures' AppHosts' launch profiles name it, and on macOS and Linux the run's PATH starts with a wrapper
named `aspire` that runs the run's own CLI there (`Aspire.Cli.<rid>`, a PackageDownload where `PassengerAspireRuntime` is
true, its bundle unpacked beside it in the package cache) or refuses, so neither the IDE nor a test ever runs the person's
CLI or reaches their ~/.aspire. On Windows an AppHost keeps that record in the person's profile whatever its environment
says, so there `AspireTests` (Alone) and `McpAspireTests`' tests of a real AppHost skip (`AspireApp.Unavailable`), and
`AspireExternalDataTests` (`AspireApp.CliUnavailable`), which runs `aspire run` through ChildProcesses, finds its AppHost
in the window and ends it with its process group, waiting until no service or orchestrator of it is left; `AspireApp` makes two of
Aspire's starters from its templates installed in a hive of the run's own, restored from the run's local feed, which
`AspireTests` share as a `SharedProject` already holding what each test asks of them (an edited method, the API's command
and its prompts, a launch profile whose folder is missing, a file-based service), so no test writes a file of its own,
and whose AppHost writes DCP's record and the dashboard's data under its obj, so a run leaves the next window the warm
server; a window there starts no C# language server unless its test reads C# (the gutter, Edit Configurations' services,
Hot Reload), what a running starter shows is read in one run of it, and a test that changes the app (Hot Reload, Rerun, a
restart, a debugged or profiled start) runs its own; and
`AspireCodeHelpTests` restore their AppHosts the same way (`AspireApp.UseFeed`), as do `AspireOrchestrationTests`, on a
solution of the tests' own target, the rest from the package cache: `tests/AspireGraphs` (an AppHost's, ServiceDefaults'
and the starter's API service's graphs at net10.0, restore-only projects of the solution, built empty) puts them there,
since the test project's own restore prunes what its frameworks bring (Microsoft.Extensions.*), which a net10.0 AppHost
asks at versions above its framework's, and an AppHost's project references take no part in its restore;
Aspire's dashboard and orchestrator (`Aspire.Dashboard.Sdk.<rid>`, `Aspire.Hosting.Orchestration.<rid>`, about 900 MB in
the package cache), which only running an AppHost needs, are downloaded only where `PassengerAspireRuntime` is true (a
property, or an environment variable, the person or CI sets; #400): without them `UseFeed` gives the folder a
Directory.Build.targets that takes them out of its AppHosts' references after the AppHost SDK adds them, and a test that
runs an AppHost skips saying so (`AspireApp.Unavailable`);
`MigrationsTests` work a SQLite console app (`ShopApp`) whose EF Core, Design package and `dotnet-ef` (at `EfCoreVersion`
and `EfCoreOlderVersion`, Directory.Packages.props) come from the package cache through the run's local feed:
`tests/EfGraphs` (the SQLite provider's and the Design package's graph at net10.0, at both versions, restore-only projects
of the solution) puts the packages there and the test project's `PackageDownload` the tool at both versions, which
`ShopApp` copies into `AvaloniaApp.Feed` with EF Core's own packages, whose metadata the window reads; a stand-in provider
(`ShopApp.Provider`, a nuspec alone) in a feed of the test's own supports only the EF Core its version names; the run's
own DOTNET_CLI_HOME keeps the person's global tool out of sight; `MigrationsPostgresTests` list, add and update the
migrations of a web app on Npgsql's provider (tests/EfGraphs/Npgsql, `NpgsqlEfVersion`) against the Postgres its fast-mode
compose run brings up beside it, at the port Compose published it on, its password in no command line Passenger ran
nor the IDE's log (`LogsTests` see a secret among a program's own arguments as dots in the log and the Run console's
echo), and skip, saying so, unless the engine holds
`postgres:17-alpine`, which they never pull;
`AspireStandInTests` need none of it and run everywhere: the window runs a stand-in AppHost (`StandInAppHost`, a console
project that sets IsAspireHost and writes the resource service key the IDE gave it to a file) whose launch profile names a
stand-in resource service the test drives (`AspireInteractionHost`, admitting only that key, recording the commands the
window runs), so its console, resources, commands and prompts are the window's as a person's AppHost's are (a row's
address and a command each pressed as an update streams in), and nothing swaps the window's own connection or providers
(BannedSymbols); the stand-in AppHost also writes the IDE's execution endpoint and token (`StandInAppHost.SessionFile`),
with which a test asks the IDE to start its built service (`StandIn.Api`) as DCP does, by the execution protocol, so its
Profile asks which service in `AspireServiceDialog`;
`McpAspireTests` (Alone) has an agent find the Aspire tools and give up on an AppHost's start, which ends its process and
its execution listener, and ends a Passenger whose AppHost made a session container two ways: killed with its watchdog,
children and orchestrator, as a crash ends it, so the next start removes the container, and on macOS and Linux killed
alone, its orchestrator held stopped (`Processes.Suspend`), so the watchdog's last command removes it; `AspireExecutionServerTests` speak to the execution server on loopback; `McpAspireStandInTests`
have an agent run the stand-in AppHost, its telemetry from a stand-in dashboard (`AspireDashboardHost`, the launch profile's
applicationUrl), its resource service's calls ended (`AspireInteractionHost.Drop`) and its resource's commands run through
`container_action` (`AspireInteractionHost.Commands`); AspireStandInTests' windows run a
stand-in aspire CLI of their own (`MainWindow.AspireCliFrom`, a shell script, so skipped on Windows); and
`AspireContainerRecoveryTests` remove a dead owner's recorded container through a stand-in engine; `AspireContainersTests`
(Alone) run the starter's AppHost with busybox containers of each lifetime, Stop taking the session container and its
session network down, and one told Podman (ASPIRE_CONTAINER_RUNTIME in its launch profile, with the person's
XDG_DATA_HOME and XDG_CONFIG_HOME, since the profile's HOME is the run's and would move Podman's store) has its
containers followed on Podman beside Docker's engine, as the Ubuntu VM has them (`AspireContainersTests.Podman`;
skipped, saying why, without Podman, with a docker that reaches Podman itself, or without busybox in Podman's store).
`ContainerTelemetryTests` run a compose app whose ASP.NET Core service sends OpenTelemetry (its packages the ones the
AspireGraphs' ServiceDefaults put in the package cache, `OpenTelemetryVersion`) with Collect logs and traces chosen in
Edit Configurations, find its log and the request's trace in the Containers window's Telemetry tab and through an
agent's `telemetry`, and see Stop take the dashboard; they skip, saying what to pull, unless the engine holds the
dashboard's image and aspnet:10.0. `WpfPreviewTests` (Windows only)
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

The MCP server's tests are agents using the SDK's own client, in classes over `McpRig` (`Alone` where one debugs or holds
what it tests to a time, and those that work in its Sum project given `RestoredFolders`, taken as a test first names it):
`McpTests` starts `Passenger --mcp` and works a project through it, `McpDebuggingTests` debugs it there (with the tool
list's size and a long call's progress), `McpWindowTests` and `McpAttachTests` attach to a window with
`--mcp-attach` (its sockets in the folder named for the run's `PASSENGER_SETTINGS`) and change what the window shows,
once it lets them in and the person answers its questions (`McpWindowDebuggingTests` where an agent debugs there or
what it tests is held to a time, as `McpAttachTimingTests` hold the joins and calls given up), and `McpProcessesTests`
(`McpBuildNodesTests` for the MSBuild nodes a build leaves, which the machine's other builds would sway), `McpIdeTests`
(`McpIdeProfilingTests` for Attach Profiler's samples), `McpLanguagesTests`, `McpRefactorTests`, `McpHotReloadTests`,
`McpSessionsTests` and the container classes take the rest by what the tools reach; each checks a tool's answer, leaving
the window's own steps to the window's tests.
An agent that gives up on a call sends the protocol's "cancelled" notification itself (`CallGivingUp`): the C# SDK's
client can drop its own when the call's token is cancelled, and sends it unawaited when it does not, so the rig's client
is a `PeerTransport` too, which drops it when the test has killed its Passenger (else the next window test caught its
failure). `McpAvaloniaAppTests` drive `tests/Debuggees/AgentApp`
(headless, its popups windows of their own; `AgentAppFolder` copies and restores it once per class through
`AvaloniaApp.Debuggee`, its project `AvaloniaApp.HeadlessProject()`, as every headless app of tests/Debuggees is). The Windows on Arm VM runs a test
filter on a commit with `~/Documents/passenger-vm/vm-test.sh NAME FILTER` (66–120 min for the whole suite, by the Mac's
load); the VM can't see dot folders, and a batch file in the share runs as `powershell -Command "& cmd.exe /c '…'"`.
The Ubuntu VM ("Ubuntu 26.04 ARM64", a real GNOME desktop CI lacks) runs one with
`~/Documents/passenger-vm/vm-test-linux.sh NAME FILTER`, headless as CI's are, from `~/src/passenger-NAME`, its .NET in
`~/.dotnet` and Node.js in `~/.local/node` on the login path; a script in the share runs as `bash -l
/media/psf/Home/…`. `TemplateFamiliesTests` installs and uninstalls a test template of its own (`probetest`, from a
folder) in the run's template home while a dialog is open; the watch sees it.

## Code style

- C#: four spaces, file-scoped namespaces, braces on every block, explicit types over `var`, accessibility always
  written. `.editorconfig` makes the build fail on a block-scoped namespace, a missing brace or a missing accessibility
  (IDE0161, IDE0011, IDE0040, warnings made errors); `var` is only a suggestion there (IDE0008), which the build lets
  through, so the type is written by hand.
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
  take widths from `PopupFrame` and titles from `popupTitle`; every list, menu and popup row is `ListRow.Height` (24), and
  one a template makes hears its item only while shown (`Observable.FollowWhileShown`), never kept by it after; a
  tool window's empty state is `TextBlock.emptyText`; a row that narrows is a `ShrinkingRow`; menus' shadow room and
  placement are `MenuShadow`, scrollbars `ControlThemes.axaml`, the focus ring `FocusRing`; every Window's ground is the
  chrome. `MainWindow.SmallestWidth` is 640: the layout works at half a 1366-wide screen, and the person's first window
  and New Window fit the screen's working area before they're shown (`MainWindow.FitToScreen`). A dialog's content is
  `DialogFooter.Page(page, DialogFooter.Bar(buttons[, left]))`; a rounded Border takes `Border.kindBadge` or
  `Border.tag` (the badge radius) or `Border.box` (the controls' radius), never a literal radius (UiRulesTests); a
  toolbar down a tool window's side is a `ToolColumn`, what has no room going behind More. A list of what runs (the
  Containers and Aspire windows') draws its rows through `RunRows`, kept by key and changed in place, never made anew on
  an update, a Details tab is `DetailsTables`, and a console of
  several sources merged is `MergedLog`. A toolbar's icon button is
  `ToolButtons.Of` (`Toggle` for one that stays pressed): never focusable, its icon the one size, as UiRulesTests holds
  every tool window's; words of less weight beside a row's are `MutedText`; a search field's keys over its list (Up,
  Down, Page Up, Page Down) are `ListKeys.Move`; a path shown to a person is `PathNames.Shown` (from the open folder
  when inside it, the folder by its name, anywhere else whole); a count with its noun is `Plural.Of`.
