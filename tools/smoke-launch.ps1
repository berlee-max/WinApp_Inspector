<#
.SYNOPSIS
    Starts a WinApp Inspector executable with --self-test on a real Windows desktop and fails when it does not exit 0.

.DESCRIPTION
    The app shows its window, renders every page once and exits 0; a start-up failure (bad XAML resource, missing dll,
    broken runtime folder) exits non-zero or never shows a window. Crash logs written under %LocalAppData% are printed
    so the CI log carries the exception. Used by ci.yml (the build output) and release.yml (the packaged exes).
#>
param(
    [Parameter(Mandatory = $true)] [string] $Path,
    [int] $TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Path)) { throw "Executable not found: $Path" }

$logDir = Join-Path $env:LOCALAPPDATA 'WinAppInspector\logs'
if (Test-Path $logDir) { Remove-Item (Join-Path $logDir 'crash-*.log') -ErrorAction SilentlyContinue }

Write-Host "Launching $Path --self-test"
$process = Start-Process -FilePath $Path -ArgumentList '--self-test' -PassThru
$exited = $process.WaitForExit($TimeoutSeconds * 1000)
if (-not $exited) {
    $process.Kill($true)
    Write-Host "::error::Self-test did not finish within $TimeoutSeconds s (no window, or the window never rendered)."
    $failed = $true
} else {
    Write-Host "Exit code: $($process.ExitCode)"
    $failed = $process.ExitCode -ne 0
}

if (Test-Path $logDir) {
    Get-ChildItem (Join-Path $logDir 'crash-*.log') | ForEach-Object {
        Write-Host "---- $($_.FullName) ----"
        Get-Content $_.FullName
    }
}

if ($failed) { exit 1 }
Write-Host "Self-test passed."
