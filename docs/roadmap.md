# Roadmap

Each milestone ends in something you can use, and each starts by reading how Rider does it. `architecture.md` holds
the decisions behind these.

## M0: the shell (built; not yet run on Windows)

The repo, a .NET 11 Avalonia app with Rider's layout (toolbar, tool-window stripes, an Explorer pane, the editor
area, a status bar), dark and light colour tokens, a Windows build that compiles, and `--screenshot` for looking at
the UI without opening a window.

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
previous error, back and forward. Passenger's own solution loads in about eight seconds.

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

Still to do: `Passenger.Host` with MSBuild evaluation (conditions, imports, globs) for the tree; build of one project;
stderr in its own colour; launch profiles; the terminal. M2's measurement says the cached project model is needed;
it starts here once the editor features settle.

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
