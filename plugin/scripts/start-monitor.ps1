[CmdletBinding()]
param()

$pluginRoot = if ($env:PLUGIN_ROOT) {
    $env:PLUGIN_ROOT
} else {
    Split-Path -Parent $PSScriptRoot
}

$executable = Join-Path $pluginRoot 'assets\app\CodexQuotaMonitor.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw "Codex Quota Monitor executable was not found: $executable"
}

if (-not (Get-Process -Name 'CodexQuotaMonitor' -ErrorAction SilentlyContinue)) {
    Start-Process -FilePath $executable -WindowStyle Hidden
}
