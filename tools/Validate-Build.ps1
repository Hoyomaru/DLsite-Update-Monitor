[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Artifacts = Join-Path $Root "artifacts"
$PluginOut = Join-Path $Artifacts "plugin"
$TestResults = Join-Path $Artifacts "TestResults"

function Fail([string]$Message) {
    Write-Host "ERROR: $Message" -ForegroundColor Red
    exit 1
}

Write-Host "== DLsite Update Monitor validation ==" -ForegroundColor Cyan
Write-Host "Root: $Root"

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Fail ".NET SDK が見つかりません。.NET 8 SDKをインストールしてから再実行してください。"
}

$sdks = & dotnet --list-sdks
Write-Host "Installed SDKs:"
$sdks | ForEach-Object { Write-Host "  $_" }
if (-not ($sdks | Where-Object { $_ -match '^8\.' })) {
    Fail ".NET 8 SDK が見つかりません。"
}

$frameworkRef = Join-Path ${env:ProgramFiles(x86)} "Reference Assemblies\Microsoft\Framework\.NETFramework\v4.6.2"
if (-not (Test-Path $frameworkRef)) {
    Fail ".NET Framework 4.6.2 Targeting Pack が見つかりません: $frameworkRef"
}

if (Test-Path $Artifacts) {
    Remove-Item $Artifacts -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $PluginOut, $TestResults | Out-Null

Push-Location $Root
try {
    Write-Host "`n[1/4] Restore Core tests" -ForegroundColor Cyan
    & dotnet restore ".\tests\DLsiteUpdateMonitor.Core.Tests\DLsiteUpdateMonitor.Core.Tests.csproj"
    if ($LASTEXITCODE -ne 0) { Fail "Core test restore failed." }

    Write-Host "`n[2/4] Run Core tests (expected: 63 cases)" -ForegroundColor Cyan
    & dotnet test ".\tests\DLsiteUpdateMonitor.Core.Tests\DLsiteUpdateMonitor.Core.Tests.csproj" `
        -c $Configuration `
        --no-restore `
        --logger "trx;LogFileName=core-tests.trx" `
        --results-directory $TestResults
    if ($LASTEXITCODE -ne 0) { Fail "Core tests failed." }

    Write-Host "`n[3/4] Restore and build Playnite plugin" -ForegroundColor Cyan
    & dotnet restore ".\src\DLsiteUpdateMonitor.Plugin\DLsiteUpdateMonitor.Plugin.csproj"
    if ($LASTEXITCODE -ne 0) { Fail "Plugin restore failed." }

    & dotnet build ".\src\DLsiteUpdateMonitor.Plugin\DLsiteUpdateMonitor.Plugin.csproj" `
        -c $Configuration `
        --no-restore
    if ($LASTEXITCODE -ne 0) { Fail "Plugin build failed." }

    $BuildOut = Join-Path $Root "src\DLsiteUpdateMonitor.Plugin\bin\$Configuration"
    $required = @(
        "DLsiteUpdateMonitor.dll",
        "DLsiteUpdateMonitor.Core.dll",
        "extension.yaml"
    )
    foreach ($name in $required) {
        $path = Join-Path $BuildOut $name
        if (-not (Test-Path $path)) { Fail "Required build output is missing: $path" }
        Copy-Item $path $PluginOut -Force
    }

    Get-ChildItem $BuildOut -Filter "*.pdb" -ErrorAction SilentlyContinue | Copy-Item -Destination $PluginOut -Force

    Write-Host "`n[4/4] Validate extension payload" -ForegroundColor Cyan
    $forbidden = @("Playnite.SDK.dll", "AngleSharp.dll", "Newtonsoft.Json.dll")
    foreach ($name in $forbidden) {
        if (Test-Path (Join-Path $PluginOut $name)) {
            Fail "Forbidden private runtime dependency was copied: $name"
        }
    }

    $yaml = Get-Content (Join-Path $PluginOut "extension.yaml") -Raw
    if ($yaml -notmatch '(?m)^Module:\s*DLsiteUpdateMonitor\.dll\s*$') {
        Fail "extension.yaml Module does not match DLsiteUpdateMonitor.dll."
    }
    if ($yaml -notmatch '(?m)^Type:\s*GenericPlugin\s*$') {
        Fail "extension.yaml Type is not GenericPlugin."
    }

    $summary = @"
DLsite Update Monitor validation passed.
Configuration: $Configuration
Validated at: $(Get-Date -Format o)
Core test gate: dotnet test completed successfully (expected test cases: 63)
Plugin output: $PluginOut
Runtime dependency policy: Playnite.SDK / AngleSharp / Newtonsoft.Json not bundled
Next gate: disposable Playnite profile smoke test (docs\SMOKE_TEST.md)
"@
    Set-Content -Path (Join-Path $Artifacts "VALIDATION-SUMMARY.txt") -Value $summary -Encoding UTF8

    Write-Host "`nPASS" -ForegroundColor Green
    Write-Host "Plugin payload: $PluginOut"
    Write-Host "Test results:  $TestResults"
    Write-Host "Next: docs\SMOKE_TEST.md"
}
finally {
    Pop-Location
}
