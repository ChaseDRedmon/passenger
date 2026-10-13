# Third-party notices

What Passenger uses that others made, and under what terms. Each package is named by its NuGet id, with the licence
and the copyright its package states (its nuspec's licence expression or licence file and its copyright line, else its
authors). `tools/check-notices.py`, which CI runs after its restore (`just notices` here), fails naming any package
the solution restores, Roslyn's language server carries, or the tool manifest (`dotnet-tools.json`) pins, that is not
named here.

## In the app

What `just publish` carries for macOS and Windows: the app, the debugger beside it under `debugger/` and Roslyn's
language server under `roslyn/`, and on Windows what Velopack's installer adds to it (`just package-win`). Packages
restored with these whose files no macOS or Windows build carries (SkiaSharp's and HarfBuzzSharp's natives for Linux
and the browser, dbgshim's for Linux and 32-bit Windows) are left out.

| Package | In Passenger | Licence | Copyright |
|---|---|---|---|
| The .NET runtime (`Microsoft.NETCore.App`) | the app and the debugger are self-contained; Roslyn's server runs on the installed .NET | MIT | © Microsoft Corporation. All rights reserved. |
| `Avalonia`, `Avalonia.Desktop`, `Avalonia.Native`, `Avalonia.Win32`, `Avalonia.X11`, `Avalonia.FreeDesktop`, `Avalonia.FreeDesktop.AtSpi`, `Avalonia.Remote.Protocol`, `Avalonia.Themes.Fluent`, `Avalonia.Skia`, `Avalonia.HarfBuzz`, `Avalonia.Headless` | the UI, and the headless platform the screenshots draw on | MIT | Copyright 2013-2026 © The AvaloniaUI Project |
| `Avalonia.Fonts.Inter` | the Inter typeface, itself under the SIL Open Font License 1.1, Copyright (c) 2016 The Inter Project Authors (https://github.com/rsms/inter) | MIT | Copyright 2013-2026 © The AvaloniaUI Project |
| `Avalonia.Angle.Windows.Natives` | ANGLE's OpenGL ES (`av_libglesv2.dll`), Windows only | BSD-3-Clause (the package's LICENSE) | Copyright 2018 The ANGLE Project Authors |
| `MicroCom.Runtime` | Avalonia's COM interop | MIT | Copyright 2021 © Nikita Tsukanov |
| `Tmds.DBus.Protocol` | Avalonia's D-Bus client, for Linux desktops | MIT | Tom Deseyn |
| `SkiaSharp`, `SkiaSharp.NativeAssets.macOS`, `SkiaSharp.NativeAssets.Win32` | drawing, with Skia (BSD-3-Clause) | MIT | © Microsoft Corporation. All rights reserved. |
| `HarfBuzzSharp`, `HarfBuzzSharp.NativeAssets.macOS`, `HarfBuzzSharp.NativeAssets.Win32` | text shaping, with HarfBuzz (Old MIT) | MIT | © Microsoft Corporation. All rights reserved. |
| `Microsoft.CodeAnalysis.CSharp`, `Microsoft.CodeAnalysis.Common` | Roslyn: colour before the server answers, and the debugger's expressions | MIT | © Microsoft Corporation. All rights reserved. |
| `roslyn-language-server.osx-arm64`, `roslyn-language-server.osx-x64`, `roslyn-language-server.win-x64`, `roslyn-language-server.win-arm64` | Roslyn's language server, one per system, copied under `roslyn/` with the package's own `ThirdPartyNotices.rtf`, less four assemblies under Visual Studio's terms; what it carries is listed below | MIT | © Microsoft Corporation. All rights reserved. |
| `StreamJsonRpc` | JSON-RPC with the language server and the debug adapter | MIT | © Microsoft Corporation. All rights reserved. |
| `Microsoft.VisualStudio.Threading.Only` | StreamJsonRpc's, whose NonConcurrentSynchronizationContext keeps the language server's messages in order | MIT | © Microsoft Corporation. All rights reserved. |
| `Microsoft.VisualStudio.Validation` | StreamJsonRpc's | MIT | © Microsoft Corporation. All rights reserved. |
| `Microsoft.NET.StringTools` | StreamJsonRpc's | MIT | © Microsoft Corporation. All rights reserved. |
| `Nerdbank.Streams`, `Nerdbank.MessagePack` | StreamJsonRpc's | MIT | © Andrew Arnott. All rights reserved. |
| `MessagePack`, `MessagePack.Annotations` | StreamJsonRpc's | MIT | © Yoshifumi Kawai and contributors. All rights reserved. |
| `PolyType` | Nerdbank.MessagePack's | MIT | 2024, Eirik Tsarpalis (its copyright line and its author) |
| `Newtonsoft.Json` | StreamJsonRpc's | MIT | Copyright © James Newton-King 2008 |
| `ModelContextProtocol.Core` | the C# MCP SDK, for Passenger's MCP server | Apache-2.0 | © Model Context Protocol a Series of LF Projects, LLC. |
| `Microsoft.Extensions.AI.Abstractions` | the MCP SDK's content types | MIT | © Microsoft Corporation. All rights reserved. |
| `Microsoft.TestPlatform.TranslationLayer`, `Microsoft.TestPlatform.ObjectModel` | VSTest's client, for finding, running and debugging tests | MIT | © Microsoft Corporation. All rights reserved. |
| `Microsoft.Diagnostics.NETCore.Client` | the debugger's and the profiler's diagnostic port | MIT | © Microsoft Corporation. All rights reserved. |
| `Microsoft.Diagnostics.Runtime` | ClrMD, reading the profiler's memory snapshots | MIT | © Microsoft Corporation. All rights reserved. |
| `Microsoft.Diagnostics.Tracing.TraceEvent` | reading the profiler's EventPipe traces, with its COM interop assemblies `Dia2Lib.dll` and `TraceReloggerLib.dll`; its Windows natives (`msdia140.dll`, `KernelTraceControl.dll`), which reading EventPipe never loads, are left out of every publish (`Passenger.App.csproj`) | MIT | © Microsoft Corporation. All rights reserved. |
| `Porta.Pty` | the terminal's pseudo-terminal, with its native forkpty shim (`libporta_pty`) | MIT | Tom Laird-McConnell All Rights Reserved |
| `Microsoft.Windows.Console.ConPTY` | Porta.Pty's console host (`conpty.dll`, `OpenConsole.exe`), Windows only | MIT | © Microsoft Corporation |
| `Microsoft.Diagnostics.DbgShim`, `Microsoft.Diagnostics.DbgShim.osx-arm64`, `Microsoft.Diagnostics.DbgShim.osx-x64`, `Microsoft.Diagnostics.DbgShim.win-x64`, `Microsoft.Diagnostics.DbgShim.win-arm64` | the debugger's way into a runtime (dbgshim), one native per system | MIT | © Microsoft Corporation. All rights reserved. |
| `Microsoft.DiaSymReader` | the expression compiler's | MIT | © Microsoft Corporation. All rights reserved. |
| `ICSharpCode.Decompiler` | ILSpy's decompiler, for frames without source | MIT | Copyright 2011-2026 AlphaSierraPapa |
| `MP.Microsoft.CodeAnalysis.CSharp.ExpressionEvaluator.ExpressionCompiler`, `MP.Microsoft.CodeAnalysis.ExpressionEvaluator.ExpressionCompiler` | Roslyn's expression compiler, packed from Roslyn's source by MattParkerDev (https://github.com/MattParkerDev/roslyn, branch custom-ee-5.9.0) | MIT | no copyright line; its authors: .NET Foundation and contributors |
| `NeoSmart.AsyncLock` | the debugger's locks | MIT | Copyright NeoSmart Technologies 2017-2022 |
| `ZLinq` | the debugger's queries | MIT | © Cysharp, Inc. |
| `Velopack` | the Windows install's hooks, which register and remove the file types Passenger opens as it is installed, updated and uninstalled, and its updates: the UpdateManager that downloads a newer release's package from its feed and checks its size and SHA-256 | MIT | Copyright © Velopack Ltd. All rights reserved. |
| `vpk` | in the Windows installer, Velopack's own programs, written in Rust with open-source crates: `Setup.exe` (the installer itself), and in the install `Update.exe` (which installs an update once Passenger has closed, and uninstalls it from Settings > Apps) and a `Passenger.exe` that starts `current\Passenger.exe` | MIT | Copyright © Velopack Ltd. All rights reserved. |

### Inside `roslyn/`

Roslyn's language server carries these packages' assemblies in its own package, which Microsoft gives as a whole under
MIT with the `ThirdPartyNotices.rtf` copied beside them. What each package says of itself on nuget.org, at the version
the server's `.deps.json` names, or, for a version not published there, what its releases there say:

| Package | Licence | Copyright |
|---|---|---|
| `Microsoft.Bcl.AsyncInterfaces`, `Microsoft.Build.Framework`, `Microsoft.Build.Locator`, `Microsoft.CodeAnalysis.Elfie`, `Microsoft.DiaSymReader`, `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Configuration.Abstractions`, `Microsoft.Extensions.Configuration.Binder`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.FileSystemGlobbing`, `Microsoft.Extensions.Logging`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Logging.Configuration`, `Microsoft.Extensions.Logging.Console`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Options.ConfigurationExtensions`, `Microsoft.Extensions.Primitives`, `Microsoft.NET.StringTools`, `Microsoft.TestPlatform.ObjectModel`, `Microsoft.TestPlatform.TranslationLayer`, `Microsoft.VisualStudio.SolutionPersistence`, `Microsoft.VisualStudio.Threading.Only`, `System.CodeDom`, `System.ComponentModel.Composition`, `System.Composition.AttributedModel`, `System.Composition.Convention`, `System.Composition.Hosting`, `System.Composition.Runtime`, `System.Composition.TypedParts`, `System.Configuration.ConfigurationManager`, `System.Diagnostics.EventLog`, `System.Diagnostics.PerformanceCounter`, `System.Management`, `System.Security.Cryptography.Pkcs`, `System.Security.Cryptography.ProtectedData`, `System.Security.Permissions`, `System.Windows.Extensions` | MIT | © Microsoft Corporation. All rights reserved. |
| `Microsoft.ServiceHub.Framework` (4.10.147), `Microsoft.VisualStudio.Composition` (18.9.20), `Microsoft.VisualStudio.Validation` (18.7.1), `StreamJsonRpc` (2.26.10), `System.CommandLine` (3.0.0-preview.6.26324.102): versions not on nuget.org | MIT (their releases there) | © Microsoft Corporation. All rights reserved. |
| `NuGet.Common`, `NuGet.Configuration`, `NuGet.DependencyResolver.Core`, `NuGet.Frameworks`, `NuGet.LibraryModel`, `NuGet.Packaging`, `NuGet.ProjectModel`, `NuGet.Protocol`, `NuGet.Versioning` | Apache-2.0 | © Microsoft Corporation. All rights reserved. |
| `Humanizer.Core` | MIT | Copyright (c) .NET Foundation and Contributors |
| `ICSharpCode.Decompiler` (9.1.0.7988) | MIT | Copyright 2011-2025 AlphaSierraPapa |
| `MessagePack`, `MessagePack.Annotations` | MIT | © Yoshifumi Kawai and contributors. All rights reserved. |
| `Nerdbank.MessagePack`, `Nerdbank.Streams` | MIT | © Andrew Arnott. All rights reserved. |
| `Newtonsoft.Json` (13.0.4) | MIT | Copyright © James Newton-King 2008 |
| `PolyType` | MIT | 2024, Eirik Tsarpalis |
| `SQLite3MC.PCLRaw.bundle`, `SQLite3MC.PCLRaw.lib`, `SQLite3MC.PCLRaw.provider` (SQLite3 Multiple Ciphers, the native `libsqlite3mc`) | MIT | Copyright (c) 2023-2026 Ulrich Telle, et al. |
| `SQLitePCLRaw.core` | Apache-2.0 | Copyright 2014-2025 SourceGear, LLC |
| `Microsoft.VisualStudio.Telemetry` (18.3.124, not on nuget.org) | not open source: its releases there link the Microsoft Visual Studio Telemetry license terms (https://visualstudio.microsoft.com/license-terms/mt736442/) | © Microsoft Corporation. All rights reserved. |
| `Microsoft.VisualStudio.Utilities.Internal` (16.3.90) | not open source: the Microsoft Visual Studio Internal Utilities license terms (https://visualstudio.microsoft.com/license-terms/mt736439/) | © Microsoft Corporation. All rights reserved. |

The package carries six assemblies under Visual Studio's terms rather than an open-source licence. Which of them the
server loads was found by logging every assembly each server and build host loaded through Passenger's C# tests (startup,
projects and solutions opened, completion, hover, signature help, pulled diagnostics, colours, definitions, usages,
renames, code actions and fixes, formatting, inlay hints), by reading Roslyn's source at the package's commit
(dotnet/roslyn dc1db3e7), and by running those tests without each.

Left out of `roslyn/` (`RoslynServerLeftOut` in `src/Passenger.Lsp/Passenger.Lsp.csproj`, with their satellite
resources; the build fails if the package stops carrying one, and a test checks that none is beside the server and that
the server never looks for one), since no path Passenger takes loads them:

- `Microsoft.ServiceHub.Client` (4.6.3200; the Visual Studio Add-ons and Extensions terms): Visual Studio's ServiceHub
  client, used only by its out-of-process host (`ServiceHubRemoteHostClient`). The server reads it only because it
  searches every `Microsoft.ServiceHub*` file beside it for MEF parts, and it has none the server uses.
- `Microsoft.VisualStudio.Debugger.Contracts` (18.3.0-beta.26277.4) and `Microsoft.VisualStudio.RpcContracts` (18.9.918),
  under the same terms: Visual Studio's Hot Reload logging and RPC contracts, used only by Roslyn's ServiceHub host
  (`Microsoft.CodeAnalysis.Remote.ServiceHub`), which the server never loaded on any of those paths.
- `Microsoft.VisualStudio.RemoteControl` (16.3.52; the Telemetry terms, mt736442): Visual Studio Telemetry's client for
  remote settings, experiments and its manifest, which no server loaded on any of those paths (the telemetry session
  Razor makes, below, is never started).

Kept: `Microsoft.VisualStudio.Telemetry` and `Microsoft.VisualStudio.Utilities.Internal`, which the server's own Razor
support needs. It is in the server's MEF composition whenever `Microsoft.VisualStudioCode.RazorExtension.dll` is beside
it, and no option turns it off. Its telemetry reporter derives from a class that takes a Visual Studio telemetry session,
so without Telemetry the composition has errors and is never cached, Razor's start fails with an error in the server's
log at every start, and a server whose composition was cached with Telemetry fails at `initialized` and loads no project;
leaving out the Razor extension too only moves the error to the server's own part that implements one of its interfaces.
At `initialized` Razor's in-process services create Visual Studio Telemetry's default session, which loads
Utilities.Internal; nothing in the server starts that session, which it must be to send anything (`--telemetryLevel off`
governs only Roslyn's own telemetry, which goes nowhere without C# Dev Kit).

## Building and testing only

Packages the build or the tests use that no app carries.

| Package | In Passenger | Licence | Copyright |
|---|---|---|---|
| `Avalonia.BuildServices` | Avalonia's build task, whose telemetry `AVALONIA_TELEMETRY_OPTOUT` turns off (the README says how) | MIT | Copyright 2023-2025 © The AvaloniaUI Project |
| `Microsoft.CodeAnalysis.Analyzers` | Roslyn's analyzers, which come with Microsoft.CodeAnalysis.Common | MIT | © Microsoft Corporation. All rights reserved. |
| `Krafs.Publicizer` | the debugger's build | MIT | © Krafs 2024 |
| `Microsoft.CodeAnalysis.BannedApiAnalyzers` | the tests' build: the entry points a test may not call | MIT | © Microsoft Corporation. All rights reserved. |
| `xunit.v3`, `xunit.v3.assert`, `xunit.v3.common`, `xunit.v3.core.mtp-v1`, `xunit.v3.extensibility.core`, `xunit.v3.mtp-v1`, `xunit.v3.runner.common`, `xunit.v3.runner.inproc.console`, `xunit.analyzers`, `xunit.runner.visualstudio` | the tests | Apache-2.0 | Copyright (C) .NET Foundation |
| `Avalonia.Headless.XUnit` | the tests on Avalonia's headless platform | MIT | Copyright 2013-2026 © The AvaloniaUI Project |
| `Microsoft.NET.Test.Sdk`, `Microsoft.TestPlatform.TestHost`, `Microsoft.CodeCoverage` | the tests' host | MIT | © Microsoft Corporation. All rights reserved. |
| `Microsoft.Testing.Platform`, `Microsoft.Testing.Platform.MSBuild`, `Microsoft.Testing.Extensions.Telemetry`, `Microsoft.Testing.Extensions.TrxReport.Abstractions`, `Microsoft.ApplicationInsights` | xunit.v3's test platform | MIT | © Microsoft Corporation. All rights reserved. |
| `Microsoft.Bcl.AsyncInterfaces`, `Microsoft.Win32.Registry` | the test platform's | MIT | © Microsoft Corporation. All rights reserved. |
| `vpk` | Velopack's packer, a .NET tool at the version `dotnet-tools.json` pins, which builds the Windows installer (`tools/package-win.sh`). It carries others' programs under their own terms for what Passenger's installer does not use or carry: Microsoft's signtool and Azure Artifact Signing client, which sign it only when signing is asked for (the Windows SDK's and Microsoft's terms), WiX (MS-RL) for MSI packages, and zstd (BSD-3-Clause or GPL-2.0) | MIT | Copyright © Velopack Ltd. All rights reserved. |

## Licence texts

`tools/Passenger.Notices/Licenses/` holds the texts of the licences packages name by SPDX identifier (`MIT.txt`,
`Apache-2.0.txt`), from the SPDX License List 3.29.0 (https://github.com/spdx/license-list-data), unchanged; its README
says where each came from. The app's notices, `THIRD-PARTY-NOTICES.txt` beside it, are written from them, from the
packages themselves and from this file: Roslyn's server's table above, and everything below.

<!-- The app's notices (THIRD-PARTY-NOTICES.txt) carry everything below this line as it is. -->

## SharpDbg and ICorDebugSharp

Passenger's debugger began as a copy of two MIT-licensed projects by Matthew Parker (MattParkerDev), and is
Passenger's own from there:

- `src/Passenger.Debugger` (the engine), `src/Passenger.DebugAdapter` (its adapter), `tests/Debuggees` and the tests
  under `tests/Passenger.Tests/Debugging` started from SharpDbg, https://github.com/MattParkerDev/sharpdbg, at commit
  93b25ac (September 15, 2026).
- `src/Passenger.Debugger.Interop` (the ICorDebug, IMetaDataImport and dbgshim declarations) started from ICorDebugSharp
  0.1.8, whose package declares the MIT licence. Its source is not published, so this copy was recovered from the
  package's assembly with ILSpy, keeping only what its author wrote: the source generators' output was dropped, since
  the build generates it again. It has since been cut to what Passenger's debugger uses: the declarations the engine
  names, and those they name in turn. Its declarations were generated from Microsoft's MIT-licensed `cordebug.h` and
  `cor.h` (https://github.com/dotnet/runtime), and its documentation comments are adapted from the Microsoft .NET
  documentation (https://github.com/dotnet/docs), licensed under the Creative Commons Attribution 4.0 International
  Public License (https://creativecommons.org/licenses/by/4.0/), changed from Markdown into XML documentation comments,
  a reference to a declaration cut since left as plain text.

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

## Code from ILSpy and ClrDebug

Two of the debugger's files came with SharpDbg from projects of their own:

- `src/Passenger.Debugger/Decompilation/PortablePdbWriter2.cs` is ILSpy's `PortablePdbWriter`
  (`ICSharpCode.Decompiler/DebugInfo/PortablePdbWriter.cs`, https://github.com/icsharpcode/ILSpy), changed to decompile
  its files in parallel and return their text: MIT, Copyright (c) 2018 Siegfried Pammer, as the file's header in ILSpy
  says.
- `src/Passenger.Debugger/ClrDebugExtensions.cs` began as ClrDebug's .NET Core sample (`Samples/NetCore/Program.cs` at
  commit 5f46218, https://github.com/lordmilko/ClrDebug): its automatic attach, and its manual one kept in comments.
  MIT, Copyright (c) 2022 lordmilko.

```
Copyright (c) 2018 Siegfried Pammer

Permission is hereby granted, free of charge, to any person obtaining a copy of this
software and associated documentation files (the "Software"), to deal in the Software
without restriction, including without limitation the rights to use, copy, modify, merge,
publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons
to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or
substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR
PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE
FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR
OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
DEALINGS IN THE SOFTWARE.
```

```
MIT License

Copyright (c) 2022 lordmilko

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
