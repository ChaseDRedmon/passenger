# Architecture

Passenger is an IDE for C# and modern .NET, inspired by JetBrains Rider and written in C# end to end. It targets
Windows and macOS first. Linux should mostly come along with Avalonia, but nobody tests it yet.

This file records what was decided and why. `roadmap.md` holds the order the work lands in.

## Scope at the start

- **Modern .NET only.** Supported projects are SDK-style and run on CoreCLR (.NET 5 and later). Not .NET Framework,
  whose debugging is Windows-only; not Mono or Unity, which need a different debugger; not Xamarin.
- **C# first.** F#, Razor, XAML and other languages come later. Since the UI speaks LSP, another language's server
  can plug in when the time comes.
- **Built on .NET 11.** It ships in November 2026, and the repo builds on its release candidate until then
  (`global.json`).

## Why C# end to end

Everything a C# IDE needs already exists as a .NET library: Roslyn, MSBuild, NuGet, the debugging interfaces
(ICorDebug), EventPipe and ClrMD for profiling, VSTest and Microsoft.Testing.Platform, and ILSpy's decompiler. So the
backend is .NET whatever the UI is written in.

Rider shows the cost of splitting languages. Its frontend is the IntelliJ platform on the JVM, its backend is
ReSharper on .NET (`lib/ReSharperHost`), and a code generator (`rd-gen`) writes matching Kotlin and C# for every type
that crosses between them. JetBrains pays that because it already owned IntelliJ. Passenger has no such reason, so
both sides share C# types and nothing is generated.

A Rust UI was considered and declined. Analysis, the part of an IDE that feels slow, is Roslyn either way, and every
request crosses a process boundary either way. A Rust UI would also mean building a UI toolkit first. Typing latency
is the one place a UI's language matters, and the answer there is owning the editor control and keeping it
allocation-free per keystroke.

## Processes

Four kinds of process, so a stall or crash in one never freezes typing:

| Process | Runs | Talks to the UI over |
|---|---|---|
| **Passenger** (the UI) | Avalonia shell, the editor, every window; holds the text of open documents. | (is the UI) |
| **roslyn-language-server** | Roslyn: completion, diagnostics, fixes, refactorings, navigation, formatting; loads projects for compilation. Runs our `Passenger.Roslyn` extension assembly in-process. | LSP over stdio |
| **Passenger.Host** | MSBuild evaluation for the explorer, build, run, NuGet, test orchestration. JIT, since MSBuild needs it. | Passenger's protocol |
| **Passenger.Debugger** | One per debug session: our engine over ICorDebug. | Passenger's protocol |

Passenger's protocol is JSON-RPC, with the contract as C# interfaces and records in one shared assembly.
StreamJsonRpc (MIT, what Visual Studio uses internally) carries it.

The UI's LSP types are generated from the LSP specification's `metaModel.json`, so every request and its fields are
typed.

## Language intelligence: Roslyn's own server

The backend for C# is Microsoft's `roslyn-language-server` (MIT, built in the Roslyn repo, from NuGet). It's the same
server C# Dev Kit and VS Code's C# extension talk to. Using it rather than hosting Roslyn ourselves:

- **Gets Roslyn's internal features for free.** It has privileged access to them: signature help, inlay hints,
  semantic tokens, every fix and refactoring, metadata-as-source. A host outside Roslyn has to reach these by
  reflection, as OmniSharp did.
- **Loads projects itself,** through out-of-process MSBuild hosts (`BuildHost-netcore`, `BuildHost-net472`).
- **Runs our code inside its process.** `--extension <path>` loads extension assemblies. The handler API is
  `IExtensionWorkspaceMessageHandler` and `IExtensionDocumentMessageHandler` in `Microsoft.CodeAnalysis.Workspaces`
  (approved in dotnet/roslyn#85236, moved there by #85209, October 2026). It's experimental (`RSEXPERIMENTAL008`)
  and read-only: a handler reads the solution and cannot modify the server's workspace. A Passenger refactoring
  computes the changed solution and returns text edits, and the UI applies them like any other edit.
- **Has custom methods the UI uses.** Found in version 2.160.4 of VS Code's C# extension: `solution/open`,
  `project/open`, `workspace/projectInitializationComplete`, `workspace/_roslyn_restore`,
  `textDocument/_vs_getProjectContexts` (switching target framework), `textDocument/runTests` with
  `workspace/attachDebugger`, `textDocument/_vs_validateBreakableRange` (where a breakpoint may sit),
  `textdocument/_vs_dataTipRange` (what a debugger hover evaluates), `textDocument/_vs_onAutoInsert`, and
  `workspace/_vs_dispatchExtensionMessage` / `textDocument/_vs_dipatchExtensionMessage` (the typo is theirs).

The costs:

- **It's "early and very experimental"** in the Roslyn team's words (dotnet/roslyn discussion #82317, February 2026),
  so options and custom methods can change between releases. We pin a version, keep every custom method behind one
  adapter, and upgrade on purpose.
- **Refactorings that need options** (change signature, extract interface) need our own dialogs, wired through the
  extension.
- **Project loading is slow without Dev Kit.** Dev Kit feeds Roslyn its projects over a private channel
  (`--brokeredServicePipeName`, its closed `.roslynDevKit` assembly). Without it, the server runs a design-time build
  per project. Dev Kit 11.0 (October 6, 2026) has the active file ready in about half a second on a 407-project
  solution (Aspire), and the whole solution in 3 s. That's the bar. We measure the standalone server on Orleans and
  Aspire before building on it. Fixing it is our own project model, cached after the first load and loading the
  active file's project first.
- **MSBuild runs twice.** The Host evaluates projects for the explorer (every item, folder and package), and the
  server builds them for compilation. That's accepted to start; it goes away if we ever host Roslyn ourselves.

The swap stays possible because the UI only ever speaks the protocol.

## The editor

Our own control, drawn through Avalonia's drawing context on Skia:

- **The buffer** is a piece table (VS Code's piece tree is the model), so edits and undo are cheap at any file size.
- **Layout is per line and cached;** only visible lines are shaped and drawn.
- **No allocations per keystroke** beyond the edit and its redraw.
- **Colour:** Roslyn's semantic tokens for C#; TextMate grammars for other files later.

AvaloniaEdit (MIT, a port of AvalonEdit) is a reference, not a dependency.

## The debugger

Microsoft's debugger (vsdbg) is licensed only for Visual Studio, VS Code and Xamarin Studio, which is why JetBrains
and Samsung each wrote their own, and why we write ours:

- **The engine** is ICorDebug through `dbgshim` (MIT, from dotnet/diagnostics), with the ClrDebug bindings for C#.
  Rider ships `dbgshim` for every OS and CPU beside its own `JetBrains.Debugger.Worker`.
- **Watches and hovers** use Roslyn's expression compiler (`Microsoft.CodeAnalysis.ExpressionEvaluator.ExpressionCompiler`,
  which Rider also ships) to compile an expression into IL against the current frame; ICorDebug's function
  evaluation runs it.
- **Hot reload and Edit and Continue** use Roslyn's EnC, applied through ICorDebug or the runtime's metadata updates.
- **Reference:** Samsung's netcoredbg (MIT, speaks DAP). It's not a dependency, since its macOS arm64 build is only
  community supported.
- **macOS** needs the debugger signed with Apple's debugger entitlement.

## The profiler

- **Sampling** (CPU, allocations, GC, lock contention, exceptions) comes from EventPipe through
  `Microsoft.Diagnostics.NETCore.Client` and TraceEvent. No native code, cross-platform.
- **Memory snapshots** are a dump read by ClrMD.
- **Line-level and tracing modes** need ICorProfiler, a native interface. It can still be written in C# and compiled
  with Native AOT (Kevin Gosse's Silhouette does this).

## The UI

- **Avalonia 12** with the Fluent theme as a base, restyled by `Theme/Shell.axaml`. Every colour is a token in
  `Theme/Tokens.axaml`, with one dictionary per theme.
- **JIT, with ReadyToRun at publish.** Native AOT was tried for startup (150 ms to a first frame against 450 ms) and
  dropped: its restrictions on reflection and its Xcode dependency on macOS cost more than the 300 ms bought.
  ReadyToRun precompiles the published build with none of those restrictions, and gets it to about 290 ms.
- **Window chrome:** on macOS the toolbar is the title bar, as in Rider. Windows keeps the system title bar until the
  shell draws its own (Avalonia 12's `WindowDrawnDecorations`).
- **Icons** are Microsoft's Fluent UI System Icons (MIT), stored as path data in `Theme/Icons.axaml`.
- **No Avalonia Accelerate components.** TreeDataGrid has needed a paid licence since 11.2. Our trees and grids are
  our own and virtualized, since a profiler's call tree can run to millions of rows. Docking is ours or Dock (MIT),
  decided with the tool windows.

## Licences we hold to

- **Never** vsdbg, C# Dev Kit, or any of Dev Kit's pieces that ship inside VS Code's C# extension (`.debugger`,
  `.roslynDevKit`, `.testDiscovery`). Nothing is copied out of that extension's folder.
- **Everything we depend on** comes from NuGet or source under an open-source licence, and is listed in
  `THIRD_PARTY_NOTICES.md`.

## Sources

- Rider 2026.1.3 as installed: `lib/ReSharperHost` (the .NET backend), `rd-gen.jar`,
  `JetBrains.Debugger.Worker*`, `runtimes/*/native/*dbgshim*`, Roslyn's expression compiler, and Avalonia under
  `plugins/dotCommon` (the dotTrace and dotMemory UI).
- [A faster, lighter C# Dev Kit](https://devblogs.microsoft.com/dotnet/faster-lighter-csharp-dev-kit/) (October 6, 2026)
- [roslyn-language-server](https://github.com/dotnet/roslyn/tree/main/src/LanguageServer/roslyn-language-server)
- [Is roslyn-language-server supported?](https://github.com/dotnet/roslyn/discussions/82317)
- [Experimental extension handler API](https://github.com/dotnet/roslyn/issues/85236)
- [C# Dev Kit FAQ](https://code.visualstudio.com/docs/csharp/cs-dev-kit-faq)
- [Breaking changes in Avalonia 12](https://docs.avaloniaui.net/docs/avalonia12-breaking-changes)
- [Avalonia.Controls.TreeDataGrid](https://www.nuget.org/packages/Avalonia.Controls.TreeDataGrid/) (requires an
  Accelerate licence)
