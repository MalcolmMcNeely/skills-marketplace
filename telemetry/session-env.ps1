<#
.SYNOPSIS
Point this PowerShell session's Claude Code at a local telemetry target.

.DESCRIPTION
Sets environment variables in the CURRENT session only. It writes no settings
file, so every other Claude session on the machine is untouched and closing
this window undoes it.

Dot-source it, or the variables land in a child process and vanish:

    . .\telemetry\session-env.ps1 sink
    . .\telemetry\session-env.ps1 stack
    . .\telemetry\session-env.ps1 off

sink   raw JSON to the Node sink on 4318, for reading with your own eyes
stack  protobuf to Loki on 3100, for the Grafana dashboard
off    clears every variable this script sets

.NOTES
This does not change what Claude Code sends to Anthropic. That is a separate
pipe, already on, and it redacts skill names. See docs/research/skill-usage-telemetry.md.
#>
param(
    [Parameter(Position = 0)]
    [ValidateSet('sink', 'stack', 'off')]
    [string]$Mode = 'sink'
)

$vars = @(
    'CLAUDE_CODE_ENABLE_TELEMETRY'
    'OTEL_LOGS_EXPORTER'
    'OTEL_METRICS_EXPORTER'
    'OTEL_TRACES_EXPORTER'
    'OTEL_EXPORTER_OTLP_PROTOCOL'
    'OTEL_EXPORTER_OTLP_ENDPOINT'
    'OTEL_LOG_TOOL_DETAILS'
    'OTEL_LOGS_EXPORT_INTERVAL'
    'OTEL_RESOURCE_ATTRIBUTES'
)

foreach ($v in $vars) { Remove-Item "env:$v" -ErrorAction SilentlyContinue }

if ($Mode -eq 'off') {
    Write-Host 'Telemetry variables cleared. Claude Code exports nothing to a local target.' -ForegroundColor Yellow
    return
}

# Events are LOGS, not metrics. Without a logs exporter there is no per-skill
# data at all, whatever else is set.
$env:CLAUDE_CODE_ENABLE_TELEMETRY = '1'
$env:OTEL_LOGS_EXPORTER = 'otlp'
$env:OTEL_METRICS_EXPORTER = 'none'
$env:OTEL_TRACES_EXPORTER = 'none'

# Lifts the "custom_skill" placeholder so a private catalogue reports real names.
# This is the single line that makes per-skill counting work.
$env:OTEL_LOG_TOOL_DETAILS = '1'

# Default is 5000 ms. Shorter means a short probe session still flushes.
$env:OTEL_LOGS_EXPORT_INTERVAL = '2000'

switch ($Mode) {
    'sink' {
        # http/json so the sink can print the bodies. protobuf arrives as binary.
        $env:OTEL_EXPORTER_OTLP_PROTOCOL = 'http/json'
        $env:OTEL_EXPORTER_OTLP_ENDPOINT = 'http://127.0.0.1:4318'
        $target = 'Node raw sink on 127.0.0.1:4318 (start it with: node telemetry/sink/raw-sink.mjs)'
    }
    'stack' {
        # Loki ingests OTLP natively at /otlp, so no collector sits in between.
        $env:OTEL_EXPORTER_OTLP_PROTOCOL = 'http/protobuf'
        $env:OTEL_EXPORTER_OTLP_ENDPOINT = 'http://127.0.0.1:3100/otlp'
        $target = 'Loki on 127.0.0.1:3100 (start it with: .\telemetry\stack\up.ps1)'
    }
}

Write-Host "Claude Code in THIS session now exports to:" -ForegroundColor Green
Write-Host "  $target"
Write-Host "No settings file was written. Close this window to undo." -ForegroundColor DarkGray
Write-Host ''
$vars | Where-Object { Test-Path "env:$_" } | ForEach-Object {
    '{0,-34} {1}' -f $_, (Get-Item "env:$_").Value
}
