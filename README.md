# Passenger

An IDE for C# and modern .NET, inspired by JetBrains Rider, written in C# with Avalonia. Windows and macOS first.

![Passenger's editor: completion open after a dot, parameter name hints before literal arguments, and the change bars of uncommitted lines](docs/images/editor.png)

It's early, but it's a working IDE: an editor of its own (a rope, multiple carets, code folding, Rider's Visual Studio
keymaps), Roslyn's colours, diagnostics, completion, hovers, fixes, refactorings, rename and navigation through the
Roslyn language server, a Solution view, building, running and unit tests, a debugger and a profiler of its own, Git,
NuGet and a terminal. An MCP server gives an agent the same IDE. The [MVP milestone](https://github.com/Zaneris/passenger/milestone/1)
lists what comes next, and `docs/architecture.md` says how it's built and why.

The pictures here are of a small sample shop, drawn by Passenger itself on Avalonia's headless platform.

### Debugging

Passenger's own debugger (started from SharpDbg, over ICorDebug): breakpoints with conditions, hit counts and log
messages, stepping, Run to Cursor and Set Next Statement, the call stack and threads, variables you can open and set,
watches and evaluation, and the values each line used drawn after it, as Rider does.

![Stopped at a breakpoint: the line's values after its code, and the Debug window's frames and variables](docs/images/debugger.png)

### Profiling

A sampling profiler like dotTrace's, over the runtime's EventPipe: a configuration profiled from its first
instruction, then its call tree, hot spots, flame graph, allocations and garbage collections, narrowed to any stretch
of the timeline. A memory snapshot counts a running program's heap by type.

![A profile of the sample's forecast: the timeline above the call tree, its hot path opened](docs/images/profiler.png)

### Git

The Commit window with its changes and unversioned files, the log with its branch graph and each commit's files,
diffs, blame annotations, change bars in the gutter that roll back a hunk, branches, push, pull and fetch.

![The Commit window beside the editor, and the Git log with a commit's details](docs/images/git.png)

### Search

Search Everywhere, Go to File, Go to Symbol and Find Action, Find and Replace in Files with a preview, Recent Files,
bookmarks, and Extend Selection through the syntax tree.

![Find in Files: every match with its file and line, the chosen one previewed below](docs/images/find-in-files.png)

## Building

You need the .NET 11 SDK (the release candidate works until it ships in November 2026) and
[just](https://github.com/casey/just).

```sh
just build                 # Debug build; warnings are errors
just run                   # run it
just screenshot shell.png  # draw the main window to a PNG off-screen (--size 1600x1000 --scale 2)
just publish osx-arm64     # a self-contained Release build (osx-x64, win-x64, win-arm64 too)
just package-mac           # Passenger.app, zipped; signed with PASSENGER_SIGN_IDENTITY, else ad hoc
```

## For agents

Passenger is a Model Context Protocol server, so a coding agent can use what the IDE knows: diagnostics, symbols,
definitions, usages, renames and fixes from Roslyn, builds and runs, unit tests, the debugger (breakpoints, stepping,
evaluation, the stopped program's variables), Git (status, diffs of unsaved text, commits, the log, branches), NuGet,
the IDE's own terminal, and screenshots of the window. Two ways to connect, shown here for Claude Code:

```sh
# Passenger with no window, its own IDE over a folder or solution
claude mcp add passenger -- /path/to/Passenger --mcp /path/to/solution
# the Passenger window you have open: the agent's breakpoints and stops show in it
claude mcp add passenger -- /path/to/Passenger --mcp-attach
```

A Passenger window listens for agents on a socket only its user can reach, unless started with `--no-mcp`. In this
repository, `.mcp.json` runs `just mcp`, which starts the last build over this folder.

## Licence

MIT (`LICENSE`). Third-party code and assets keep their own licences, listed in `THIRD_PARTY_NOTICES.md`.
