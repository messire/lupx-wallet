<#
.SYNOPSIS
PreToolUse guard for LupexWallet - routes the commands listed below to "ask"
(explicit user confirmation), even in auto mode. Implements the "critical stop"
rule from CLAUDE.md ("REZHIM ORKESTRATSII" section, item 2: irreversible/destructive
actions) for commands specific to this project's stack that are not already covered
by generic safety rules.

Intentionally does NOT duplicate what is already covered by generic safety rules
(git push --force, git reset --hard, etc.) - only .NET/EF Core/Docker commands that
would destroy this project's local PostgreSQL data.
#>

$ErrorActionPreference = 'Stop'

try {
    $stdin = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($stdin)) { exit 0 }
    $data = $stdin | ConvertFrom-Json
} catch {
    exit 0
}

$command = $data.tool_input.command
if ([string]::IsNullOrWhiteSpace($command)) { exit 0 }

$dangerousPatterns = @(
    @{ Pattern = 'dotnet\s+ef\s+database\s+drop'; Reason = 'Drops the database via EF Core CLI (dotnet ef database drop) - irreversible.' },
    @{ Pattern = '(docker-compose|docker\s+compose)\s+down\s+.*(-v\b|--volumes\b)'; Reason = 'docker compose down with -v/--volumes deletes the named Postgres volume (all local lupex-wallet data) - irreversible.' },
    @{ Pattern = 'docker\s+volume\s+rm.*lupex'; Reason = 'Directly removes the lupex-wallet-postgres docker volume - irreversible.' },
    @{ Pattern = 'docker\s+volume\s+prune'; Reason = 'docker volume prune may remove the lupex-wallet-postgres volume along with other unused volumes - irreversible.' }
)

foreach ($p in $dangerousPatterns) {
    if ($command -match $p.Pattern) {
        $output = @{
            hookSpecificOutput = @{
                hookEventName             = 'PreToolUse'
                permissionDecision         = 'ask'
                permissionDecisionReason   = "[CLAUDE.md, REZHIM ORKESTRATSII] Irreversible action for this project: $($p.Reason) Explicit user confirmation required."
            }
        }
        $output | ConvertTo-Json -Depth 5 -Compress
        exit 0
    }
}

exit 0
