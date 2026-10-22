# Aspire 13.6.2 upgrade

Target: update draft [PR #338](https://github.com/Zaneris/passenger/pull/338) from Aspire 13.5.4 to 13.6.2. Keep its base branch `0.2.0` and leave issue #243 open.

The earlier 193-test Linux gate applies to commit `b27ab69c81f01ba58f5160d26231bba814bab802` and Aspire 13.5.4 only. It is not evidence for this upgrade.

## Release research

The product release called “13.6” uses tag `v13.6.0`; `v13.6` is not a release tag. GitHub redirects the former `dotnet/aspire` repository to `microsoft/aspire`.

| Release | Verified commit | Relevant changes |
| --- | --- | --- |
| [13.6.0](https://github.com/microsoft/aspire/releases/tag/v13.6.0), September 29 | `56f3e9c0d216c0c7069dabb49dd0464e4827744f` | Persistent dashboard history; Native AOT dashboard; experimental AppHost-owned terminals and database REPLs; CLI `--launch-profile`; file-based C# AppHosts; resolved-environment debug hooks. Upload limits now apply consistently across clients. |
| [13.6.1](https://github.com/microsoft/aspire/releases/tag/v13.6.1), October 7 | `3751e615ad660621f2a7aa4c390ef48e08a0eed8` | Fixes metric-ingestion CPU growth, Metrics-tree scrolling, stalled DCP `ContainerExec` watches, disconnected-browser navigation, and Windows cleanup under `dotnet run`/`dotnet watch`. No new features are listed. |
| [13.6.2](https://github.com/microsoft/aspire/releases/tag/v13.6.2), October 20 | `af07e33f8d1ef733645138b3f8e36067d073148d` | Pins MessagePack 2.5.307 to fix the vulnerable 2.5.302 dependency inherited through StreamJsonRpc; otherwise release-version bookkeeping. No new features are listed. |

Further official evidence:

- [13.6 overview and breaking changes](https://aspire.dev/whats-new/aspire-13-6/): experimental terminal types move to `Aspire.Hosting.ApplicationModel` without a shim; MongoDB TLS and Cosmos emulator defaults change for applications that use them.
- [13.6.2 security backport #21156](https://github.com/microsoft/aspire/pull/21156): StreamJsonRpc itself is unchanged. Passenger already pins MessagePack 2.5.307; the upgraded fixture AppHosts must also restore the fixed transitive version.
- Dashboard persistence now retains resource/telemetry data across runs. Passenger must continue to scope reads to the owned AppHost and resource generation, mask secrets and reuse Aspire storage rather than create another telemetry store.
- AppHost-owned terminal lifetime is distinct from an attached viewer. New terminal support must not make closing a viewer terminate an unrelated owned terminal.

## Environment probe

The user-scoped CLI update succeeded. `aspire --version` reports `13.6.2+af07e33f8d1ef733645138b3f8e36067d073148d`. This confirms the installed tool, not Passenger runtime compatibility.

## Approved compatibility contract

- Pin the runtime gate, test templates, fixture AppHosts and CI CLI to 13.6.2. Older patch releases are not silently accepted, particularly before the 13.6.2 security fix.
- Refresh the resource-service proto from the exact 13.6.2 tag. A terminal prompt receives a visible native explanation and an explicit canceled terminal response. It must not disconnect ordinary interactions or invoke `CloseTerminal` on the caller-owned terminal. A native terminal viewer is outside this bounded upgrade.
- Keep project service execution and existing single-file AppHost support. Explicitly refuse file-based service `.cs` launch with a specific unsupported message rather than route it through an unowned process. Do not advertise the new opt-in `project-with-external-build.v1` capability.
- Honor an explicit single-file AppHost `LaunchProfile` with the new CLI `--launch-profile` argument. Keep profile/default resolution inside the CLI, then discover the actual authenticated endpoint from its owned dashboard resource.
- Preserve distinct credentials, loopback restrictions, payload bounds, value masking, generation checks and shutdown cleanup. Unchanged telemetry JSON is discovery evidence only, not runtime proof.

Source evidence: [IDE execution specification](https://github.com/microsoft/aspire/blob/v13.6.2/docs/specs/IDE-execution.md#project-launch-configuration), [execution routing](https://github.com/microsoft/aspire/blob/v13.6.2/src/Aspire.Hosting/Dcp/ExecutableLaunchPolicy.cs), [resource/terminal proto](https://github.com/microsoft/aspire/blob/v13.6.2/src/Aspire.Hosting/Dashboard/proto/dashboard_service.proto), and [terminal ownership checks](https://github.com/microsoft/aspire/blob/v13.6.2/src/Aspire.Hosting/Dashboard/DashboardService.cs).

Required fresh checks: real starter Run/Debug, resource authentication, describe/console/OTEL snapshots and traces, terminal refusal followed by ordinary interactions, explicit profile selection, file-service refusal, uploads, linked apps and shutdown. Restore fixture dependencies at MessagePack 2.5.307. Recheck existing affected regressions before the final solution build.

## Verified results

- **196 tests passed**, no failures or skips, including **31 Aspire acceptance cases**. Evidence: `artifacts/test-results/aspire-136-final.trx` and `artifacts/aspire-136-final-tests.log`.
- The final solution build ran after the tests and passed with **zero warnings and errors**. Logs and unique binary logs are under `artifacts/aspire-136-final-build*`.
- The fixture verifies the 13.6.2 SDK-style template root and the restored `MessagePack/2.5.307` dependency. Template downloads and fixtures now use 13.6.2; no dependency security checks were disabled.
- Explicit `--launch-profile` selection passed against a real canonical single-file AppHost with a deliberately invalid first profile and a valid selected HTTP profile. Authenticated endpoint discovery still uses the actual owned dashboard resource.
- A `.cs` service request returns HTTP 400 `UnsupportedProjectType`, does not execute its source and leaves ordinary services running. It does not fall back to an unowned process.
- Close, Escape and window dismissal each send a terminal cancellation with the current terminal/interaction IDs. Ordinary dialogs continue afterward. Queued sends and uploads use dialog cancellation; committed writes retain the shared connection lifetime.
- A generated starter with the default `AspireUseCliBundle=true` also ran through Passenger and served its weather page. The automated starter uses the per-RID SDK packages, so both launch layouts received runtime probes.
- Scale-2 captures were inspected: native terminal refusal (`artifacts/aspire-136-terminal.png`), running dark starter (`artifacts/aspire-136-dark.png`), and narrow light starter (`artifacts/aspire-136-light-narrow.png`). Temporary capture code was removed.
- Independent source review found no high/medium defects. In-editor C# diagnostics were unavailable because the language server stopped responding; fresh SDK compilation and behavioral checks provide the final evidence.

## Remaining limits

PR #338 remains a draft against `0.2.0`. Native container-window integration still depends on #242, and macOS/Windows runtime verification remains open. Terminal viewers, file-based service execution and opt-in external-build coordination are not implemented by this upgrade. Issue #243 remains open.
