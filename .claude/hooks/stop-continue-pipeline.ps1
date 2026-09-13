<#
.SYNOPSIS
Stop hook - mechanical implementation of "do not stop without a critical reason" from
CLAUDE.md, "REZHIM ORKESTRATSII" section. Reads pipeline-state.json next to this script
(one level up, in .claude/); if status is "in_progress", blocks the turn from ending and
asks the agent to continue the pipeline. In every other case (no file, status not
in_progress) it exits silently - normal sessions are unaffected.

State is managed by hand by the orchestrator (the main agent): it sets status=in_progress
at the start of a /pipeline run covering the whole given scope, status=blocked on a
critical stop (CLAUDE.md, "REZHIM ORKESTRATSII", "Kriticheskiy stop" section), status=done
on completion. The file is NOT committed (see .gitignore) - it is ephemeral session state,
not a project artifact.

Runaway protection:
- Staleness guard: if updated_at is older than 30 minutes, treat the run as abandoned
  and let the turn end.
- Circuit breaker: after MaxConsecutiveContinues blocks in a row without the counter
  being reset (reset by the orchestrator on real progress), force-allow the stop and
  flip status to "blocked" with a reason for the user to see.
#>

$ErrorActionPreference = 'Stop'
$MaxConsecutiveContinues = 8
$StalenessMinutes = 30

$stateFile = Join-Path $PSScriptRoot '..\pipeline-state.json'

if (-not (Test-Path $stateFile)) { exit 0 }

try {
    $state = Get-Content $stateFile -Raw | ConvertFrom-Json
} catch {
    exit 0
}

if (-not $state -or $state.status -ne 'in_progress') { exit 0 }

try {
    $updatedAt = [DateTimeOffset]::Parse($state.updated_at)
    if (([DateTimeOffset]::UtcNow - $updatedAt).TotalMinutes -gt $StalenessMinutes) {
        exit 0
    }
} catch {
    # Missing/invalid updated_at - do not block, to avoid hanging forever.
    exit 0
}

$continues = 0
if ($state.PSObject.Properties.Name -contains 'consecutive_continues') {
    $continues = [int]$state.consecutive_continues
}

if ($continues -ge $MaxConsecutiveContinues) {
    $state.status = 'blocked'
    $state | Add-Member -NotePropertyName 'reason' -NotePropertyValue 'circuit breaker: consecutive auto-continue limit reached without progress being reset - stopped for user review' -Force
    $state.updated_at = (Get-Date).ToUniversalTime().ToString('o')
    ($state | ConvertTo-Json -Depth 5) | Set-Content -Path $stateFile -Encoding utf8
    exit 0
}

$state.consecutive_continues = $continues + 1
$state.updated_at = (Get-Date).ToUniversalTime().ToString('o')
($state | ConvertTo-Json -Depth 5) | Set-Content -Path $stateFile -Encoding utf8

$reason = 'The pipeline is not finished yet (docs/PROGRESS.md, "Sleduyushchiy shag" section). ' +
    'Continue per the protocol in CLAUDE.md, "REZHIM ORKESTRATSII" section - do not return ' +
    'control until the whole given scope is closed (status=done in .claude/pipeline-state.json) ' +
    'or a critical stop is hit (status=blocked, see the same section).'

$output = @{
    decision = 'block'
    reason   = $reason
}
$output | ConvertTo-Json -Compress
exit 0
