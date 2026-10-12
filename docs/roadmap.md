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

## M7: the rest of the daily loop

Git (changes, commit, diff, log, branches), the terminal, NuGet management, settings with search, keymaps, Search
Everywhere.

**Built so far:** Git's first half. The toolbar's widget names the branch and opens Rider's branch menu (New Branch,
Commit, Push, the local branches to check out; Create Git Repository outside one). The Commit tool window lists the
changes and the unversioned files with a box each, Amend (from the last message), Commit and Commit and Push; Rollback
asks, then restores and deletes; a double-click or Ctrl+D shows a file's diff. The diff is Rider's side by side, HEAD
beside the open file's own text, with bands between and an arrow on each that puts that change back. The editor's
gutter has Rider's change bars; a click on one shows what the lines were, with Rollback and Show Diff; Next and
Previous Change move between them and Rollback Lines puts one back; the Explorer colours changed files. Open files
reload from disk after a checkout or rollback and when the window comes back to the front. Still to come: the log,
pull and fetch, and word-level highlights in the diff.

## M8: depth and shipping

Hot reload and Edit and Continue, refactoring dialogs, decompiled sources with Source Link, code cleanup. Packaging:
a signed and notarized macOS `.app`, a Windows installer, and updates.
