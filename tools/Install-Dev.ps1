[CmdletBinding(SupportsShouldProcess=$true)]
param(
    [string]$PlayniteConfigRoot = (Join-Path $env:APPDATA "Playnite")
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Payload = Join-Path $Root "artifacts\plugin"
$Target = Join-Path $PlayniteConfigRoot "Extensions\DLsiteUpdateMonitor"
$BackupRoot = Join-Path $Root "artifacts\install-backups"

if (-not (Test-Path (Join-Path $Payload "DLsiteUpdateMonitor.dll"))) {
    throw "Validated plugin payload not found. Run tools\Validate-Build.ps1 first."
}

if (Test-Path $Target) {
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $backup = Join-Path $BackupRoot "DLsiteUpdateMonitor-$stamp"
    New-Item -ItemType Directory -Force -Path $BackupRoot | Out-Null
    if ($PSCmdlet.ShouldProcess($Target, "Backup existing extension to $backup")) {
        Copy-Item $Target $backup -Recurse -Force
    }
}

if ($PSCmdlet.ShouldProcess($Target, "Install validated development build")) {
    New-Item -ItemType Directory -Force -Path $Target | Out-Null
    Get-ChildItem $Target -Force | Remove-Item -Recurse -Force
    Copy-Item (Join-Path $Payload "*") $Target -Recurse -Force
}

Write-Host "Installed development build to: $Target" -ForegroundColor Green
Write-Host "Restart Playnite before smoke testing."
