# Native Aspire integration for Passenger 0.2.0

Upstream: [issue #243](https://github.com/Zaneris/passenger/issues/243). This document does not replace the owner's issue body.

## Release and compatibility baseline

- Current upgrade target: **Aspire 13.6.2**. See [release research and the upgrade gate](aspire-13.6-upgrade.md) for the 13.6.0/13.6.1/13.6.2 changes and fresh verification status. The 13.5.4 results below are historical evidence, not proof of 13.6.2 compatibility.

- Passenger base: upstream `0.2.0` at `d77d322200091a13cbc5cc8269e4ab76dfb64719`.
- Feature: `agent/243-aspire-integration`, isolated at `/tmp/opencode/passenger-243`.
- SDK: the repository's .NET 11 RC2 SDK, unchanged.
- Initial protocol baseline: Aspire **13.5.4**, the installed CLI/templates. Its source tag resolves to `9c1b401dd67746739044f68959cbf4d3d7af93a6`.
- Later releases require compatibility checks; current `main` is not a substitute for testing the selected release.
- No Visual Studio or C# Dev Kit tooling may ship. Native Avalonia views are required; embedding the dashboard is not the implementation.

## Source evidence

Official Aspire sources:

1. [IDE execution specification](https://github.com/dotnet/aspire/blob/v13.5.4/docs/specs/IDE-execution.md).
2. [Open-source DCP server](https://github.com/dotnet/aspire/blob/v13.5.4/extension/src/dcp/AspireDcpServer.ts) and [wire types](https://github.com/dotnet/aspire/blob/v13.5.4/extension/src/dcp/types.ts).
3. [Resource service schema](https://github.com/dotnet/aspire/blob/v13.5.4/src/Aspire.Hosting/Dashboard/proto/dashboard_service.proto).
4. [Dashboard client](https://github.com/dotnet/aspire/blob/v13.5.4/src/Aspire.Dashboard/ServiceClient/DashboardClient.cs), [service host](https://github.com/dotnet/aspire/blob/v13.5.4/src/Aspire.Hosting/Dashboard/DashboardServiceHost.cs), and [configuration names](https://github.com/dotnet/aspire/blob/v13.5.4/src/Shared/KnownConfigNames.cs).
5. [Dashboard configuration](https://aspire.dev/dashboard/configuration/).
6. [Aspire package versions](https://github.com/dotnet/aspire/blob/v13.5.4/Directory.Packages.props) and [MIT extension licence](https://github.com/dotnet/aspire/blob/v13.5.4/extension/LICENSE.TXT).

Passenger source traced at the release base:

- `Run/RunConfiguration.cs`, `Run/RunConfigurations.cs`: generated launch-profile configurations and workspace-relative persistence.
- `Run/LaunchPlan.cs`: one resolved process, preserving framework, environment, working directory, build and browser settings.
- `Run/BuildRunController.cs`: save/build/launch coordination, but only one ordinary run per mode.
- `Debugging/DebugController.cs`: starting another debug session currently stops the first. Hot Reload belongs to one session.
- `ProjectSession.cs`: owns workspace lifetime, live inspection and controller state.
- `Workspace/WorkspaceModel.cs`: discovers project files, not single-file AppHosts.
- `ToolWindows/ToolViews.cs`, `MainWindow.axaml.cs`: native dock registration and shared views.
- `Mcp/McpHost.cs`: discovers attributed methods on `PassengerTools` automatically.
- `CSharp/WorkspaceEdits.cs`: `Commands.Record` and `Watch` are the global-undo boundary for multi-file orchestration edits.

These are source/textual findings, not claims of exhaustive semantic references or working behavior.

## Integration contracts

### Distributed run and service ownership

Keep `LaunchPlan` per process. An Aspire app owns its AppHost, DCP instance, resource processes and subscriptions. An app group owns linked AppHosts; resource identity includes its app and generation. A resource restart must not attach stale output, stop another service, or overwrite another app's state.

Each additional debug session owns its console, debugger, Hot Reload baseline and lifetime. The Debug window selects a session; current-session commands retain ordinary behavior. Stop All and workspace shutdown must end all owned services and listeners. UI work crosses through `UiThread`; long-lived streams must not keep screenshots permanently busy.

Detect project AppHosts through evaluated `IsAspireHost`, not a filename or an `AppHost` suffix. Generate distinct Aspire configurations. A copied configuration can link other AppHosts explicitly; persist their paths relative to the workspace, like the primary project.

### DCP execution endpoint

Passenger hosts a loopback HTTP/WebSocket endpoint. AppHost/DCP receives:

- `DEBUG_SESSION_PORT`.
- A random `DEBUG_SESSION_TOKEN`.
- `DEBUG_SESSION_INFO` advertising only implemented protocol dates and launch types.

Routes are `GET /info`, `PUT /run_session`, `DELETE /run_session/{id}`, and WebSocket `GET /run_session/notify`. Requests use bearer authentication and `Microsoft-Developer-DCP-Instance-ID`. DCP instances are isolated. Session creation supplies launch configurations, arguments and environment overlays; empty values must survive. Return the protocol's `Location` and exact notification messages, not a Passenger-specific substitute.

Setting environment variables alone is insufficient: DCP must select IDE execution for project resources. Verify that selection with a real starter app.

### Resources and telemetry

The resource gRPC service exposes application information, snapshots/changes, console streams, commands and interactions. Its API key is **not** the DCP bearer token. Its header is `x-resource-service-api-key`; endpoint/key discovery and injection are separate lifecycle work.

The resource service is not a general trace store. Aspire 13.5.4's installed CLI exposes `aspire otel logs`, `spans` and `traces`, which query the dashboard telemetry API. Prefer reusing Aspire's canonical telemetry store through a verified interface over duplicating OTLP ingestion/storage. Pin the exact API, authentication and response shapes before coding this path.

Native views must provide resource state/health/URLs/endpoints/environment/relationships, resource console output with `ConsoleLinks`, structured logs, cross-service traces and error details. Open links through `MainWindow.Browse`. Prompt responses and resource commands must reach the real AppHost. The external dashboard remains an optional link.

### Security and failure boundaries

- Bind listeners to loopback. Use per-runtime high-entropy secrets, constant-time comparison and instance-scoped ownership.
- Do not log tokens, environment payloads, secret parameters or login URLs containing credentials.
- Do not disable certificate validation or make automatic system trust changes.
- Bound request bodies, queues, retained logs/traces and subscriptions. Cancel and dispose on app/workspace end.
- Build, restore, template, certificate and container failures must be visible; never show success from a parsed plan alone.
- Use `ChildProcesses` for every owned external program.

## Acceptance inventory

Every row remains open until its behavioral checks pass.

| Area | Required result |
| --- | --- |
| Configuration | Discover project and single-file AppHosts; show separate apps and linked groups; persist edits. |
| Run | DCP starts each .NET project through Passenger; build/environment/profile/arguments remain correct. |
| Debug | Concurrent service breakpoints, selection, restart, run-without-debugger, attach and Hot Reload. |
| Profile/inspection | Select a service to profile; Avalonia/WPF resource inspection has independent ownership. |
| Resources | Real state/health/URLs/environment/relationships; start/stop/restart; console links; AppHost interactions. |
| Telemetry | Native structured logs and cross-service traces, errors and source navigation; no embedded-browser substitute. |
| Code assistance | Project/resource navigation and completion; missing references/connection-string diagnostics and quick fixes. |
| Add orchestration | Create AppHost/ServiceDefaults, wire selected projects and solution, one global undo operation. |
| Tests | Aspire integration tests run/debug with service breakpoints under the test's app lifecycle. |
| Readiness | HTTPS certificate checks and offered trust; real container-engine state, coordinated with #242. |
| MCP | Start/stop apps, read resources/logs/traces, resource commands and per-service debug/profile. |
| Compatibility | Real starter behavior on Linux, macOS and Windows; explicit container-only skips if no engine. |

## Final verification contract

After implementation and review, author tests through Passenger's window/process boundaries. Use `Ui.Settle`, `Ui.WaitFor` and existing helpers, not fixed sleeps. Cover two AppHosts, concurrent service sessions, bearer failures, cross-instance requests, restart, resource commands, log/trace retrieval and cleanup. Drive a real `aspire-starter`; protocol stand-ins cannot replace that check.

Run affected regression tests and new Aspire tests, inspect scale-2 light/dark screenshots (including narrow layout), then run the final solution build with telemetry opted out and a unique binary log. Report actual platform limits. Do not close issue #243 or claim full integration from a partial slice.

## Selected implementation and review decisions

- Project AppHosts launch their resolved assemblies with SDK-provided Aspire bundle metadata. Single-file AppHosts launch through the pinned Aspire CLI. Readiness must identify a child of the owned runner, not another process using the same project path.
- Each owned AppHost has a separate authenticated DCP endpoint and resource-service key. `DEBUG_SESSION_RUN_MODE` selects Debug/NoDebug; explicit per-resource overrides support restart with or without a debugger.
- Despite the older specification table describing a bare port, 13.5.4's `DashboardEventHandlers.cs:680–691` requires `DEBUG_SESSION_PORT=localhost:<port>`. The pinned extension also publishes that form (`AspireDcpServer.ts:551`); use the executable implementation contract.
- Native resource state, console streams and prompts use the authenticated resource gRPC service. Dashboard telemetry stays in Aspire's existing store and is read through the documented CLI JSON boundary. Structured logs/traces are bounded snapshots; do not label them as continuously streamed.
- The CLI is initially pinned to 13.5.4. Later versions are not silently assumed compatible. One AppHost project path must identify one running instance; multiple distinct linked AppHosts are supported.
- C# assistance supports proven straight-line top-level AppHost graphs. Ambiguous graphs suppress diagnostics. Connection-string calls and direct `HttpClient.BaseAddress = new Uri(...)` are supported; arbitrary URI data flow is not inferred.
- Resource-service properties marked sensitive must remain masked. DCP bearer tokens, resource-service keys and telemetry credentials are distinct.
- DCP console forwarding uses a bounded queue with reserved lifecycle capacity. Under pressure, console forwarding may truncate rather than terminate the service. The local console remains bounded too.

Independent source review found and drove fixes for reordered named `AddProject` arguments, platform-specific TFM compatibility, shutdown without a live dispatcher, wrong-resource PID lookup, concurrent startup reservation and MCP cancellation. The final Linux gate below supplies behavioral evidence; inspections alone do not.

Runtime probing found two additional configuration requirements. HTTP fixtures explicitly opt in through `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true`; Passenger does not disable the SDK's transport validation. The AppHost builder replaces resource-service options from its public `ASPIRE_DASHBOARD_RESOURCESERVICE_APIKEY` input. Setting only `AppHost__ResourceService__ApiKey` is insufficient, so the owned launch now supplies the public input. An unset resource endpoint defaults to HTTPS unless the configuration explicitly permits HTTP.

The installed DCP v0.25.13 uses `os.LookupEnv` for `DEBUG_SESSION_SERVER_CERTIFICATE`. An empty value attempts certificate parsing and prevents IDE-runner registration. HTTP launches must remove that variable from `ProcessStartInfo.Environment`, including inherited values. This was confirmed by real DCP diagnostics and the matching public source (`microsoft/dcp`, `internal/exerunners/ide_connection_info.go:77–86`). Authentication and instance checks remain unchanged.

The starter regression also exposed command cancellation caused by its own live resource updates. Resource-command lifetime is now independent of telemetry refresh generations. Shutdown marks the workspace as closing before callbacks run, and native view callbacks reject ended workspaces. The final Linux tests cover these fixes.

## Concrete final test cases

1. **Starter Run:** generate the official non-Redis starter, select its HTTP AppHost profile, launch through the window, observe API/frontend resources and real PIDs, make an HTTP request, read console/structured logs and the cross-service trace.
2. **Commands and streams:** restart a resource through its advertised command; observe actual PID/generation changes and live console/state. Reject a stale command snapshot. Answer an actual AppHost prompt, including server validation and secret masking.
3. **Distributed Debug:** bind service breakpoints before launch, stop in two services without replacing the first session, select a paused sibling, and propagate a new source breakpoint after both sessions start. An old stop must not repeatedly steal selection.
4. **Resource targeting:** run the same project under different resource names, stop one, and confirm that attach/profile of the stopped sibling cannot select the other PID.
5. **Lifecycle:** Stop, workspace replacement, cancellation during startup and no-dispatcher shutdown leave no owned service, AppHost, listener or interaction stream. Simultaneous Run/Debug cannot start the same project twice.
6. **Configuration:** discover single-file SDK directives (including comments/whitespace), show two project AppHosts, persist linked relative paths, and start/stop the linked group.
7. **Assistance:** project/resource navigation, connected-name completion, missing connection diagnostics, reference quick fix and global Undo. Suppress ambiguous graphs and stale fixes.
8. **Orchestration:** generate missing projects, preserve existing source/encoding, one global Undo/Redo; avoid duplicate resource names with reordered named arguments; reject a platform-incompatible ServiceDefaults reference before writes.
9. **Integration tests and profiling:** test host and service debugger sessions coexist; test completion/cancellation closes only its scope; explicit service profiling never attaches to another process.
10. **Transport/security:** invalid bearer and missing/foreign instance identifiers, cross-instance deletion, malformed payloads, early disconnect, cancellation, queue saturation and disposal through a real process boundary.

### External integration limits

The release base has no container engine abstraction or Containers window: upstream #242 defines that separate work. This change must not pretend to reuse a nonexistent API or silently implement the whole container milestone. It can display Aspire's real container resources and probe the existing CLI. Full #243 closure still needs the #242 integration and macOS/Windows runtime checks; Linux evidence cannot establish those platforms.

## Original 13.5.4 Linux gate — 2026-10-21

The final gate passed **193 tests**, including **28 Aspire acceptance cases**, with no failures or skips. The solution build then passed with **zero warnings and errors**. These results verify the automated Linux cases, not every platform or manual acceptance row above.

```sh
AVALONIA_TELEMETRY_OPTOUT=1 ASPIRE_CLI_TELEMETRY_OPTOUT=1 dotnet test tests/Passenger.Tests/Passenger.Tests.csproj --no-restore \
  --filter 'FullyQualifiedName~Aspire|FullyQualifiedName~Ending_a_windowless_Passengers_input|FullyQualifiedName~RunConfigurationsTests|FullyQualifiedName~BuildRunTests|FullyQualifiedName~HotReloadTests|FullyQualifiedName~HotReloadEditsTests|FullyQualifiedName~ProfilerTests|FullyQualifiedName~DebuggerTests|FullyQualifiedName~UiRulesTests|FullyQualifiedName~SolutionViewTests' \
  --logger 'trx;LogFileName=aspire-final.trx' --results-directory artifacts/test-results '/bl:artifacts/aspire-final-tests-{}.binlog'
AVALONIA_TELEMETRY_OPTOUT=1 ASPIRE_CLI_TELEMETRY_OPTOUT=1 dotnet build Passenger.slnx --no-restore '/bl:artifacts/aspire-final-build-{}.binlog'
python3 tools/check-notices.py
```

Evidence is retained in `artifacts/test-results/aspire-final.trx`, `artifacts/aspire-final-tests.log`, `artifacts/aspire-final-build.log`, and unique binary logs. Notice inventory covers all 272 packages. Task-owned C# whitespace checks and `git diff --check` passed.

Final scale-2 captures were inspected: `artifacts/aspire-dark-final.png` at 1400×900 and `artifacts/aspire-light-narrow-final.png` at 900×650. Both use a real running starter. API/frontend health, the weather request, all five tabs and native resource actions were visible or exercised. The temporary capture driver and private dump/diagnostic artifacts were removed.

### Compatibility and final fixes

- Aspire 13.5.4 single-file execution requires canonical `apphost.cs` without a sibling `.csproj`. Other SDK-marked files remain discoverable but launch is refused with a version-specific reason. The runtime rechecks newly added sibling projects.
- Single-file launches use the CLI's real profile/default behavior. After verifying ownership, Passenger reads the hidden dashboard resource with `describe --include-hidden` to obtain its actual authenticated resource endpoint. This bounded raw path is internal; public snapshots still mask every environment value and unknown property. The external-starter test verifies masking and preservation of the structural project path.
- Direct `HttpClient.BaseAddress` warnings resolve the actual `Aspire.Hosting.IResourceWithServiceDiscovery` capability symbol. The missing-reference warning and stale-fix protection passed.
- Shared reference editing now inserts item groups inside a single-line project's root. The orchestration repeat/Undo/Redo cases and affected solution-view regressions passed.
- Cancellation tests pump the dispatcher while startup remains pending; they do not wait for global idle before sending cancellation. The real process and listener terminate.

### Later Aspire releases

Aspire 13.6.2 is the upgrade target. The [upgrade record](aspire-13.6-upgrade.md) compares all three 13.6 releases and records fresh runtime evidence. Unchanged telemetry JSON alone does not establish compatibility.

The refreshed protocol recognizes terminal prompts and responds with explicit cancellation and a native explanation. It does not implement a terminal viewer or close the caller-owned terminal. File-based **service** launch remains explicitly unsupported; canonical single-file **AppHosts** are supported. Passenger does not advertise the new external-build capability.
