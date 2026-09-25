[CmdletBinding()]
param()

& (Join-Path $PSScriptRoot 'start-monitor.ps1')
Write-Output 'Codex Quota Monitor is installed. It will start from the plugin SessionStart hook after the hook is reviewed and trusted in Codex.'
