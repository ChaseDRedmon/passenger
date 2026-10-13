# Third-party notices

What Passenger uses that others made, and under what terms.

## Packages

| Package | Licence | Source |
|---|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Skia, Avalonia.HarfBuzz, Avalonia.Headless | MIT | https://github.com/AvaloniaUI/Avalonia |
| Avalonia.Fonts.Inter (the Inter typeface) | SIL Open Font License 1.1 | https://github.com/rsms/inter |
| SkiaSharp (with Skia, BSD-3-Clause) | MIT | https://github.com/mono/SkiaSharp |
| HarfBuzzSharp (with HarfBuzz, Old MIT) | MIT | https://github.com/mono/SkiaSharp |
| Microsoft.CodeAnalysis.CSharp (Roslyn) | MIT | https://github.com/dotnet/roslyn |
| roslyn-language-server (Roslyn's language server, copied beside the app under `roslyn/`) | MIT | https://github.com/dotnet/roslyn |
| StreamJsonRpc | MIT | https://github.com/microsoft/vs-streamjsonrpc |
| ModelContextProtocol.Core (the C# MCP SDK, for Passenger's MCP server) | Apache-2.0 | https://github.com/modelcontextprotocol/csharp-sdk |
| Microsoft.Extensions.AI.Abstractions (the MCP SDK's content types) | MIT | https://github.com/dotnet/extensions |
| Microsoft.TestPlatform.TranslationLayer, Microsoft.TestPlatform.ObjectModel (VSTest's client, for finding, running and debugging tests) | MIT | https://github.com/microsoft/vstest |
| Microsoft.Diagnostics.DbgShim, Microsoft.Diagnostics.NETCore.Client (the debugger's and the profiler's runtime interface) | MIT | https://github.com/dotnet/diagnostics |
| ICSharpCode.Decompiler (ILSpy's decompiler, for frames without source) | MIT | https://github.com/icsharpcode/ILSpy |
| MP.Microsoft.CodeAnalysis.CSharp.ExpressionEvaluator.ExpressionCompiler (Roslyn's expression compiler, packed from Roslyn's source by MattParkerDev) | MIT | https://github.com/MattParkerDev/roslyn (branch custom-ee-5.9.0) |
| Microsoft.Diagnostics.Runtime (ClrMD, reading the profiler's memory snapshots) | MIT | https://github.com/microsoft/clrmd |
| Microsoft.Diagnostics.Tracing.TraceEvent (reading the profiler's EventPipe traces) | MIT | https://github.com/microsoft/perfview |
| Porta.Pty (the terminal's pseudo-terminal, with its native forkpty shim, libporta_pty) | MIT | https://github.com/tomlm/Porta.Pty |
| Microsoft.Windows.Console.ConPTY (Porta.Pty's console host on Windows: conpty.dll and OpenConsole.exe) | MIT | https://github.com/microsoft/terminal |
| NeoSmart.AsyncLock | MIT | https://github.com/neosmart/AsyncLock |
| ZLinq | MIT | https://github.com/Cysharp/ZLinq |
| Krafs.Publicizer (build time only) | MIT | https://github.com/krafs/Publicizer |
| Microsoft.CodeAnalysis.BannedApiAnalyzers (the tests' build only: the entry points a test may not call) | MIT | https://github.com/dotnet/roslyn |

## SharpDbg and ICorDebugSharp

Passenger's debugger began as a copy of two MIT-licensed projects by Matthew Parker (MattParkerDev), and is
Passenger's own from there:

- `src/Passenger.Debugger` (the engine), `src/Passenger.DebugAdapter` (its adapter), `tests/Debuggees` and the tests
  under `tests/Passenger.Tests/Debugging` started from SharpDbg, https://github.com/MattParkerDev/sharpdbg, at commit
  93b25ac (September 15, 2026).
- `src/Passenger.Debugger.Interop` (the ICorDebug, IMetaDataImport and dbgshim declarations) started from ICorDebugSharp
  0.1.8, whose package declares the MIT licence. Its source is not published, so this copy was recovered from the
  package's assembly with ILSpy, keeping only what its author wrote: the source generators' output was dropped, since
  the build generates it again. Its declarations were generated from Microsoft's MIT-licensed `cordebug.h` and
  `cor.h` (https://github.com/dotnet/runtime), and its documentation comments are adapted from the Microsoft .NET
  documentation (https://github.com/dotnet/docs), licensed under the Creative Commons Attribution 4.0 International
  Public License (https://creativecommons.org/licenses/by/4.0/), changed from Markdown into XML documentation comments.

```
MIT License

Copyright (c) 2026 Matthew Parker

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## JetBrains Mono

`src/Passenger.Editor/Fonts/` holds JetBrains Mono 2.304 (Regular, Bold, Italic, Bold Italic), the
editor's font, from https://github.com/JetBrains/JetBrainsMono under the SIL Open Font License 1.1,
whose full text is beside the fonts (`OFL.txt`). The fonts are unmodified.

## Fluent UI System Icons

`src/Passenger.App/Theme/Icons.axaml` holds path data from Microsoft's Fluent UI System Icons
(https://github.com/microsoft/fluentui-system-icons), written by `tools/icons.py`, which lists every
icon used.

```
MIT License

Copyright (c) 2020 Microsoft Corporation

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

## Unicode Character Database

`src/Passenger.Terminal/WideCharacters.cs`, the characters the terminal draws two columns wide, is derived from the
Unicode Character Database 16.0.0's `EastAsianWidth.txt` and `emoji/emoji-data.txt`
(https://www.unicode.org/Public/16.0.0/ucd/), written by `tools/unicode-widths.py`.

```
UNICODE LICENSE V3

COPYRIGHT AND PERMISSION NOTICE

Copyright © 1991-2026 Unicode, Inc.

NOTICE TO USER: Carefully read the following legal agreement. BY
DOWNLOADING, INSTALLING, COPYING OR OTHERWISE USING DATA FILES, AND/OR
SOFTWARE, YOU UNEQUIVOCALLY ACCEPT, AND AGREE TO BE BOUND BY, ALL OF THE
TERMS AND CONDITIONS OF THIS AGREEMENT. IF YOU DO NOT AGREE, DO NOT
DOWNLOAD, INSTALL, COPY, DISTRIBUTE OR USE THE DATA FILES OR SOFTWARE.

Permission is hereby granted, free of charge, to any person obtaining a
copy of data files and any associated documentation (the "Data Files") or
software and any associated documentation (the "Software") to deal in the
Data Files or Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, and/or sell
copies of the Data Files or Software, and to permit persons to whom the
Data Files or Software are furnished to do so, provided that either (a)
this copyright and permission notice appear with all copies of the Data
Files or Software, or (b) this copyright and permission notice appear in
associated Documentation.

THE DATA FILES AND SOFTWARE ARE PROVIDED "AS IS", WITHOUT WARRANTY OF ANY
KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT OF
THIRD PARTY RIGHTS.

IN NO EVENT SHALL THE COPYRIGHT HOLDER OR HOLDERS INCLUDED IN THIS NOTICE
BE LIABLE FOR ANY CLAIM, OR ANY SPECIAL INDIRECT OR CONSEQUENTIAL DAMAGES,
OR ANY DAMAGES WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS,
WHETHER IN AN ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION,
ARISING OUT OF OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THE DATA
FILES OR SOFTWARE.

Except as contained in this notice, the name of a copyright holder shall
not be used in advertising or otherwise to promote the sale, use or other
dealings in these Data Files or Software without prior written
authorization of the copyright holder.
```
