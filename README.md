# Passenger

An IDE for C# and modern .NET, inspired by JetBrains Rider, written in C# with Avalonia. Windows and macOS first.

It's at the very start: the window and its layout exist, and nothing in them works yet. `docs/roadmap.md` says
what comes next, and `docs/architecture.md` says how it's built and why.

## Building

You need the .NET 11 SDK (the release candidate works until it ships in November 2026) and
[just](https://github.com/casey/just).

```sh
just build                 # Debug build; warnings are errors
just run                   # run it
just screenshot shell.png  # draw the main window to a PNG off-screen (--size 1600x1000 --scale 2)
just publish-mac           # Native AOT build for macOS (needs Xcode, not just the Command Line Tools)
just publish-win           # Native AOT build for Windows, run on Windows
```

## Licence

None has been chosen yet, so for now all rights are reserved. Third-party code and assets keep their own licences,
listed in `THIRD_PARTY_NOTICES.md`.
