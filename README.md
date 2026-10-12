# Passenger

An IDE for C# and modern .NET, inspired by JetBrains Rider, written in C# with Avalonia. Windows and macOS first.

It's early. What works: an editor of its own (a rope, multiple carets, Rider's keymaps), Roslyn's colours,
diagnostics, completion, hovers, fixes, rename and navigation through the Roslyn language server, a Solution view,
building and running from the IDE, and debugging with Passenger's own debugger (started from SharpDbg): breakpoints,
stepping, the call stack, variables and evaluation. An MCP server gives an agent the same IDE. `docs/roadmap.md` says
what comes next, and `docs/architecture.md` says how it's built and why.

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
