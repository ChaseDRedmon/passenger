# Flake ledger

Each test failure on CI (or a VM) once its cause is known: the test, what it said, and which of two things it was, so a
pattern shows. A **race in the IDE** is fixed in the IDE; waiting longer or trying again would only have hidden it. A
**test waited wrong** is fixed in the test, by waiting for what it checks (`Ui.WaitFor`, `Ui.PumpUntil`,
`Waiting.Until`), never by a longer fixed time. A failure that was neither says what it was.

To add one: a line under its date, newest first, naming the test, the run, the cause in a sentence and the commit
that fixed it, in one of the forms below. A failure not yet understood goes in only once it is.

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
