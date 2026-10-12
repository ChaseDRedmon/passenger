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

The UI's LSP types are hand-written records for the part of the protocol Passenger speaks (`Passenger.Lsp`), with
`JsonElement` where the protocol allows several shapes; generating the whole model from `metaModel.json` waits until
another language server needs more of it.

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
- **Diagnostics are pulled per source.** Roslyn registers ten (`syntax`, `DocumentCompilerSemantic`, the analyzers,
  hot reload, ...) and one catch-all with no identifier that repeats them; Passenger pulls the named ones.
- **Project loading is slow without Dev Kit.** Dev Kit feeds Roslyn its projects over a private channel
  (`--brokeredServicePipeName`, its closed `.roslynDevKit` assembly). Without it, the server runs a design-time build
  per project. Dev Kit 11.0 (October 6, 2026) has the active file ready in about half a second on a 407-project
  solution (Aspire), and the whole solution in 3 s. That's the bar. Measured October 11, 2026 on an M2 Max (server
  5.11.0 started cold, packages restored, the time to a hover answer on a file opened at once, and to Roslyn's
  `projectInitializationComplete`):

  | Solution | Projects | Active file ready | Whole solution ready | Dev Kit 11.0 | Dev Kit 3.3 |
  |---|---|---|---|---|---|
  | Orleans | 156 | 11.2 s | 24.2 s | 0.53 s / 2.3 s | 50.3 s |
  | Aspire | 442 | 2.4 s | 41.4 s | 0.47 s / 3.0 s | 84.1 s |

  About twice as fast as the Dev Kit of a month ago, ten to fourteen times slower than today's on the whole
  solution, and on Orleans an open file waits eleven seconds for answers. Closing that gap is our own project model,
  cached after the first load, loading the active file's project first, and fed to Roslyn instead of its design-time
  builds; it needs Roslyn hosted by us or an extension point to feed projects in, so it waits until the editor
  features are settled.
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
and Samsung each wrote their own.

**Decided October 11, 2026: Passenger's own, started from SharpDbg.** SharpDbg (MIT, github.com/MattParkerDev/sharpdbg)
is a debugger written in C#: ICorDebug for processes, breakpoints, stepping and values; Roslyn's expression compiler
for watches and conditions, with a CIL interpreter beside it; DebuggerDisplay and DebuggerTypeProxy; async stacks and
stepping; decompiled frames through ILSpy. Rather than ship someone else's package, Passenger copied it in (commit
713fe40 is the copy as it was) and owns it from there:

- `Passenger.Debugger.Interop`: ICorDebug, IMetaDataImport and dbgshim declared for .NET's COM and P/Invoke source
  generators. It began as ICorDebugSharp 0.1.8, whose source is not published, recovered from its assembly; its
  public API matched the package's member for member.
- `Passenger.Debugger`: the engine (SharpDbg.Infrastructure). Its dependencies are MIT: `dbgshim` and
  `Microsoft.Diagnostics.NETCore.Client` (dotnet/diagnostics), ILSpy's decompiler, Roslyn and its expression compiler
  (packed from Roslyn's source by SharpDbg's author, since Roslyn publishes no package of it), AsyncLock, ZLinq.
- `Passenger.Dap`: the Debug Adapter Protocol, Passenger's own messages and connection, used by both ends. SharpDbg
  spoke it through Microsoft's `VSCodeDebugProtocol` package, which is not open source (its terms make a distributor
  indemnify Microsoft and bind its users to protective terms), so it is gone.
- `Passenger.DebugAdapter`: the engine behind the protocol, in its own process, as Rider runs its debugger worker: a
  debugger's native side failing ends that process, not the IDE. It takes netcoredbg's flags, so another client can
  run it, and the app carries it under `debugger/`.

Its tests came with it: 57 scenarios that launch or attach to the programs in `tests/Debuggees` and drive the adapter
over the protocol (breakpoints of every kind, stepping through async code and lambdas, stacks, variables, evaluation,
exceptions). They run one at a time, as upstream's did.

Upstream's open issues, read October 11, 2026 (upstream has had no commits since the copy), and what they mean for
Passenger:

- Fixed in Passenger: breakpoints bound by file name alone, so a same-named file in another folder or project won
  (#57; now the same file, links resolved, or a document off this disk sharing its folder and name); the debuggee
  inheriting the adapter's standard input, which is the protocol's own pipe (found in Passenger; the program has an
  input of its own, which the Console's input line writes to through an `input` request of this adapter's); Step Into
  entering `[DebuggerStepThrough]`, `[DebuggerNonUserCode]` and `[DebuggerHidden]` methods (#52) and the user-unhandled
  filter missing an exception caught in such code of the user's own assembly (#49), since Just My Code was decided by
  module only (now the runtime is told each such method is not the user's when its module loads, and asked about the
  handler's method); the thread running `Main` named `<No Name>` while a runtime thread was `Main Thread` (#59; now the
  runtime's first created thread, as netcoredbg names it); a breakpoint condition that cannot be evaluated (an unknown
  name, a throw, not a bool) passed silently (#54; now it stops, and says why on the breakpoint and in the Console);
  function evaluation with no time limit, so a getter that never returns hung the debugger (#30; now it is aborted after
  five seconds and answers that it was stopped); a lambda's captured parameter listed twice (#58; the closure's copy,
  the live one, takes the stale slot's place).
- Fixed as well: a breakpoint in a method marked not the user's stopped under Just My Code (#54's tail); now it stays
  unbound and says why, as Visual Studio does, and one in a [DebuggerHidden] method never binds. Threads with no managed
  frame are no longer listed (#59's empty runtime thread, reported on Windows; not seen on macOS).
- Fixed too: `[DebuggerDisplay]` rendered as Visual Studio does (#51, #53): fragment by fragment, strings quoted unless
  `nq` or `raw`, `\{` and `\}` as braces, a failed fragment saying so in its own place, `Name` naming a member's row
  and `Type` its type column, inherited from base types, and a value inside a display shown by its own; inner
  exceptions reported, the whole chain (#50); `RootHidden` lifting an object's members (#55); a type proxy whose
  constructor throws leaving the value's own members (#56).
- Fixed in the IL interpreter (#30): checked conversions from unsigned values; a constrained call on a struct changing
  the struct where it lives, not a boxed copy; an enum widened through its value, signed as its type is; unboxing that
  tells Pair<int> from Pair<string>, for unbox as for unbox.any; struct arrays allocated by the struct's own module, not
  a token another module's primitive shares; native integers the debuggee's width; `not` keeping an int an int.
  Left for Edit and Continue, which needs it: Roslyn's method context hard-codes method version 1.
- Planned anyway: Edit and Continue (#10, M8's hot reload), synthetic variables (#26), remote debugging (#14).

Found by Passenger debugging itself (October 12, 2026), and fixed:

- **A program is launched as netcoredbg launches one.** SharpDbg held a launched program at its start with
  `DOTNET_DefaultDiagnosticPortSuspend=1`, which every .NET program it starts inherits: Passenger's own Roslyn server,
  a `dotnet build`, any worker was held at its start too, and outlived the program, orphaned and suspended. On macOS
  and Linux the adapter now starts it with dbgshim's `CreateProcessForLaunch`, suspended before its first instruction
  with no variable to hold it, registers for its runtime's start, and lets it go (`SuspendedLaunch`). dbgshim gives
  the program the adapter's descriptors 0, 1 and 2, so the adapter moves the protocol onto descriptors of its own at
  startup and points 0, 1 and 2 at the program's pipes for the moment of the fork; its environment block is narrow
  characters, whatever the command line is. A program the debugger cannot take hold of is killed, never left held.
  Windows still launches through the diagnostic port, until it has the same start of its own.
- **mscordbi can crash tearing a session down** once the program has exited: `CordbProcess::Neuter` shuts the
  transport down and sends its session-close message through a channel the transport's worker, finding the pipe
  closed, has just set to null without the lock (`DbgTransportSession::Shutdown` and `SendMessage`, .NET 11 RC1). It
  takes the adapter with it, and with it the runtime's word that the program ended. So the adapter tells the exit itself
  as soon as `waitpid` sees it, after the program's last output, and the IDE takes an adapter that dies after that as a
  note in the Console rather than a failed session; an adapter that dies before it is reported as the debugger
  stopping. Nothing that touches the process runs after its exit either (breakpoints, handles, detach), and ICorDebug
  is shut down in its documented order.

Still ahead, in the engine or beside it: smart step into, set value, data breakpoints, tracepoints, hot reload and
Edit and Continue (Roslyn's EnC, applied through ICorDebug or the runtime's metadata updates), and on macOS the signing
with Apple's debugger entitlement that attaching to a process we did not start needs. Samsung's netcoredbg (MIT, DAP)
stays the reference for what a mature engine does.

## Tests

Tests are VSTest's to find and run, as in Rider and Visual Studio: the IDE drives the `vstest.console` that ships in
the SDK the folder builds with (`dotnet --version` there, so its global.json's), through VSTest's own translation layer
(`Microsoft.TestPlatform.TranslationLayer`, MIT), so every framework with a VSTest adapter is found with its source
locations and run with structured results. Two things about that layer are not what its API suggests: its public
constructors run a `.dll` console under the current process's executable, which is Passenger's apphost, so the internal
constructor that names the dotnet host is used; and its async calls wait for the console's handshake before they start
the console, so its blocking calls run on worker threads. Debugging a test attaches the debugger to the test host
VSTest starts and answers VSTest only once the attach has hold of it, since VSTest runs the tests at that answer.

## Version control

Git is the `git` executable, as Rider drives it: status from `git status --porcelain=v2 -z`, a file as the last commit
has it from `git show HEAD:path`, and commits, pushes and checkouts as the command line makes them, so hooks, signing,
credential helpers and the user's config all apply, and nothing in the IDE has to keep up with Git's formats. A commit
names its files (`git commit --only`), so what is checked in the Commit window is exactly what goes in, whatever else
is staged. The gutter's change bars are Myers' diff of the last commit's text against the editor's (what `git diff`
shows), worked out off the window's thread a moment after typing stops. When something else changes files (a checkout,
a rollback, another program while the IDE was in the background), every open file without unsaved changes takes its
text from disk as one undoable edit over only what differs. The diff viewer's right side is the open file's own
document, so an edit there, or a change its band's arrow puts back, is an edit to the file.

## The profiler

- **Sampling** (CPU, allocations, GC, lock contention, exceptions) comes from EventPipe through
  `Microsoft.Diagnostics.NETCore.Client` and TraceEvent. No native code, cross-platform.
- **Memory snapshots** are a dump read by ClrMD.
- **Line-level and tracing modes** need ICorProfiler, a native interface. It can still be written in C# and compiled
  with Native AOT (Kevin Gosse's Silhouette does this).

## Passenger for agents

Passenger is a Model Context Protocol server, so an agent works through the IDE rather than beside it: Roslyn's
answers about the code, builds and runs, the debugger, and the window itself.

- **The official C# SDK** (`ModelContextProtocol.Core`, Apache-2.0), with no hosting framework: tools are the public
  methods of `Mcp/PassengerTools.cs`, found by their `[McpServerTool]` attributes.
- **A tool is a user's command.** Each runs on the UI thread through the window's own code paths (OpenFile, Build,
  the breakpoint store, the debug session), so the agent and the person see one IDE in one state, and a tool cannot
  do what the window could not. Lines and columns count from 1, as the editor shows them; paths are the folder's.
- **The debugger's tools wait.** `debug_start`, `debug_continue`, `debug_step` and `debug_run_to` return when the
  program stops or ends, with the location, the stack and the top frame's locals (and an exception's type and
  message), so a stop costs an agent one call.
- **`screenshot` sees what the person would**: the frontmost window, so a diff or a dialog open over the IDE is what
  it captures.
- **Three ways in.** `--mcp [FOLDER]` is a headless IDE on standard input and output (Avalonia's headless platform
  with Skia drawing, so `screenshot` captures real frames), for an agent to start as its own. A window listens on a
  Unix domain socket under the user's local application data, in a folder only they can open (Windows has AF_UNIX
  too); `--mcp-attach [PID]` joins an agent's standard input and output to it, the newest window's by default.
  `--no-mcp` keeps a window from listening. Each connection is a server of its own over the same window.
- **Passenger works on itself through it**, from a clone of the last build (`tools/mcp.sh`), since the agent's own
  build of Passenger would otherwise replace the files the running server loads from.

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
  our own and virtualized, since a profiler's call tree can run to millions of rows.
- **Docking is ours, in IntelliJ's model** (decided October 12, 2026). Dock (MIT) supports Avalonia 12 but docks as
  Visual Studio does, by drop indicators into any nested split; Rider docks a tool window at one of six places (each
  side's upper or lower half, the bottom's left or right half), shows one window per place, and moves a window when
  its stripe button or header is dragged elsewhere, so `Docking/DockHost` does exactly that: stripes down both edges
  with a group of buttons per place, the side and bottom areas round the editor, a header per window with Move to and
  Hide, the place under a drag lit up, and the layout kept in the user's settings folder (only by the window a person
  uses; a headless IDE starts on the defaults). View Mode floats a window over the IDE's (an owned window) or gives it
an independent one, its button staying on its stripe; maximizing widens its area until it is asked back.

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
