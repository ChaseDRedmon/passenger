# Passenger

An IDE for C# and modern .NET, inspired by JetBrains Rider, written in C# with Avalonia. Windows and macOS first.

It's early. What works: an editor of its own (a rope, multiple carets, Rider's keymaps), Roslyn's colours,
diagnostics, completion, hovers, fixes, rename and navigation through the Roslyn language server, a Solution view,
and building and running from the IDE. Its debugger, Passenger's own and started from SharpDbg, debugs over the Debug
Adapter Protocol and is next to be wired into the window. `docs/roadmap.md` says what comes next, and
`docs/architecture.md` says how it's built and why.

## Building

You need the .NET 11 SDK (the release candidate works until it ships in November 2026) and
[just](https://github.com/casey/just).

```sh
just build                 # Debug build; warnings are errors
just run                   # run it
just screenshot shell.png  # draw the main window to a PNG off-screen (--size 1600x1000 --scale 2)
just publish osx-arm64     # a self-contained Release build (osx-x64, win-x64, win-arm64 too)
```

## Licence

MIT (`LICENSE`). Third-party code and assets keep their own licences, listed in `THIRD_PARTY_NOTICES.md`.
