# CLAUDE.md

Guidance for Claude Code working in this repository.

## What this is

**Passenger** is a Rider-inspired IDE for C# and modern .NET (CoreCLR only: no .NET Framework, Mono or Unity),
written in C# on .NET 11 with an Avalonia 12 UI, for Windows and macOS first. `docs/architecture.md` holds every
decision and why; `docs/roadmap.md` holds the milestones in order. Read both before starting anything larger than a
fix, so the work lands in its milestone. When a reference for how something should behave is needed, JetBrains
Rider is installed at `~/Applications/Rider.app`.

## Commands

```sh
just build                 # Debug build of the solution (Passenger.slnx); warnings are errors
just run [PATH] [args]     # run the IDE on a folder or file (--file F, --caret L:C, --theme dark|light)
just screenshot X.png ...  # the main window drawn off-screen to a PNG, after the steps given
just publish RID           # self-contained Release build, ReadyToRun (osx-arm64, osx-x64, win-x64, win-arm64)
just icons                 # rewrite Theme/Icons.axaml from the list in tools/icons.py
just test [filter]         # every test, or those whose name matches
just mcp [FOLDER]          # Passenger as an MCP server over a folder (this one by default), from a clone of the last build
```

**Use Passenger on itself.** `.mcp.json` starts Passenger's MCP server over this repository (`tools/mcp.sh`), so its
tools are the quickest way to ask Roslyn about Passenger's code (`diagnostics`, `find_symbol`, `outline`, `usages`),
to debug the IDE's own projects, and to look at the window (`screenshot`). It runs from an APFS clone of the last
Debug build, so `just build` never changes the files under it; reconnect the server to pick a new build up. A tool's
lines and columns count from 1 and its paths are the folder's. Anything it gets wrong is a Passenger bug to fix.

The screenshot is the self-check for anything visible. It opens what the command line names, takes
its steps in order through the window's real input, waits for background work to land, and captures
the compositor's own frame:

```sh
artifacts/bin/Passenger.App/debug/Passenger . --file src/Passenger.App/App.axaml.cs --caret 12:5 \
    --key enter --type "var x = 1;" --key "shift+meta+left" --click 400,300 \
    --screenshot out.png --size 1400x880 --scale 2 --crop 320,76,900,500 --theme light
```

`--key` takes "cmd+shift+L" or Rider's "meta shift L"; `--drag X1,Y1,X2,Y2` drags (`--drag-hold` keeps the button down,
to capture a tool window over its drop place); `--crop X,Y,W,H`, `--click X,Y` and `--hover X,Y` (the
pointer left to rest, for a hover card) are in logical units; `--wait MS` gives a running program time.

Headless, the dispatcher's timers (the editor's hover, a caret's blink) fire only inside a running dispatcher frame,
never from running its queued jobs: a wait that needs one pumps a frame (`Screenshot.Pump`, `Ui.WaitFor` in tests).

Builds land under `artifacts/` (`UseArtifactsOutput`), never beside the sources. Package versions live only in
`Directory.Packages.props`; shared properties only in `Directory.Build.props`.

**Look at UI changes.** After changing anything visible, run `just screenshot` and view the PNG at `--scale 2`
before calling it done. The capture runs on Avalonia's headless platform with real Skia drawing, so it never opens a
window or takes the focus; it shows everything but the system's own window chrome.

## Layout

- `src/Passenger.Text`: no UI. The rope (`Rope.cs`, an immutable B-tree, so every version is a
  thread-safe snapshot), `TextDocument` (undo, change events in LSP order), carets
  (`SelectionSet`) and every editing command as a pure function to an `EditPlan`.
- `src/Passenger.Editor`: the `CodeEditor` control (drawing, input, commands, in partial files),
  the keymap (`Keymap.cs`, chords included; defaults in `EditorActions.cs` follow Rider's Visual
  Studio keymaps) and the highlighting contract (`IHighlighter`), with JetBrains Mono embedded.
- `src/Passenger.Languages`: colour before any language server answers. C# is Roslyn's parser
  in-process, incremental and off the UI thread (`CSharpHighlighter`), classified from syntax
  alone (`CSharpClassifier`); the language server's semantic colours will layer over it.
- `src/Passenger.Lsp`: the Language Server Protocol client (StreamJsonRpc over stdio) and the
  Roslyn language server it runs, downloaded per platform at build and copied beside the app under
  `roslyn/`. `RoslynSession` opens the folder's solution; `LspDocument` keeps one document in step
  and makes requests in the editor's offsets.
- `src/Passenger.App/CSharp`: the language server in the IDE. `CSharpService` runs Roslyn for the
  folder; `CSharpDocument` gives an open file its semantic colours and diagnostics; `CodeAssist`
  is completion, hover, signature help and the code actions (`app.code.*`), its popups on a layer
  over the editor; `WorkspaceEdits` applies renames and fixes across files.
- `src/Passenger.App/Editors`: `EditorArea` is a tree of splits, each an `EditorGroup` (tabs, find bar, editor); its
  `CodeEditor` and `Active` are the active group's, so code that acts on "the editor" keeps working, and wiring for every
  editor's events goes through `ForEachEditor`, since a split makes a new one.
- `src/Passenger.App/Docking`: the tool windows' frame, in IntelliJ's six places (`DockHost`, `ToolWindow`), moved
  by dragging a stripe button or a header, the layout kept in `DockLayout` (the settings folder, or
  `PASSENGER_SETTINGS`). A new tool window is a view and one `Dock.Add` in `MainWindow.AddToolWindows`.
- `src/Passenger.App/Vcs`: Git through the `git` executable (`Git`, porcelain v2), Myers' line diff (`LineDiff`) for
  the gutter's change bars, and `VcsController`, the window's working tree: status, branch, each open file's bars,
  commit, push, update, checkout and rollback (after which open files take their text from disk); `DiffView` is the
  side-by-side diff and `CommitGraph` the log's lanes. `MainWindow.Vcs.cs` is the branch widget, the Commit and Git
  windows' places (`ToolWindows/CommitView`, `GitLogView`) and the `app.vcs.*` actions.
- `src/Passenger.App/Packages`: NuGet. `IPackageFeed` is nuget.org's v3 search and flat container (`NuGetOrgFeed`) or a
  folder of .nupkg files (`FolderFeed`, what the tests use offline); `PackageManager` changes projects through
  `dotnet add`/`remove package`. `ToolWindows/NuGetView` is the window.
- `src/Passenger.App/Settings`: `AppSettings` (kept as JSON beside the layout, only by the person's window) and Rider's
  searchable `SettingsDialog`; `MainWindow.Settings.cs` applies them live. A new setting is a property, an option row
  in the dialog and a line in `ApplySettings`.
- `src/Passenger.Terminal`: the terminal. `TerminalScreen` is the VT emulator (no UI), `TerminalView` draws it and
  encodes the keys, `TerminalSession` runs the shell in a pseudo-terminal (Porta.Pty). `MainWindow.Terminal.cs` is its
  tool window and the keys it takes ahead of the IDE's.
- `src/Passenger.App/Run` and `ToolWindows`: build and run through the .NET command line
  (`ProcessRunner` streams a process into a document; `BuildOutput` reads MSBuild's error lines),
  and the bottom panel's Build, Run and Problems windows. `Workspace/SolutionModel` reads
  solutions and projects for the Explorer's Solution view.
- `src/Passenger.App`: the shell. One window-wide keymap (`AppActions.cs`) routes IDE actions
  itself and editor actions to the focused editor. Explorer, editor tabs, files on disk.
- `src/Passenger.App/Mcp`: Passenger as an MCP server (the official C# SDK). `PassengerTools` is every tool, each
  run on the UI thread as the window's own commands are; `McpHost` hosts them three ways: `--mcp [FOLDER]` (a
  headless IDE on standard input and output), the window's socket, and `--mcp-attach [PID]` (standard input and
  output joined to that socket).
- The debugger, Passenger's own, started from SharpDbg and ICorDebugSharp (`docs/architecture.md`, The debugger):
  `src/Passenger.Debugger.Interop` (ICorDebug, IMetaDataImport and dbgshim for the COM source generator),
  `src/Passenger.Debugger` (the engine), `src/Passenger.Dap` (the Debug Adapter Protocol, both ends) and
  `src/Passenger.DebugAdapter` (the engine behind the protocol, its own process, carried under `debugger/`).

## Rules

- **Never use Microsoft's debugger (vsdbg) or anything from C# Dev Kit**, including Dev Kit's pieces that ship inside
  VS Code's C# extension (`.debugger`, `.roslynDevKit`, `.testDiscovery`). Their licences limit them to Microsoft's
  own editors. Nothing is copied out of an installed extension's folder; `roslyn-language-server` comes from NuGet.
  Nor Microsoft's `VSCodeDebugProtocol` package, whose terms are not open source: `Passenger.Dap` is the protocol.
- **No Avalonia Accelerate components** (TreeDataGrid and the rest need a paid licence).
- **Every dependency, icon or asset** has an open-source licence and an entry in `THIRD_PARTY_NOTICES.md`, added in the
  same change that brings it in.
- **Colours are tokens.** A control names a brush from `Theme/Tokens.axaml` (`Passenger.*`), never a literal colour,
  and every token exists in both the Dark and Light dictionaries.
- **A malformed flag or value is fatal**, never a silent default (`LaunchOptions.cs`).
- **Work off the UI thread that changes what is drawn goes through `BackgroundWork`**, so a
  screenshot waits for it instead of capturing a half-finished frame.
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
the line index and undo honest; keep it passing rather than narrowing it.

The debugger's tests (`tests/Passenger.Tests/Debugging`) run the adapter from the tests' own `debugger/` folder, as
the IDE does, and launch or attach to the programs in `tests/Debuggees`, whose lines and columns (tabs included) they
stop at: moving a line there means moving the tests that name it. They run one at a time (`DebugSession.Collection`). `PASSENGER_ENGINE_LOG=FILE` keeps the engine's own log, for the
tests' sessions and the IDE's alike.

The MCP server's tests (`McpTests`) are agents using the SDK's own client: one starts `Passenger --mcp` and debugs a
project through it, one attaches to a window with `--mcp-attach` and changes what the window shows.

## Code style

- C#: four spaces, file-scoped namespaces, braces on every block, explicit types over `var`, accessibility always
  written. `.editorconfig` enforces most of it in the build.
- Comments say why, or what else depends on this, never what the next lines do. A type's or member's `///` summary
  is at most five lines; an in-body comment is one line.
- XAML: four spaces; one style per selector in `Theme/Shell.axaml`, restyling Fluent's states on its
  `PART_ContentPresenter`, where Fluent paints them.
