[CmdletBinding()]
param([switch]$RemoveLocalData)

try {
    & (Join-Path $PSScriptRoot 'control.ps1') -Action exit | Out-Null
} catch {
    Get-Process -Name 'CodexQuotaMonitor' -ErrorAction SilentlyContinue | Stop-Process
}

if ($RemoveLocalData) {
    $dataPath = Join-Path $env:LOCALAPPDATA 'CodexQuotaMonitor'
    $resolvedBase = [System.IO.Path]::GetFullPath($env:LOCALAPPDATA).TrimEnd('\') + '\'
    $resolvedTarget = [System.IO.Path]::GetFullPath($dataPath)
    if (-not $resolvedTarget.StartsWith($resolvedBase, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove unexpected path: $resolvedTarget"
    }
    if (Test-Path -LiteralPath $resolvedTarget) {
        Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
    }
}

Write-Output 'Codex Quota Monitor is stopped. Remove the plugin from Codex to complete uninstallation.'
