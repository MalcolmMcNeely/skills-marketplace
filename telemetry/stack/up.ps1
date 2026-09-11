<#
.SYNOPSIS
Start Loki and Grafana in podman, ready to receive Claude Code telemetry.

.DESCRIPTION
Two containers, no compose provider needed. Loki ingests OTLP directly, so there
is no collector in the middle. Grafana comes up with the datasource and the skill
usage dashboard already provisioned.

    .\telemetry\stack\up.ps1

Then, in the window you want to measure:

    . .\telemetry\session-env.ps1 stack
    claude

.PARAMETER Reset
Delete Loki's stored data before starting, for a clean window.
#>
param(
    [switch]$Reset
)

$ErrorActionPreference = 'Stop'

$stackDir = $PSScriptRoot
$network = 'claude-telemetry'
$lokiImage = 'docker.io/grafana/loki:3.5.9'
$grafanaImage = 'docker.io/grafana/grafana:12.2.0'

function Test-Podman {
    param([string[]]$Args)
    # podman's `exists` subcommands signal by exit code, so check it separately.
    & podman @Args 2>$null | Out-Null
    return $LASTEXITCODE -eq 0
}

function Remove-IfPresent($name) {
    if (Test-Podman @('container', 'exists', $name)) {
        podman rm -f $name | Out-Null
    }
}

Write-Host 'Starting the Claude Code telemetry stack' -ForegroundColor Cyan

# A user-defined network lets Grafana reach Loki by container name.
if (-not (Test-Podman @('network', 'exists', $network))) {
    podman network create $network | Out-Null
}

Remove-IfPresent 'claude-grafana'
Remove-IfPresent 'claude-loki'

if ($Reset) {
    podman volume rm -f claude-loki-data 2>$null | Out-Null
    Write-Host '  wiped Loki data' -ForegroundColor DarkGray
}

# Loki's packaged config already has auth off, filesystem storage and schema v13,
# which is what enables structured metadata. Nothing to mount.
podman run -d --name claude-loki --network $network `
    -p 3100:3100 `
    -v claude-loki-data:/loki `
    $lokiImage | Out-Null
Write-Host '  loki      http://localhost:3100' -ForegroundColor Green

podman run -d --name claude-grafana --network $network `
    -p 3000:3000 `
    -e GF_AUTH_ANONYMOUS_ENABLED=true `
    -e GF_AUTH_ANONYMOUS_ORG_ROLE=Admin `
    -e GF_AUTH_DISABLE_LOGIN_FORM=true `
    -e GF_AUTH_BASIC_ENABLED=false `
    -e GF_FEATURE_TOGGLES_ENABLE=lokiStructuredMetadata `
    -v "$stackDir\grafana\provisioning:/etc/grafana/provisioning:ro" `
    -v "$stackDir\grafana\dashboards:/var/lib/grafana/dashboards:ro" `
    $grafanaImage | Out-Null
Write-Host '  grafana   http://localhost:3000' -ForegroundColor Green

# A cold start on a fresh volume took about four minutes on this machine. Loki
# answers /ready with 503 the whole time, so a short timeout reports a failure
# that is really just patience.
Write-Host 'Waiting for Loki to accept writes (a cold start takes a few minutes)' -NoNewline
$ready = $false
foreach ($i in 1..180) {
    try {
        $r = Invoke-WebRequest -Uri 'http://localhost:3100/ready' -UseBasicParsing -TimeoutSec 3 -SkipHttpErrorCheck
        if ($r.StatusCode -eq 200) { $ready = $true; break }
    } catch { }
    if ($i % 10 -eq 0) { Write-Host '.' -NoNewline }
    Start-Sleep -Seconds 2
}
Write-Host ''

if (-not $ready) {
    Write-Host 'Loki did not come up. Check: podman logs claude-loki' -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host 'Ready.' -ForegroundColor Cyan
Write-Host '  Dashboard   http://localhost:3000/d/claude-skill-usage'
Write-Host '  Next        . .\telemetry\session-env.ps1 stack   then run claude in that window'
Write-Host '  Stop        .\telemetry\stack\down.ps1'
