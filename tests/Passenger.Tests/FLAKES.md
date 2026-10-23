# Flake ledger

Each test failure on CI (or a VM) once its cause is known: the test, what it said, and which of two things it was, so a
pattern shows. A **race in the IDE** is fixed in the IDE; waiting longer or trying again would only have hidden it. A
**test waited wrong** is fixed in the test, by waiting for what it checks (`Ui.WaitFor`, `Ui.PumpUntil`,
`Waiting.Until`), never by a longer fixed time. A failure that was neither says what it was.

To add one: a line under its date, newest first, naming the test, the run, the cause in a sentence and the commit
that fixed it, in one of the forms below. A failure not yet understood goes in only once it is.

## 2026-10-22

- `AspireCodeHelpTests`, every test, five of some twenty class starts on the Mac while fixing the three below, loaded or
  alone: "Class fixture type … threw in its constructor … The file '…/Worker/obj/Worker.csproj.nuget.g.props' already
  exists". Its AppHosts' references name Api and Worker by their real paths (/private/var/folders), the solution, restored
  through the temporary folder's link, by /var/folders, so dotnet restore restored each twice at once. Neither: **the
  test's restore**, fixed in 8dad2ce9 (restored by the folder's real path). AspireApp's starter, an AppHost too, is still
  restored through the link and has not yet failed so.
- `AspireCodeHelpTests`' connection warning's fix, the Mac's full run through `just test` at 1dfdfb92 (first phase, three
  processes side by side), as once before in a loaded batch: "Waited 135 s for the native fix to add the missing AppHost
  edge". The window refused the chosen fix ("source changed; request the fix again") whenever the Aspire graph's version
  had moved since it was offered, and every `workspace/diagnostic/refresh` moves it, which the server sends for any change
  in the solution; under load one landed while the fix was offered. Another file changed while it is offered failed it
  every time. **Race in the IDE**, fixed in 84d6688a (the server works the edge out again as it is chosen, so only the
  service's own text changing refuses it; the test now changes another file while the fix is offered).
- `TestingPlatformTests`' Stop as a debugged run's test host starts, the same run (its Alone phase): "Tests passed: 0,
  failed: 0 of 0 in 1.2 s, stopped" where it expected "The tests could not run: Debugging Tests was stopped as it
  started". No race: every run since 2eb347b4 failed it, which gave the debugger's start of a test host the run's own
  cancellation, so Stop, cancelling the run before it stops the debugger, ends the run as stopped, as at any other moment
  of a run; the debugger's own word then landed after the run's in the status bar. **The test** (its words from before)
  and **a race in the IDE** (the order of the two words), fixed in 96f975ef (a start given up ends before its caller
  hears so).
- `ExplorerFilesTests`' delete the Trash refuses, the same run (first phase): "Waited 135 s for Notes.txt deleted and its
  tab closed". The test clicked Notes.txt's row again to give the Explorer the keys back after typing; when the steps
  between took less than Avalonia's double-click time, as they did on the loaded machine (one run in two under twelve
  busy loops; it passed alone), the click was a double click (its ClickCount 2), which opened the file and gave the editor
  the keys, so Backspace deleted a character there. **The test**, fixed in 5442629c (Cmd+1, Focus Explorer, gives the keys
  back).
- `ContainerRunDebuggingTests`' Hot Reload of a paused service, the Mac's 24-minute run of the container, MCP, profiler
  and debug-session classes: "The file should have '"web 1"' to replace" with Program.cs's text, the editor gone to web's
  stop just after the test had opened Message.cs. The window shows a stop once the debugger has answered its threads and
  then its frames, which was no background work: the test's wait (the session paused, and its own frames, asked after the
  window's threads and so answered first) and the settle after it opened Message.cs both ended before the window's frames
  came. Holding the window's frames back until Message.cs opened failed it the same way, and with the fix it passed.
  **Race in the IDE**, fixed in 4d1e4017 (a stop is background work until the editor is at it, so every wait for a
  quiet IDE waits for it; the test settles after the stop before it acts).
- `McpTests`' attached diagnostic shut out (`revokeConnection: True`), the same run: "An error occurred invoking
  'diagnostics'." where the agent should hear that the connection to the window ended. The proxy's MCP client fails a
  call still waiting as the connection's reader stops, and says the session is complete a moment later on another of the
  pool's threads (the SDK's code read); the proxy looked at that word as the failure came, so when a busy machine ran the
  failure first, the IOException went to the SDK, which answers any such with that line. Not reproduced: 30 runs alone,
  and 3,300 of the SDK's client hung up on mid-call over a Unix socket, idle and with the pool busy, all heard the end
  first. **Race in the IDE**, fixed in a45a1abf (a call's failure waits up to 5 s for the connection's end to be told).
- `ProtocolTests`' header past the adapter's limit (`sent: "header"`), the Windows VM: "The pipe has been ended". The
  adapter stops reading at 64 KB and ends, as it should, and Windows' pipe ends under the rest of the test's 80 KB write,
  where macOS's and Linux's larger buffers take the rest first. Neither: **the test took the adapter's end for a failure**, fixed
  in 65dded10 (a write cut short is what that header expects; the adapter's end and its log's reason are still checked).

## 2026-10-21

- `ContainersTests`' no docker or engine not running, the Mac beside two agents' Docker test runs: "The IDE was not
  quiet within 90 s", background work begun at ProcessRunner.cs:237, at its first settle. The test before it had closed
  a window streaming a container's log, and the stream's end was heard only once this test had begun: the console's
  word of it went to the earlier test's dispatcher, shut down by then, which dropped it, so the hold it was to let go of
  stayed (an end heard late, put in by hand, did it every time). **Race in the IDE**, fixed in fe11c0e7 (work the window
  will never run lets go of its hold; and a program whose output a job it left still holds is ended with that job after
  ChildProcesses' grace, as `RunAsync`'s is, rather than holding the console for good).

## 2026-10-20

- `TypeRenameTests`' form and partial class renames, the Mac, three times in four class runs once: "Form1.cs changed on
  disk as the language server worked this out", the test writing those files about 2 s before the rename it asked for,
  which refuses a file not open written within 2 s (`WorkspaceEdits.StaleAsync`). **Test waited wrong**, fixed in
  8933a612 (the files are the project's from the start).
- `TablesTests`' CSV Text, TSV copy and sheet choice, Ubuntu VM: "Sequence contains no matching element" for a segment,
  or a click a row below its cell, the table read but its bar and grid not yet laid out when the wait returned: a small
  file's read can end before its await, so `Ui.WaitFor` found it there at once and ran none of the window's jobs.
  **Test waited wrong**, fixed in 00ba1dae (Open waited for both to be arranged), then for every test in e835e3e2 (every
  wait ends with the window laid out and drawn, and Open no longer waits itself).

## 2026-10-19

- `EvalTests`' LINQ watch, CI Linux 39301973028: "The expression took too long to evaluate", a
  program's first LINQ evaluation slower than the 1 s the run gave the debugger. **Test waited wrong**, fixed in
  52bdcaf7 (the run gives 3 s).
- `CodeAssistTests`' long-file colours, CI macOS 39278571192: the whole file's colours asked twice, the server's word
  that they moved taken as newer than a request it had already read. **Race in the IDE**, fixed in b45ecf0b (where the
  request crossed to the server, not where the UI thread sent it), after e3802ba4 (the word counted as read).
- `CommandLineTests`' screenshot agent step, CI macOS 39278571192: still running after two minutes, a Terminal tab
  missing its program's end told before it listened. **Race in the IDE**, fixed in e69d3c10.
- `McpTests`' Change Signature after a write: refused as "changed on disk as the language server worked this out".
  **Race in the IDE** (the agent's call refused what the server had not yet read), fixed in e030d520.
- `McpTests`' run with Hot Reload, CI on all three systems: "its working folder .../bin/Debug/net10.0 does not exist",
  the run checked before dotnet watch's own first build made it. **Race in the IDE**, fixed in 8e487216.
- `BuildRunTests`' Avalonia hold, CI Windows 39265497664: AVLN9999, the test's stand-in leaving an empty resources
  file. Neither: **the test's stand-in was wrong**, fixed in 0f56f8c5.
- `LogsTests`' servers' end, CI Windows: a line of another program whose process id Windows had reused. **Test waited
  wrong** (it read the whole run's log, not its window's), fixed in 43b621c6.
- `UpdatesTests`' download dropped, CI macOS: DirectoryNotFoundException from the test's own look while the updater
  removed the folder. **Test waited wrong** (its look threw where it should read false), fixed in 01786a9f.
- `NewSolutionTests`' Avalonia app, CI Windows: MSB4018 on `obj/…/Avalonia/resources`, a design-time build holding it.
  **Race in the IDE**, fixed in b16f9c6e.
- `DebuggerTests`, `MemorySnapshotsTests`, macOS: "Build failed: 1 error", a design-time build making and signing the
  apphost beside the build. **Race in the IDE**, likely fixed in 070beaac (not proven; 6684f240 makes the next
  failure say the build's errors).
- `ProblemsTests`' solution-wide analysis, CI Windows: "Waited 60 s for the broken caller's error", the analysis loop
  ended for good by one refused pull. **Race in the IDE**, likely fixed in c6818c74.
- `ExplorerFilesTests`' delete of a project, CI macOS: the folder sent to the Trash came back, a design-time build
  still writing it. **Race in the IDE**, fixed in ac35de99.
- `IndentationTests`' Enter in a large XAML file, CI Linux 39245798584: the count took in the jump's frame as well as
  the Enter. **Test waited wrong** (it measured from before the jump had drawn), fixed in 48871ad7.
- `CodeAssistTests`' completion read off the UI thread, CI Linux 39245798584: "0 KB for 80 items", the list already
  open when the count began. **Test waited wrong**, fixed in 60c1e386.
- `BuildRunTests`' Hot Reload signature change, CI macOS: "Waited 60 s for the status bar to say dotnet watch restarts
  the program", its message quoting the window as the wait began. Not reproduced since; 90eecddd makes the message say
  what the window said when the wait ran out.
- CI Windows shard 2, e258c203: no test listed, xunit v3 3.2.2's runner writing after its JSON. Neither: **xunit's
  runner**, worked around in 6001a6f7.
- `XamlTimingTests`' keys while Passenger.App's types are read, CI Linux 39324137868: a key waited 255 ms against the
  250 ms bound once, on a shared runner; it passed on re-run, and the bound measures responsiveness, so the time scale
  rightly leaves it alone. Neither: **the runner's load**, not changed; again on Linux means look at the read's work on
  the UI thread.
- The v0.1.0 release run 39337400528: a shard whose tests had all passed failed uploading its results (CreateArtifact,
  ETIMEDOUT), and the publish met an HTTP 500 from GitHub's releases API (`gh release create` removed its draft).
  Neither: **GitHub**; re-running the failed jobs published the release.
- `TerminalTests`' flood of output, the Ubuntu VM's full run at bc93b815 (beside the Windows VM's full run and three
  agents' builds): a key waited 268 ms against the 250 ms bound; it passed three re-runs at 6–7 s each. Neither: **the
  machine's load**, not changed; the bound measures responsiveness, which the time scale rightly leaves alone.
- `AssetPreviewTests`' large image off the UI thread, the Mac under load: "small.png was decoded before the open
  returned". A small picture's decode on the pool can finish before the open reaches its await, which then goes on at
  once, so the open returns with the picture in place. **The test**, which held every open to still be loading: only the
  large picture's is now; the small one's off-thread decode is shown by the allocations compared.
- The Windows VM's final full run at d77d3222, run unscaled beside the Mac's and Ubuntu's: `DebuggerTests`' Continue
  test saw the program run on 110 ms after Continue against its 100 ms bound (CI's Windows scale makes that 250), and
  `BuildRunTests`' build beside an Avalonia design-time build could not delete its folder while a `dotnet.exe` still
  held it. Both passed run again on their own. Neither: **the machine's load**, not changed.
- `TitleBarTests`' menu bar titles, the same run: "The bar shows 3 of the main menu's titles at 1200 wide". The test was
  first run only on macOS, where the bar holds no window buttons; at 1200 wide Windows fits three titles beside its own,
  the rest behind the button as designed. **The test**, now opened 1600 wide.
- The Mac's full run at 509e5dfc, beside three agents' builds: `BuildRunTests`' launch profile run with arguments of its
  own lacked them. The short program had ended before `RunToEnd` looked, which then pressed Run again, running the
  profile's own arguments. **The test waited wrong**; it now waits for the run it started (`Ended`).
- The same run: `ContainersTests`' attach with tools of another version, its second attach refused "Already under way:
  Downloading Passenger's container tools for linux-arm64". The first attach's download said its stage from a worker,
  posted to the window, and it landed after the attach had ended: `ActionWork.Said` put the ended work back under way, so
  Attach to Process was refused for good. **A race in the IDE**: a stage said after its work ended now leaves it ended.
- `LongTextTests`' thousand carets, the Mac's full run at 58b8dc3f beside three agents' builds and test runs: a key took
  17.6 ms in 8,000 methods against 6.1 ms in 1,000 (the bound 17.2). It passed three runs alone straight after, with
  the load average near 10. Neither: **the machine's load**, not changed; the bound measures the UI thread's work, which
  the time scale rightly leaves alone.
- `McpAttachTests`' call nobody answers, the Mac's full run on lead-373 (d5ca7904) with the load average near 35 from
  two agents' builds and tests: the first `workspace` call through `--mcp-attach` had no answer in 60 s, where the
  window's limit closes its admission question at 5 s; the question had shown. It passed three runs alone straight
  after, the load still 25–37. Not explained: the message tailed only the test's own state, which an attached window
  never writes; it now tails the run's passenger.log and mcp.log too (the test turns protocol traces on), so a repeat says
  where the call waited.
