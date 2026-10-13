# CLAUDE.md

Guidance for Claude Code working in this repository.

## What this is

**Passenger** is a Rider-inspired IDE for C# and modern .NET (CoreCLR only: no .NET Framework, Mono or Unity),
written in C# on .NET 11 with an Avalonia 12 UI, for Windows and macOS first. The work still to do is the issues of
the GitHub MVP milestone (https://github.com/Zaneris/passenger/milestone/1), each saying what Rider does and when it
is done. Read the issue before starting anything larger than a fix, and close or tick it when it lands. When a reference for how something should behave is needed, JetBrains
Rider is installed at `~/Applications/Rider.app`.

## Commands

```sh
just build                 # Debug build of the solution (Passenger.slnx); warnings are errors
just run [PATH] [args]     # run the IDE on a folder or file (--file F, --caret L:C, --theme dark|light)
just screenshot X.png ...  # the main window drawn off-screen to a PNG, after the steps given
just publish RID           # self-contained Release build, ReadyToRun (osx-arm64, osx-x64, win-x64, win-arm64)
just package-mac [RID] [--dry-run]  # artifacts/package/Passenger-macos-arm64.dmg (or -x64), checked mounted; signed
                           # with PASSENGER_SIGN_IDENTITY and notarized with PASSENGER_NOTARY_PROFILE when set, else ad hoc
just icons                 # rewrite Theme/Icons.axaml from the list in tools/icons.py (icons from a fixed commit)
just app-icon              # Passenger.icns, Passenger.ico and passenger.png from Assets/passenger.svg, by sips; fails
                           # unless every size's corners are transparent (never write the three files any other way)
just package-win [RID]     # artifacts/package/Passenger-windows-x64.exe (or -arm64), Velopack's per-user installer;
                           # signed on Windows by PASSENGER_SIGNTOOL_PARAMS or PASSENGER_ARTIFACT_SIGNING_METADATA
just notices               # fail naming any package the solution, Roslyn's server or a dotnet tool carries that the
                           # notices miss
just test [filter]         # every test, or those whose name matches
just mcp [FOLDER]          # Passenger as an MCP server over a folder (this one by default), from a clone of the last build
```

**Use Passenger on itself.** `.mcp.json` starts Passenger's MCP server over this repository (`tools/mcp.sh`), so its
tools are the quickest way to ask Roslyn about Passenger's code (`diagnostics`, `find_symbol`, `outline`, `usages`),
to debug the IDE's own projects, and to look at the window (`screenshot`). It runs from an APFS clone of the last
Debug build, so `just build` never changes the files under it; reconnect the server to pick a new build up. A tool's
lines and columns count from 1 and its paths are the folder's. Anything it gets wrong is a Passenger bug to fix.

The screenshot is the self-check for anything visible (with nothing named it shows the Welcome screen, its list
empty, since a screenshot keeps no state: unless PASSENGER_SETTINGS names a folder, its state and logs go to a
temporary one removed as it ends, never the person's own log). It opens what the command line names, takes
its steps in order through the window's real input, waits for background work to land, and captures
the compositor's own frame:

```sh
artifacts/bin/Passenger.App/debug/Passenger . --file src/Passenger.App/App.axaml.cs --caret 12:5 \
    --key enter --type "var x = 1;" --key "shift+meta+left" --click 400,300 \
    --screenshot out.png --size 1400x880 --scale 2 --crop 320,76,900,500 --theme light
```

A step after one that opens a dialog goes to that dialog, as a person's keys and clicks would. `--key` takes
"cmd+shift+L" or Rider's "meta shift L"; `--drag X1,Y1,X2,Y2` drags (`--drag-hold` keeps the button down,
to capture a tool window over its drop place); `--crop X,Y,W,H`, `--click X,Y` and `--hover X,Y` (the
pointer left to rest, for a hover card) are in logical units; `--wait MS` gives a running program time.

Headless, the dispatcher's timers (the editor's hover, a caret's blink) fire only inside a running dispatcher frame,
never from running its queued jobs: a wait that needs one pumps a frame (`Screenshot.Pump`, `Ui.WaitFor` in tests).

Builds land under `artifacts/` (`UseArtifactsOutput`), never beside the sources. Package versions live only in
`Directory.Packages.props` (Roslyn's server's too, as `RoslynServerVersion`); shared properties only in
`Directory.Build.props`. Packages come from nuget.org alone, as nuget.org signed them (`nuget.config`), at what each
project's `packages.lock.json` pins: a restore that adds or changes a package rewrites the lock file, committed with
the change, and CI restores in locked mode. Every `just` recipe and CI set `AVALONIA_TELEMETRY_OPTOUT=1`, which
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
  group per child, which a watchdog shell kills once Passenger's pipe to it closes). `DotnetCommand` builds without
  MSBuild's server (which Stop could not reach) and in nodes salted to this IDE, which end with it. `Dotnet`,
  `Programs` and `ShellEnvironment` find the tools as the login shell would; `PathNames` makes paths canonical
  (`realpath`, or `GetFinalPathNameByHandle` on Windows) and answers `IsUnder` for every "is this inside that folder".
- `src/Passenger.Text`: no UI. The rope (`Rope.cs`, an immutable B-tree, so every version is a
  thread-safe snapshot), `TextDocument` (undo, change events in LSP order), carets
  (`SelectionSet`) and every editing command as a pure function to an `EditPlan`.
- `src/Passenger.Editor`: the `CodeEditor` control (drawing, input, commands, in partial files),
  the keymap (`Keymap.cs`, chords included; defaults in `EditorActions.cs` follow Rider's Visual
  Studio keymaps) and the highlighting contract (`IHighlighter`), with JetBrains Mono embedded.
  An inlay hint (`CodeEditor.Hints.cs`) is a parameter's name, which goes with the text after it, or a type, which
  goes with the text before it; the hints at an offset are a caret place of their own, as IntelliJ's inlays are.
- `src/Passenger.Languages`: colour before any language server answers. C# is Roslyn's parser
  in-process, incremental and off the UI thread (`CSharpHighlighter`), classified from syntax
  alone (`CSharpClassifier`); the language server's semantic colours layer over it. `CSharpHighlighter` is also each
  open file's one syntax service (`OpenDocument.Syntax`: the newest tree, carried through the edits since): a feature
  that reads C# syntax (templates, postfix, test marks, selection ranges, inline values) uses it, never a fresh parse.
  `TypeHints` turns Roslyn's type hints into Rider's (after the name, left off where Rider calls the type evident).
  A key costs what it changed, not the file's size: folds (`FoldRegions`) and bracket depths come from the parse
  before through the nodes Roslyn reused, never from a walk of the whole tree, and the server's colours are asked for
  the screen's lines first and the whole file only once typing stops.
- `src/Passenger.Lsp`: the Language Server Protocol client (StreamJsonRpc over stdio) and the
  Roslyn language server it runs, downloaded per platform at build and copied beside the app under
  `roslyn/`, less the four Visual Studio assemblies it never loads (`RoslynServerLeftOut`; the build fails if the
  package stops carrying one). `RoslynSession` opens the folder's solution; `LspDocument` keeps one document in step
  and makes requests in the editor's offsets. A server that stops, restarts or refuses a request is one exception,
  `LanguageServerFailed`, which callers catch; StreamJsonRpc's own types never leave the project.
- `src/Passenger.App/CSharp`: the language server in the IDE. `CSharpService` runs Roslyn for the
  folder; `CSharpDocument` gives an open file its semantic colours and diagnostics; `CodeAssist`
  is completion, hover, signature help and the code actions (`app.code.*`), its popups on a layer
  over the editor; `WorkspaceEdits` applies renames and fixes across files.
- `src/Passenger.App/Editors`: `EditorArea` is a tree of splits, each an `EditorGroup` (tabs, find bar, editor); its
  `CodeEditor` and `Active` are the active group's, so code that acts on "the editor" keeps working, and wiring for every
  editor's events goes through `ForEachEditor`, since a split makes a new one. An editor window (Open Tab in New Window,
  or a tab dragged out) is an `EditorFrame` of the same `EditorArea`, never a second area, so a file shown in two
  windows is one `OpenDocument`; the folder keeps the windows in `WorkspaceState.EditorWindows`.
- `src/Passenger.App/Docking`: the tool windows' frame, in IntelliJ's six places. `DockHost` is the layout and what
  it keeps; `Stripe`, `ToolPane` and `FloatWindow` (a window of its own that knows nothing of tool windows) are its
  parts. A window moves by dragging a stripe button or a header (`DockDrag` says where it lands); `DragGesture` and
  `DropOverlay` are the one drag and drop zone, which editor tabs share. The layout is kept in the state store as a
  `DockLayout`, with the folder open and as the one a folder without its own opens on. A new tool window is a view and
  one `Dock.Add` in `MainWindow.AddToolWindows`.
- `src/Passenger.App/State`: the IDE's own state, in one `StateStore` under `PASSENGER_SETTINGS` or the user's local
  application data, kept only by the person's window: the app's documents at its top (`settings.json`, `layout.json`,
  `recentSolutions.json`, `updates.json`)
  and each folder's under `workspaces/<name>-<hash>/` (editors and recent files, layout, breakpoints, bookmarks, run
  configurations, the profiler's `snapshots/`). A
  `StateDocument` is a record that is an `IStateDocument`: its `Upgrades` from each older version and its `Clamped` value;
  it is written debounced off the UI thread through a temporary file, merged over the file so values it does not know
  stay (and an entry another window removed stays removed), and a file that does not read is set aside as `.bad` and logged. New state is a document there, never a file of
  its own.
- Logs: one folder, `logs/` under the state root, written by `IdeLog` off a queue with every file rotated by size:
  `passenger.log` (IDE errors, the language server's warnings and errors, each folder's startup phases with times),
  `roslyn/` (the server's own logs), `lsp.log`, `dap.log` and `vstest.log`. Settings | Diagnostics turns on protocol
  traces (`TraceProtocols`) and a line for every program started (`LogEveryProgram`; otherwise only failing or slow
  ones); Help | Show Log opens the folder. What the language server says to show reaches the status bar.
- `src/Passenger.App/Vcs`: Git through the `git` executable (`Git`, porcelain v2), Myers' line diff (`LineDiff`) for
  the gutter's change bars, and `VcsController`, the window's working tree: status, branch, each open file's bars,
  commit, push, update, checkout and rollback (after which open files take their text from disk); `DiffView` is the
  side-by-side diff and `CommitGraph` the log's lanes. `MainWindow.Vcs.cs` is the branch widget, the Commit and Git
  windows' places (`ToolWindows/CommitView`, `GitLogView`) and the `app.vcs.*` actions.
- `src/Passenger.App/Packages`: NuGet. `IPackageFeed` is nuget.org's v3 search and flat container (`NuGetOrgFeed`) or a
  folder of .nupkg files (`FolderFeed`, what the tests use offline); `PackageManager` changes projects through
  `dotnet add`/`remove package`. `ToolWindows/NuGetView` is the window.
- `src/Passenger.App/Settings`: `AppSettings` (the store's `settings.json`) and Rider's searchable `SettingsDialog`;
  `MainWindow.Settings.cs` applies them live. A new setting is a property, an option row in the dialog, a line in
  `ApplySettings`, and its range in `Clamped` when it has one.
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
- `src/Passenger.Profiler`: the profiler with no UI. `ProfilingSession` starts a program held at a diagnostic port of
  its own and streams EventPipe to a .nettrace; `Snapshot` reads it (TraceEvent) into call trees and hot spots.
  `App/Profiling` is the window (`ProfilerView`, `FlameGraph`), `MainWindow.Profiler.cs` the Profile action.
- `src/Passenger.Terminal`: the terminal. `TerminalScreen` is the VT emulator (no UI), `TerminalView` draws it and
  encodes the keys, `TerminalSession` runs the shell in a pseudo-terminal (Porta.Pty). `MainWindow.Terminal.cs` is its
  tool window and the keys it takes ahead of the IDE's.
- `src/Passenger.App/Run` and `ToolWindows`: build and run through the .NET command line
  (`ProcessRunner` streams a process into a document; `BuildOutput` reads MSBuild's error lines),
  and the bottom panel's Build, Run and Problems windows. Every console (Run, Build, Debug, the profiler's) is written
  through `ConsoleSink`: a frame's output at a time, colour codes stripped whole across reads, the head trimmed past
  Rider's 1024 KB, and what is typed into it sent to the program. A run configuration is data (`RunConfiguration`: the
  folder's own in `runConfigurations.json`, the rest made from the executable projects' launch profiles, those that
  cannot launch here saying why); `LaunchPlan.ResolveAsync` works one out once, in one MSBuild request, into what Run,
  Debug, Profile, Hot Reload and a debugged test host start, and `RunConfigurationsDialog` is Run > Edit
  Configurations. A test project's first Project launch profile gives its variables to its tests.
- `src/Passenger.App/Testing`: unit testing works in Passenger's own `TestItem` (its id the project and full name, plus
  a data row's name) and `TestOutcome`, behind `ITestRunner`; `VsTestRunner` is the only file that knows VSTest's
  object model. `TestFinder` shows a solution's tests from code as it opens, before any build: test projects' files
  parsed off the UI thread, an open file's from its own parse (`TestsAtCaret.Read` is the one reading of a file's
  tests), changed ones through the folder watcher; discovery and runs merge into those rows by id.
  `MainWindow.Tests.cs` wires it.
- `src/Passenger.App/Workspace`: `SolutionModel` reads solutions and projects for the Explorer's Solution view, and
  `SolutionEdits` is every change to a .sln or .slnx (solution folders, the files they list, projects in and out): a
  .sln line by line and a .slnx through XDocument with its whitespace kept, so the rest of the file stays byte for byte
  as it was; never `dotnet sln`, which rewrites a whole .sln. `MainWindow.Solution.cs` is the Solution view's New
  Solution Folder, Existing Project, Existing Item, Rename and Remove.
  `EditorConfig` reads the .editorconfig files over a file (its own parser and globs); `FileIndent` picks a file's
  indent (its .editorconfig, else `Indentation.Detect` on its lines, else its language) and names it in the status bar;
  `OpenDocument.Save` applies the save-time properties. Every edit from the language server goes through
  `LineEndings.Fit`, then `Indentation.Fit`.
- `src/Passenger.App`: the shell. Explorer, editor tabs, files on disk. Each action is an `IdeAction` (id, title,
  group, menu places, `Run`, `WhyNot`, whether its keys work while the terminal has them) that its feature adds to the
  window's `ActionRegistry`: `MainWindow.Actions.cs` for the window's own, a partial's or controller's `Add…Actions`
  for the rest, and a `ToolWindow`'s `Action` for its show action. `AppActions.cs` holds the ids and each platform's
  keymap. Keys, the main menu (`MainMenu.Build`), Find Action, context menus and MCP's `ide_action` all run actions
  through the registry, so one that cannot run says why; editor actions go to the focused editor.
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
  bar says by then; `McpHost` hosts them
  three ways: `--mcp [FOLDER]` (a headless IDE on standard input and output), a window's own socket under the state
  folder (`AgentSocket`, open only while the person's Enable MCP Server setting is on), and `--mcp-attach [PID]`
  (standard input and output joined to the window over the agent's folder). On a person's window a tool neither
  `ReadOnly` nor `Destructive = false` asks them the first time an agent uses it (`ConnectedTool`).
- The debugger, Passenger's own, started from SharpDbg and ICorDebugSharp (MIT; `THIRD_PARTY_NOTICES.md` says what came from where):
  `src/Passenger.Debugger.Interop` (ICorDebug, IMetaDataImport and dbgshim for the COM source generator),
  `src/Passenger.Debugger` (the engine), `src/Passenger.Dap` (the Debug Adapter Protocol, both ends) and
  `src/Passenger.DebugAdapter` (the engine behind the protocol, its own process, carried under `debugger/`).
- `tools/Passenger.Notices`: writes the app's `THIRD-PARTY-NOTICES.txt` (beside `LICENSE.txt` in every build and
  publish, through `WriteNotices` in `Passenger.App.csproj`) from the packages the build copies, the self-contained
  runtime pack, the debugger's and Roslyn's server's `.deps.json`, each package's licence and copyright, and the part
  of THIRD_PARTY_NOTICES.md after its marker line; a licence it cannot write fails the build.
- `site/`: passenger-ide.net, plain HTML and CSS, deployed to Cloudflare Pages by `.github/workflows/site.yml` on a push that
  touches it. Its downloads are the latest GitHub release's assets, by fixed names (#56); its share card,
  `images/social.png`, is drawn from `tools/social-card.html` (the command is in its first comment).
- `.github/workflows`: `test.yml` is every test on macOS and Windows, called by `ci.yml` (each push and pull request,
  with the packages' and notices' jobs) and by `release.yml`, which a pushed `vX.Y.Z` tag runs: the tests, then both
  disk images and both Windows installers at the tag's version, `SHA256SUMS` and Velopack's feeds, published as the
  release under the fixed names the site and the updater link to. Its Run workflow with dry run builds it all and
  publishes nothing.

## Rules

- **Never use Microsoft's debugger (vsdbg) or anything from C# Dev Kit**, including Dev Kit's pieces that ship inside
  VS Code's C# extension (`.debugger`, `.roslynDevKit`, `.testDiscovery`). Their licences limit them to Microsoft's
  own editors. Nothing is copied out of an installed extension's folder; `roslyn-language-server` comes from NuGet.
  Nor Microsoft's `VSCodeDebugProtocol` package, whose terms are not open source: `Passenger.Dap` is the protocol.
- **No Avalonia Accelerate components** (TreeDataGrid and the rest need a paid licence).
- **Every program Passenger starts goes through `ChildProcesses`** (`Command` or `DotnetCommand`, then `Start` or
  `RunAsync`; `Kill` ends it with all it started), so none outlives the IDE. Only the terminal's shell (Porta.Pty's,
  ended by its pseudo-terminal's hangup), vstest.console (VSTest's, which watches the IDE) and, on Windows, Velopack's
  Update.exe (started as Passenger closes with an update ready, since it must outlive Passenger to replace it) start
  elsewhere. macOS's update runs nothing after Passenger: it swaps the app in as the process ends and starts the new
  one through LaunchServices (`open`, through ChildProcesses).
- **Every dependency, icon or asset** has an open-source licence and an entry in `THIRD_PARTY_NOTICES.md`, added in the
  same change that brings it in: a package by its NuGet id in backticks, with its licence and copyright as the package
  states them (`just notices`, and CI, fail on one missing), copied source by its file with the project it came from.
  The one exception, decided by the owner: `Microsoft.VisualStudio.Telemetry` and
  `Microsoft.VisualStudio.Utilities.Internal`, under Visual Studio's terms, stay inside Roslyn's server (MIT on nuget.org)
  because its built-in Razor support needs them; THIRD_PARTY_NOTICES.md says so. Nothing else under such terms ships. A new SPDX licence needs its text in
  `tools/Passenger.Notices/Licenses`, a package inside Roslyn's server a row in THIRD_PARTY_NOTICES.md's roslyn/ table,
  and copied source or an asset its entry after that file's marker line, so the app carries them.
- **Colours are tokens.** A control names a brush from `Theme/Tokens.axaml` (`Passenger.*`), never a literal colour,
  and every token exists in both the Dark and Light dictionaries.
- **A malformed flag or value is fatal**, never a silent default (`LaunchOptions.cs`).
- **Work off the UI thread that changes what is drawn goes through `BackgroundWork`**, so a
  screenshot waits for it instead of capturing a half-finished frame: `BackgroundWork.Run` rather than a bare
  `Task.Run` (one that must not hold screenshots says why in a comment), and `Debounce` rather than a
  `DispatcherTimer` for a pause before such work, since a headless timer never fires while a screenshot settles.
- **One threading rule.** Controls, the documents editors show, controllers and views belong to the UI thread
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
the line index and undo honest; keep it passing rather than narrowing it. One test of something a person sees or
gets is worth more than many that restate the code they call: a test that only checks one type or function on its own
is deleted, and what it covered tested through the window instead. `BannedSymbols.txt` makes that a build error
(RS0030) for the internals tests used to drive directly (the terminal's emulator, a Roslyn or profiling session started
by hand, a lone `TextDocument`, `LineDiff`, `CommitGraph`, `Snippet.Parse`); the editor rig in `Rig.cs` is the one
exception.

A run keeps to a folder of its own (`TestRun`, set up by a module initializer before anything starts): the IDE's state
(`PASSENGER_SETTINGS`: its log, the language servers' logs, agents' sockets, profiler snapshots), every test's
folders (`TestFolders.Create`) and the shells' history, with an empty Git configuration in place of the person's
(`GIT_CONFIG_GLOBAL`, `GIT_CONFIG_NOSYSTEM`). Nothing changes those variables afterwards; a test that needs fresh
state gives the window a folder of its own (`MainWindow.StateFolder`). So a run never touches the person's settings,
log, history or Git signing and hooks. No test reaches GitHub: the run points `PASSENGER_RELEASES` at a dead loopback
address, and a test that needs releases gives its window a `Releases` feed of its own (`UpdatesTests`); one that tests
an update gives its window the updater of a copy installed in a test folder, and no other window has one. After every window test, pass or fail, the windows it left open are closed,
what they started is waited out and its folders are removed (`TestsLeaveNothingBehind`), so one failure never
cascades into the next test's waits. The run sets its own nice value to 0 when it starts below it (GitHub's macOS
runner starts at −10), since MSBuild keeps no idle node at any other; a test that needs one skips, saying why, when
the run is above 0 and cannot come down (`Nice`).

The debugger's tests (`tests/Passenger.Tests/Debugging`) run the adapter from the tests' own `debugger/` folder, as
the IDE does, and launch or attach to the programs in `tests/Debuggees`, whose lines and columns (tabs included) they
stop at: moving a line there means moving the tests that name it. They run one at a time (`DebugSession.Collection`). `PASSENGER_ENGINE_LOG=FILE` keeps the engine's own log, for the
tests' sessions and the IDE's alike.

The MCP server's tests (`McpTests`) are agents using the SDK's own client: one starts `Passenger --mcp` and debugs a
project through it, others attach to a window with `--mcp-attach` (its sockets under the run's
`PASSENGER_SETTINGS`) and change what the window shows, once it lets them in and the person answers its questions.
An agent that gives up on a call sends the protocol's "cancelled" notification itself (`CallGivingUp`): the C# SDK's
client can drop its own when the call's token is cancelled.

## Code style

- C#: four spaces, file-scoped namespaces, braces on every block, explicit types over `var`, accessibility always
  written. `.editorconfig` enforces most of it in the build.
- Comments say why, or what else depends on this, never what the next lines do. A type's or member's `///` summary
  is at most five lines; an in-body comment is one line.
- XAML: four spaces; one style per selector in `Theme/Shell.axaml`, restyling Fluent's states on its
  `PART_ContentPresenter`, where Fluent paints them.
