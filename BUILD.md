# Build and validation

## Supported target

- Playnite stable: 10.56
- Plugin target: .NET Framework 4.6.2
- PlayniteSDK package: 6.16.0
- Core test target: .NET 8.0

The plugin deliberately references the Playnite-provided runtime copies of AngleSharp 0.9.9 and Newtonsoft.Json 10.0.3 instead of shipping competing copies.

## Prerequisites on Windows

1. Visual Studio 2022 Build Tools or Visual Studio 2022
2. .NET Framework 4.6.2 Developer/Targeting Pack
3. .NET 8 SDK
4. Playnite 10.56 for runtime testing

## Restore and run Core tests

```powershell
cd <repository-root>
dotnet restore .\DLsiteUpdateMonitor.sln
dotnet test .\tests\DLsiteUpdateMonitor.Core.Tests\DLsiteUpdateMonitor.Core.Tests.csproj -c Release
```

All tests must pass before building/installing the plugin.

## Build plugin

```powershell
dotnet build .\src\DLsiteUpdateMonitor.Plugin\DLsiteUpdateMonitor.Plugin.csproj -c Release
```

Expected output directory:

```text
src\DLsiteUpdateMonitor.Plugin\bin\Release\
```

It must contain at least:

```text
DLsiteUpdateMonitor.dll
DLsiteUpdateMonitor.Core.dll
extension.yaml
```

Do not add private copies of `Playnite.SDK.dll`, `AngleSharp.dll`, or `Newtonsoft.Json.dll` to the extension package unless the dependency strategy is deliberately changed and retested.

## Development install

Copy the Release output into a dedicated Playnite extension folder, for example:

```text
%APPDATA%\Playnite\Extensions\DLsiteUpdateMonitor\
```

Restart Playnite, then confirm the plugin appears under Add-ons and the `DLsite Update Monitor` menus are present.

## First runtime validation order

Use a disposable Playnite backup/profile first.

1. Run `DLsiteリンク診断` only. Confirm no metadata is modified.
2. Pick one game with a known DLsite link and run `今すぐ確認`.
3. Confirm `tracking.json` is created in the plugin user-data directory.
4. Confirm first observation is treated as baseline/監視開始, with no update tag.
5. Repeat the same game. Confirm no update is detected.
6. Test an intentionally malformed DLsite link. Confirm LinkError does not erase an existing pending state.
7. Only after these pass, run a small batch (5–10 games).
8. Only after the small batch passes, run the full library.

## Packaging

After `tools\Validate-Build.cmd` passes and `docs\SMOKE_TEST.md` is completed, package the **already validated** `artifacts\plugin` folder with Playnite Toolbox. Do not rebuild between the successful runtime smoke test and packaging.

```powershell
.\tools\Package-Release.ps1 -ConfirmRuntimeValidated
```

or:

```cmd
tools\Package-Release.cmd
```

The script uses the official `Toolbox.exe pack <extensionfolder> <targetfolder>` flow, writes the `.pext` to `artifacts\release`, and generates `SHA256SUMS.txt` plus `RELEASE-SUMMARY.txt`.

If Toolbox cannot be detected automatically, pass `-ToolboxPath` explicitly.

## Optional cross-platform static precheck

If Python 3 is available, the repository also contains a lightweight structural check that can run before the real C# build:

```powershell
python .\tools\Static-Validate.py
```

It validates project/XAML XML, project-reference paths, extension manifest invariants, C# delimiter/string/comment boundaries, pinned dependency versions, and a static xUnit case estimate. It is **not** a replacement for `dotnet test`.

## One-command validation on Windows

From PowerShell at the repository root:

```powershell
.\tools\Validate-Build.ps1
```

The script verifies prerequisites, restores packages, runs the **63 Core test cases**, builds the net462 Playnite plugin, validates the extension payload, and writes it to `artifacts\plugin`. It deliberately fails if private copies of `Playnite.SDK.dll`, `AngleSharp.dll`, or `Newtonsoft.Json.dll` appear in the payload.

After that passes, follow `docs\SMOKE_TEST.md`. For a disposable development install you can use:

```powershell
.\tools\Install-Dev.ps1 -WhatIf
.\tools\Install-Dev.ps1
```

`Install-Dev.ps1` backs up an existing development extension folder before replacing it.
