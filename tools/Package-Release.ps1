[CmdletBinding()]
param(
    [string]$ToolboxPath = "",
    [switch]$ConfirmRuntimeValidated
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Artifacts = Join-Path $Root "artifacts"
$PluginOut = Join-Path $Artifacts "plugin"
$ReleaseOut = Join-Path $Artifacts "release"
$ValidationSummary = Join-Path $Artifacts "VALIDATION-SUMMARY.txt"

function Fail([string]$Message) {
    Write-Host "ERROR: $Message" -ForegroundColor Red
    exit 1
}

Write-Host "== DLsite Update Monitor release packaging ==" -ForegroundColor Cyan
Write-Host "Root: $Root"

if (-not $ConfirmRuntimeValidated) {
    Fail "Runtime smoke test confirmation is required. Re-run with -ConfirmRuntimeValidated only after docs\SMOKE_TEST.md has passed."
}

if (-not (Test-Path $PluginOut)) {
    Fail "Validated plugin payload was not found: $PluginOut`nRun tools\Validate-Build.cmd first."
}

if (-not (Test-Path $ValidationSummary)) {
    Fail "VALIDATION-SUMMARY.txt is missing. Run tools\Validate-Build.cmd before packaging."
}

$summary = Get-Content $ValidationSummary -Raw
if ($summary -notmatch 'validation passed') {
    Fail "VALIDATION-SUMMARY.txt does not indicate a successful validation run."
}

$required = @(
    "DLsiteUpdateMonitor.dll",
    "DLsiteUpdateMonitor.Core.dll",
    "extension.yaml"
)
foreach ($name in $required) {
    if (-not (Test-Path (Join-Path $PluginOut $name))) {
        Fail "Required payload file is missing: $name"
    }
}

$forbidden = @("Playnite.SDK.dll", "AngleSharp.dll", "Newtonsoft.Json.dll")
foreach ($name in $forbidden) {
    if (Test-Path (Join-Path $PluginOut $name)) {
        Fail "Forbidden private runtime dependency is present: $name"
    }
}

$yamlPath = Join-Path $PluginOut "extension.yaml"
$yaml = Get-Content $yamlPath -Raw
if ($yaml -notmatch '(?m)^Id:\s*DLsiteUpdateMonitor_334542c6-1f81-4cc5-afd5-e052b021d37e\s*$') {
    Fail "extension.yaml contains an unexpected extension Id."
}
if ($yaml -notmatch '(?m)^Version:\s*(?<v>\d+(?:\.\d+){1,3})\s*$') {
    Fail "extension.yaml Version is missing or invalid."
}
$version = $Matches['v']

function Resolve-Toolbox([string]$ExplicitPath) {
    if ($ExplicitPath) {
        if (Test-Path $ExplicitPath) { return (Resolve-Path $ExplicitPath).Path }
        Fail "Toolbox.exe was not found at the specified path: $ExplicitPath"
    }

    $cmd = Get-Command Toolbox.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidates = @()
    if ($env:LOCALAPPDATA) { $candidates += (Join-Path $env:LOCALAPPDATA "Playnite\Toolbox.exe") }
    if ($env:ProgramW6432) { $candidates += (Join-Path $env:ProgramW6432 "Playnite\Toolbox.exe") }
    if (${env:ProgramFiles(x86)}) { $candidates += (Join-Path ${env:ProgramFiles(x86)} "Playnite\Toolbox.exe") }

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return (Resolve-Path $candidate).Path }
    }

    return $null
}

$toolbox = Resolve-Toolbox $ToolboxPath
if (-not $toolbox) {
    Fail "Toolbox.exe could not be located automatically. Pass -ToolboxPath with the path to Playnite's Toolbox.exe."
}
Write-Host "Toolbox: $toolbox"
Write-Host "Version: $version"

if (Test-Path $ReleaseOut) {
    Remove-Item $ReleaseOut -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $ReleaseOut | Out-Null

Write-Host "`nPacking validated payload with Playnite Toolbox..." -ForegroundColor Cyan
& $toolbox pack $PluginOut $ReleaseOut
if ($LASTEXITCODE -ne 0) {
    Fail "Toolbox.exe pack failed with exit code $LASTEXITCODE."
}

$packages = @(Get-ChildItem $ReleaseOut -Filter "*.pext" -File)
if ($packages.Count -ne 1) {
    Fail "Expected exactly one .pext package, found $($packages.Count)."
}

$package = $packages[0]

function Get-Sha256Hex([string]$Path) {
    $stream = $null
    $sha256 = $null
    try {
        $stream = [System.IO.File]::OpenRead($Path)
        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        $bytes = $sha256.ComputeHash($stream)
        return ([System.BitConverter]::ToString($bytes)).Replace("-", "").ToLowerInvariant()
    }
    finally {
        if ($stream -ne $null) { $stream.Dispose() }
        if ($sha256 -ne $null) { $sha256.Dispose() }
    }
}

$hashHex = Get-Sha256Hex $package.FullName
$hashLine = "$hashHex  $($package.Name)"
Set-Content -Path (Join-Path $ReleaseOut "SHA256SUMS.txt") -Value $hashLine -Encoding ASCII

$releaseSummary = @"
DLsite Update Monitor release candidate packaged successfully.
Version: $version
Package: $($package.FullName)
SHA-256: $hashHex
Payload source: $PluginOut
Build/test gate: PASS (artifacts\VALIDATION-SUMMARY.txt)
Runtime gate: confirmed by -ConfirmRuntimeValidated
Packaging tool: $toolbox
Packaged at: $(Get-Date -Format o)
"@
Set-Content -Path (Join-Path $ReleaseOut "RELEASE-SUMMARY.txt") -Value $releaseSummary -Encoding UTF8

Write-Host "`nPASS" -ForegroundColor Green
Write-Host "Package: $($package.FullName)"
Write-Host "SHA-256: $hashHex"
Write-Host "Summary: $(Join-Path $ReleaseOut 'RELEASE-SUMMARY.txt')"
