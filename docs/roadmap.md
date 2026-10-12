# Roadmap

Each milestone ends in something you can use, and each starts by reading how Rider does it. `architecture.md` holds
the decisions behind these.

## M0: the shell (built; not yet run on Windows)

The repo, a .NET 11 Avalonia app with Rider's layout (toolbar, tool-window stripes, an Explorer pane, the editor
area, a status bar), dark and light colour tokens, a Windows build that compiles, and `--screenshot` for looking at
the UI without opening a window.

Since: tool windows dock as IntelliJ's do, at six places round the editor, each moved by dragging its stripe button
or its header (or with Move to), one shown per place, floated over the IDE or in a window of its own (View Mode),
maximized (⌘⇧', Ctrl+Shift+', or a double-click on its header), all hidden at once (⌘⇧F12, Ctrl+Shift+F12), and the
layout (floating windows' bounds too) kept for the next start. Editors split as Rider's: Split Right and Split Down
(a file twice over one text, each split with its own caret), a tab dragged onto a split's edge splitting it there and
onto another split's middle or strip moving into it (the place lighting up), Move to Opposite Group, Close Other and
Close All Tabs from the tab's menu, and a split whose last tab goes giving its room back. To come: an editor in a
window of its own, and the editor splits kept with the layout.

**Done when:** `just run` opens it on macOS and Windows.

## M1: the editor (built)

Our own text control over an immutable rope (not the piece table first planned: a rope's versions are free
snapshots for background readers), cached line layout, multiple carets, undo grouped by word, a line-number gutter,
Rider's Visual Studio keymaps with chords, C# colour from Roslyn's parser off the UI thread, find and replace, Go to
File and Go to Line. The Explorer's file tree, tabs that keep their carets and scroll, saving that keeps encoding, BOM,
line endings and permissions.

**Done when:** editing a 50,000-line file feels instant, and the editor holds a frame per keystroke. Measured
October 11, 2026, on an M2 Max, headless with Skia drawing on the CPU: a keystroke in a 50,000-line C# file, its layout
and a rendered frame took 4.2 ms at the median, 7.0 ms at p90 and 10.4 ms at worst with colour on, and about 2 ms
without.

## M2: C# intelligence (in progress)

Built: Roslyn's server bundled and started per folder, document sync, semantic colours over the parser's, diagnostics
as squiggles and faded unnecessary code, completion as you type (and Ctrl+Space), hover cards, signature help, quick
fixes and refactorings (Alt+Enter), rename, go to declaration and implementation, find usages, reformat, next and
previous error, back and forward, Go to Symbol (⌘⇧⌥T, Ctrl+Shift+Alt+T), Go to Everything (⌘T, Ctrl+T: types, then
files, then members) and File Structure (Alt+\, opening on the caret's member). Passenger's own solution loads in about
eight seconds.

Measured: the standalone server loads Orleans in 24 s and Aspire in 41 s (Dev Kit 11.0: 2.3 s and 3.0 s), the open
file answering after 11 s and 2.4 s; `architecture.md` has the table and what closing the gap takes.

Still to do: decompiled sources for library symbols; inlay hints; a usages tool window in place of the popup; commit
characters and completion item documentation.

**Done when:** a day of writing C# in Passenger needs no other editor.

## M3: solution explorer, build and run (in progress)

Built: the Solution view (the .slnx or .sln and each SDK-style project's own declarations, read without MSBuild:
folders, projects with their frameworks, dependencies, files) beside the File System view; Build (Cmd+Shift+B) with
its output in the Build window and its errors and warnings in Problems; run configurations for executable projects,
Run (Ctrl+F5) with its output in the Run window, Stop and Rerun; the bottom tool windows and their stripe buttons.

Launch profiles (`Properties/launchSettings.json`) are run configurations named "Project: profile", as Rider names
them, with their arguments, environment and working directory for Run and Debug alike.

Still to do: `Passenger.Host` with MSBuild evaluation (conditions, imports, globs) for the tree; build of one project;
stderr in its own colour; editing run configurations; the terminal. M2's measurement says the cached project model is needed;
it starts here once the editor features settle.

**Done when:** a multi-project solution builds and runs from the toolbar.

## M4: the debugger (in progress)

`Passenger.Debugger` over ICorDebug: launch and attach, breakpoints (line, conditional, hit count, tracepoints),
stepping, call stack and threads, locals and watches through Roslyn's expression compiler, exception breakpoints, hover
data tips. On macOS, the signing that attaching needs.

**Built:** the engine, Passenger's own from SharpDbg (`docs/architecture.md`, The debugger), behind Passenger's own
Debug Adapter Protocol in its own process: launch and attach, line, column, function, conditional and hit-count
breakpoints, exception breakpoints with conditions, stepping (async and lambdas included), stacks with async callers,
variables, evaluation, decompiled frames. Its 57 scenario tests pass on macOS arm64.

In the IDE: breakpoints set with F9 or a click on the line number (over the number, as Rider's new UI draws them),
following their code through edits, and drawn bound once the debugger binds them; Alt+F5 (or F5 with nothing running)
builds and launches the run configuration's assembly under the debugger; the execution line and arrow; F10, F11,
Shift+F11, Run to Cursor, Resume, Pause and Stop by Rider's keys; the Debug window (Debugger and Console tabs,
threads, frames, variables with members opened on demand, watches and an evaluated expression); the value of what
the pointer rests on while paused.

Then: inline values after each line of the stopped method, an exception stop named at the line that threw it,
breakpoint properties from a right-click in the gutter or Alt+F9 (enabled, suspend, condition, hit count, log
message), tracepoints (Shift+click, or Suspend off: a message logged with its {values}, the program running on),
conditional and tracepoint marks in the gutter, and the Breakpoints window (⌥⌘B, Ctrl+Alt+B).

Set Value: F2 or a double-click on a value types over it in its row, a C# expression assigned through the
protocol's setExpression.

Set Next Statement (Ctrl+Shift+F10, ⌘⇧F10, and `debug_set_next_statement` over MCP): the paused thread goes on
from the caret's line of the method it is in, skipping or repeating what lies between, through the protocol's
gotoTargets and goto and the runtime's SetIP, refused with the runtime's reason where the move is unsafe. Dragging the execution arrow in the gutter does the same.

**Still to do:** exception breakpoints in the Breakpoints window, smart step into, a terminal for the program's
input, and the macOS signing.
Upstream SharpDbg's open issues that reach Passenger's users are listed in `docs/architecture.md`.

**Done when:** Passenger debugs itself. It does, from the MCP server: Passenger built and launched under Passenger's
debugger, stopped in its own `Main` with its parsed options, evaluated and run to its end (October 12, 2026), which
found the two launch bugs `architecture.md` records. Still to see: the same from the window, on a window of its own.

## Alongside: agents (built)

Passenger as an MCP server (`architecture.md`, Passenger for agents): the workspace and the editor's text,
diagnostics, Go to Symbol, a file's structure, definitions, usages, implementations, rename, fixes and reformatting,
build and run, and the debugger (breakpoints with conditions, launch, step, run to a line, pause, evaluate, members,
the program's console both ways), with screenshots and the IDE's actions. Headless with `--mcp`, or attached to a
window with `--mcp-attach`. Each milestone after this adds its tools beside its UI (tests, the profiler, git).

## M5: tests (in progress)

Discovery and runs, a test tree with results, debugging a test, rerunning failures.

Built: tests found and run through VSTest (the vstest.console of the SDK the folder builds with, through VSTest's own
translation layer, so xUnit, NUnit and MSTest alike), after a build; the Unit Tests window (⌘8, Alt+8) with the tests
by project, namespace, class and method, each marked passed, failed, skipped, running or not run with its time, and a
theory's cases under their method; the chosen test's message, stack trace and output beside the tree; Run All, Run
Tests and Debug Tests at the caret or on the window's selection (⌘U R and ⌘U D, Ctrl+U R and Ctrl+U D, as Rider's Visual
Studio keymaps), Repeat the last run (⌘U U), Rerun Failed (⌘U F) and Stop. Debugging attaches Passenger's debugger to
the test host VSTest starts, before a test runs in it. The MCP server has tests_run and tests_list.

Run marks in the gutter beside each test method and class, found from the file's syntax and coloured by the last run
(Rider's green triangle, red with a mark for a failure), a click on one offering Run and Debug.

Still to do: a tree that follows edits without a run, filters and grouping, running until failure, and
Microsoft.Testing.Platform projects with no VSTest adapter.

## M6: the profiler

EventPipe sampling with a call tree, a flame graph and a timeline; allocations and GC; memory snapshots read with
ClrMD.

**Done when:** Passenger profiles its own startup.

**Built so far (October 12, 2026), and done:** Passenger profiled its own startup through its own MCP server (the
`profile` tool over Passenger.App with `--screenshot`, 0.8 s and 478 samples, `MainWindow..ctor` 42% of them).
Profile (the toolbar's gauge) builds the chosen configuration and starts it held at a diagnostic port of its own, so
the run begins at its first instruction; the Performance Profiler window follows it, Get Snapshot ends the run and the
program's own end does too. The snapshot is a call tree by thread with the hot path opened, hot spots by their own
time and a flame graph that zooms; a double-click goes to a method's source, and a .nettrace file (Passenger's or
dotnet-trace's) opens as a snapshot. Allocations lists the types allocated with each one's back traces (the runtime's
allocation ticks), and GC each collection with its generation, reason and pause. Its first find in Passenger itself:
every tooltip and menu parsed the whole keymap again, 46% of the strings allocated at startup; the keymap is now made
once. The timeline above the views shows each busy thread's samples and the collections in time; a drag across it
narrows every view to that stretch. Get Memory Snapshot, while a run goes, lists the heap's live objects by type with
their count and size: the runtime writes a dump of the heap through its diagnostic port and ClrMD reads it. Still to
come: comparing two memory snapshots, and what keeps an object alive.

## M7: the rest of the daily loop

Git (changes, commit, diff, log, branches), the terminal, NuGet management, settings with search, keymaps, Search
Everywhere.

**Built so far:** Git's first half. The toolbar's widget names the branch and opens Rider's branch menu (New Branch,
Commit, Push, the local branches to check out; Create Git Repository outside one). The Commit tool window lists the
changes and the unversioned files with a box each, Amend (from the last message), Commit and Commit and Push; Rollback
asks, then restores and deletes; a double-click or Ctrl+D shows a file's diff. The diff is Rider's side by side, HEAD
beside the open file's own text, with bands between and an arrow on each that puts that change back. The editor's
gutter has Rider's change bars; a click on one shows what the lines were, with Rollback and Show Diff; Next and
Previous Change move between them and Rollback Lines puts one back; the Explorer colours changed files. The Git
window's Log lists every branch's history beside its graph, with branches and tags, a filter by text or hash, and the
chosen commit's files (a double-click shows what it did to one) and message. Update Project pulls and Fetch fetches.
Open files reload from disk after a checkout, a rollback or an update, and when the window comes back to the front.
Annotate with Git Blame (the gutter's right-click menu, or Find Action) names each line's commit at the gutter's
left, a block at its first line, lines typed since as not committed yet, and a click chooses the commit in the log.
Still to come: word-level highlights in the diff, merge conflicts, stashes, and the log's branch tree.

The terminal is built: Rider's Terminal window (Ctrl+Alt+1, Cmd+Ctrl+1 on the Mac) with a shell per tab in the open
folder, an emulator of Passenger's own that runs full-screen programs (vim, less, htop), selection and the clipboard,
the scrollback, and the mouse for programs that ask for it. Find Action (Ctrl+Shift+A, Cmd+Shift+A) runs any action
by name, the ones without a shortcut too. Find in Files and Replace in Files (Ctrl+Shift+F and H, Cmd on the Mac) search
every text file of the folder, open files as the editor has them, matching as the Find bar does, with a preview of the
chosen match; Replace All edits open files undoably and writes closed ones. Recent Files (Ctrl+,) lists the files
opened last and reopens one where its caret was left. The status bar's path names the file and the type and member the
caret is in, as Rider's new UI does, each part a way there. Bookmarks (Ctrl+K, K to toggle, N and P to move between
them across files, W for the Bookmarks window) mark lines and move with them as they are edited. Extend Selection (Ctrl+W, Cmd+Alt+Right on the
Mac) grows every caret's selection through the code around it, from the syntax tree in C#, and Shrink Selection
takes it back. Code folding folds a member's, a type's or a namespace's braces, the usings, #regions and long
comments, from the file's own parse: Ctrl+M, M toggles the fold at the caret, Ctrl+M, S and E fold and unfold, A and X
all of them, the gutter's chevrons and a fold's placeholder too, and a caret that goes into folded code opens it. Rainbow braces colour brackets by how deeply they nest, three colours taking
turns (Settings, Editor). The Explorer's menu
adds files, folders and classes (in the namespace their folder gives), renames (F2) and deletes to the Trash (Delete),
its open tabs following, and copies or reveals a path. Settings (Ctrl+Alt+S, Cmd+, on the Mac) has Rider's search over its pages
(Appearance, Editor, Terminal, and the Keymap, where Add takes the next keys pressed, Remove unbinds and Reset goes
back to the Visual Studio keymap's), applies a change at once and keeps it beside the layout. The NuGet window (Alt+7, Cmd+7) lists the solution's packages, searches nuget.org, and installs, updates and
removes a package per project through `dotnet add` and `dotnet remove package`, central package management included.
Still to come: more feeds than nuget.org from the window, chords captured in the keymap.

## M8: depth and shipping

Hot reload and Edit and Continue, refactoring dialogs, decompiled sources with Source Link, code cleanup. Packaging:
a signed and notarized macOS `.app`, a Windows installer, and updates.

**Built so far:** decompiled sources. Go to Declaration on a library's symbol opens what Roslyn's language server makes
of it (decompiled with ILSpy, or the real source through Source Link or an embedded PDB) at the declaration, read-only
under Rider's bar saying where it came from. Code Cleanup (Ctrl+E, C or F; Cmd on the Mac) removes the unnecessary usings
and sorts the rest with the language server's own fixes, then reformats the file. Hot Reload (Settings, Run) runs a
configuration under `dotnet watch`, so each save applies its edits to the running program, and an edit that cannot be
applied restarts it. Refactor This (Ctrl+Shift+R, Cmd+Shift+R) lists the language server's refactorings at the caret or
selection, and Extract Method, Introduce Variable and Inline have Rider's own chords (Ctrl+R, M / V / I), Extract Method
then asking for the new method's name. Generate (Alt+Insert or Ctrl+N, Cmd+N on the Mac) lists what the server can write
for the type: constructors, Equals and GetHashCode, interface members, overrides. Parameter name hints show an argument's parameter before it where Rider shows them (a literal, a null or an
indexer's argument, never where the argument already says the name or the name says nothing, as Console.WriteLine's
value), from the language server's inlay hints, the caret stepping past one as a single place; Settings, Editor turns them off. Still to come: Edit and Continue while debugging, Change Signature and the other
refactorings that need a dialog, a Windows installer, and updates. `just package-mac` makes
Passenger.app from the ReadyToRun publish, with the debugger and Roslyn's server inside, signed under the hardened
runtime with the debugger's entitlements when PASSENGER_SIGN_IDENTITY names a Developer ID (ad hoc otherwise), and
notarized and stapled when PASSENGER_NOTARY_PROFILE names a notarytool profile. Passenger has an icon of its own,
drawn from `Assets/passenger.svg` (`just app-icon`), on the bundle and the window.
