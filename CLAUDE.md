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
just run [args]            # run the IDE
just screenshot X.png      # the main window drawn off-screen to a PNG (--size WxH logical, --scale N)
just publish RID           # self-contained Release build, ReadyToRun (osx-arm64, osx-x64, win-x64, win-arm64)
```

Builds land under `artifacts/` (`UseArtifactsOutput`), never beside the sources. Package versions live only in
`Directory.Packages.props`; shared properties only in `Directory.Build.props`.

**Look at UI changes.** After changing anything visible, run `just screenshot` and view the PNG at `--scale 2`
before calling it done. The capture runs on Avalonia's headless platform with real Skia drawing, so it never opens a
window or takes the focus; it shows everything but the system's own window chrome.

## Rules

- **Never use Microsoft's debugger (vsdbg) or anything from C# Dev Kit**, including Dev Kit's pieces that ship inside
  VS Code's C# extension (`.debugger`, `.roslynDevKit`, `.testDiscovery`). Their licences limit them to Microsoft's
  own editors. Nothing is copied out of an installed extension's folder; `roslyn-language-server` comes from NuGet.
- **No Avalonia Accelerate components** (TreeDataGrid and the rest need a paid licence).
- **Every dependency, icon or asset** has an open-source licence and an entry in `THIRD_PARTY_NOTICES.md`, added in the
  same change that brings it in.
- **Colours are tokens.** A control names a brush from `Theme/Tokens.axaml` (`Passenger.*`), never a literal colour,
  and every token exists in both the Dark and Light dictionaries.
- **A malformed flag or value is fatal**, never a silent default (`LaunchOptions.cs`).
- Commit messages go through a file (`git commit -F msg.txt`): backticks in a `-m` string are zsh command
  substitution.

## Code style

- C#: four spaces, file-scoped namespaces, braces on every block, explicit types over `var`, accessibility always
  written. `.editorconfig` enforces most of it in the build.
- Comments say why, or what else depends on this, never what the next lines do. A type's or member's `///` summary
  is at most five lines; an in-body comment is one line.
- XAML: four spaces; one style per selector in `Theme/Shell.axaml`, restyling Fluent's states on its
  `PART_ContentPresenter`, where Fluent paints them.
