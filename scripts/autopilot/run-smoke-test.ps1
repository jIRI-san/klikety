<#
.SYNOPSIS
    Smoke test for autopilot infrastructure.
.DESCRIPTION
    Invokes launch.ps1 with the smoke-test plan in host mode.
    Verifies the end-to-end flow works: auth, worktree, agent execution, commit.

    Prerequisites:
    - Copilot CLI token stored in Windows Credential Manager (target: copilot-autopilot)
    - Git configured with push access
    - CredentialManager PowerShell module installed

    Usage:
    .\run-smoke-test.ps1
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path $PSScriptRoot -Parent
$LaunchScript = Join-Path $ScriptDir 'launch.ps1'

Write-Host "=== Autopilot Smoke Test ==="
Write-Host ""
Write-Host "This will:"
Write-Host "  1. Create a worktree for 'feature/smoke-test'"
Write-Host "  2. Run Copilot CLI to add a comment to AssemblyInfo.cs"
Write-Host "  3. Commit and push"
Write-Host ""
Write-Host "Press Ctrl+C to abort, or any key to continue..."
$null = $Host.UI.RawUI.ReadKey('NoEcho,IncludeKeyDown')

& $LaunchScript -PlanSlug '020-autonomous-plan-execution/smoke-test' -Mode 'whole-plan' -Runtime 'host'

$exitCode = $LASTEXITCODE
Write-Host ""
if ($exitCode -eq 0) {
    Write-Host "SMOKE TEST PASSED" -ForegroundColor Green
} elseif ($exitCode -eq 42) {
    Write-Host "SMOKE TEST: Agent hit @human step (expected for some tests)" -ForegroundColor Yellow
} else {
    Write-Host "SMOKE TEST FAILED (exit code: $exitCode)" -ForegroundColor Red
}

exit $exitCode
