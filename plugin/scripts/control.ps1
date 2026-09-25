[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('show', 'hide', 'toggle', 'refresh', 'exit', 'status', 'variant-a', 'variant-b', 'variant-c', 'theme-auto', 'theme-light', 'theme-dark')]
    [string]$Action
)

function Invoke-MonitorCommand {
    param([string]$Command)

    $pipe = [System.IO.Pipes.NamedPipeClientStream]::new(
        '.',
        'CodexQuotaMonitor.Control',
        [System.IO.Pipes.PipeDirection]::InOut,
        [System.IO.Pipes.PipeOptions]::Asynchronous
    )

    try {
        $pipe.Connect(800)
        $writer = [System.IO.StreamWriter]::new($pipe, [System.Text.UTF8Encoding]::new($false), 1024, $true)
        $reader = [System.IO.StreamReader]::new($pipe, [System.Text.UTF8Encoding]::new($false), $false, 1024, $true)
        $writer.AutoFlush = $true
        $writer.WriteLine($Command)
        return $reader.ReadLine()
    } finally {
        $pipe.Dispose()
    }
}

try {
    $result = Invoke-MonitorCommand -Command $Action
} catch {
    & (Join-Path $PSScriptRoot 'start-monitor.ps1')
    Start-Sleep -Milliseconds 700
    $result = Invoke-MonitorCommand -Command $Action
}

if ($result) {
    Write-Output $result
}
