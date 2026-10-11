# Roadmap

Each milestone ends in something you can use, and each starts by reading how Rider does it. `architecture.md` holds
the decisions behind these.

## M0: the shell (built; not yet run on Windows)

The repo, a .NET 11 Avalonia app with Rider's layout (toolbar, tool-window stripes, an Explorer pane, the editor
area, a status bar), dark and light colour tokens, a Native AOT build for macOS, a Windows build that compiles, and
`--screenshot` for looking at the UI without opening a window.

**Done when:** `just run` opens it on macOS and Windows, and `just publish-mac` produces a Native AOT binary.

## M1: the editor

Our own text control: a piece-table buffer, cached line layout, caret and selection (multiple carets included),
undo and redo, scrolling, a line-number gutter, and fast large files. Opening a folder fills a file tree in the
Explorer; clicking a file opens it in a tab; saving writes it back.

**Done when:** editing a 50,000-line file feels instant, and the editor holds a frame per keystroke.

## M2: C# intelligence

Measure first: `roslyn-language-server` on Orleans (155 projects) and Aspire (407), against Dev Kit 11.0's numbers.
Then the LSP client: open a solution, document sync, semantic highlighting, diagnostics, completion, signature help,
hover, go to definition (into decompiled code too), find usages, rename, quick fixes and refactorings, formatting,
inlay hints.

**Done when:** a day of writing C# in Passenger needs no other editor.

## M3: solution explorer, build and run

`Passenger.Host`: MSBuild evaluation into a solution tree (projects, folders, files, dependencies), run
configurations, build output and the Problems window, run with output in a console tool window. Our cached project
model starts here if M2's measurement says we need it.

**Done when:** a multi-project solution builds and runs from the toolbar.

## M4: the debugger

`Passenger.Debugger` over ICorDebug: launch and attach, breakpoints (line, conditional, hit count, tracepoints),
stepping, call stack and threads, locals and watches through Roslyn's expression compiler, exception breakpoints, hover
data tips. On macOS, the signing that attaching needs.

**Done when:** Passenger debugs itself.

## M5: tests

Discovery and runs (the server's `runTests` first), a test tree with results, debugging a test, rerunning failures.

## M6: the profiler

EventPipe sampling with a call tree, a flame graph and a timeline; allocations and GC; memory snapshots read with
ClrMD.

**Done when:** Passenger profiles its own startup.

## M7: the rest of the daily loop

Git (changes, commit, diff, log, branches), the terminal, NuGet management, settings with search, keymaps, Search
Everywhere.

## M8: depth and shipping

Hot reload and Edit and Continue, refactoring dialogs, decompiled sources with Source Link, code cleanup. Packaging:
a signed and notarized macOS `.app`, a Windows installer, and updates.
