<#
.SYNOPSIS
Stop the telemetry stack.

.DESCRIPTION
    .\telemetry\stack\down.ps1              stop the containers, keep the captured data
    .\telemetry\stack\down.ps1 -Purge       also delete Loki's volume and the network
#>
param(
    [switch]$Purge
)

$network = 'claude-telemetry'

function Test-Podman {
    param([string[]]$Args)
    # podman's `exists` subcommands signal by exit code, so check it separately.
    & podman @Args 2>$null | Out-Null
    return $LASTEXITCODE -eq 0
}

foreach ($name in 'claude-grafana', 'claude-loki') {
    if (Test-Podman @('container', 'exists', $name)) {
        podman rm -f $name | Out-Null
        Write-Host "  removed $name" -ForegroundColor DarkGray
    }
}

if ($Purge) {
    podman volume rm -f claude-loki-data 2>$null | Out-Null
    if (Test-Podman @('network', 'exists', $network)) {
        podman network rm $network 2>$null | Out-Null
    }
    Write-Host '  purged the volume and the network' -ForegroundColor DarkGray
}

Write-Host 'Stack down.' -ForegroundColor Cyan
