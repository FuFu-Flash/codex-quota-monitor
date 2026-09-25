[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$monitorProject = Join-Path $repositoryRoot 'monitor-src\CodexQuotaMonitor.csproj'
$installerProject = Join-Path $repositoryRoot 'installer-src\CodexQuotaMonitorSetup\CodexQuotaMonitorSetup.csproj'
$pluginRoot = Join-Path $repositoryRoot 'plugin'
$monitorOutput = Join-Path $repositoryRoot 'artifacts\monitor'
$releaseOutput = Join-Path $repositoryRoot 'artifacts\release'
$pluginExecutable = Join-Path $pluginRoot 'assets\app\CodexQuotaMonitor.exe'
$payload = Join-Path $repositoryRoot 'installer-src\CodexQuotaMonitorSetup\payload.zip'

New-Item -ItemType Directory -Path $monitorOutput -Force | Out-Null
New-Item -ItemType Directory -Path $releaseOutput -Force | Out-Null
New-Item -ItemType Directory -Path (Split-Path -Parent $pluginExecutable) -Force | Out-Null

dotnet publish $monitorProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $monitorOutput
if ($LASTEXITCODE -ne 0) { throw 'Monitor publish failed.' }

Copy-Item -LiteralPath (Join-Path $monitorOutput 'CodexQuotaMonitor.exe') -Destination $pluginExecutable -Force

$expectedPayloadParent = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'installer-src\CodexQuotaMonitorSetup'))
if ([System.IO.Path]::GetFullPath((Split-Path -Parent $payload)) -ne $expectedPayloadParent) {
    throw "Unexpected payload path: $payload"
}
if (Test-Path -LiteralPath $payload) {
    [System.IO.File]::Delete($payload)
}

tar -a -cf $payload -C $pluginRoot .codex-plugin hooks skills scripts assets
if ($LASTEXITCODE -ne 0) { throw 'Plugin payload creation failed.' }

dotnet publish $installerProject -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $releaseOutput
if ($LASTEXITCODE -ne 0) { throw 'Installer publish failed.' }

$setup = Join-Path $releaseOutput 'Setup.exe'
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash
Set-Content -LiteralPath (Join-Path $releaseOutput 'Setup.exe.sha256') -Value "$hash  Setup.exe" -Encoding ascii

Write-Output "Built: $setup"
Write-Output "SHA256: $hash"
