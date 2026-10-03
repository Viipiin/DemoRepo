# Phase 2 (Leave & Attendance) in one go. Safe to re-run.
# Creates the leave tables, registers the leave plugins and Custom APIs, loads leave types, the
# Standard India Policy and Karnataka holidays, creates balances for 2026-27 and accrues April..October 2026.
$ErrorActionPreference = 'Stop'

& "$PSScriptRoot/hra.ps1" validate
& "$PSScriptRoot/hra.ps1" provision
& "$PSScriptRoot/build-plugins.ps1"
& "$PSScriptRoot/hra.ps1" register-plugins
& "$PSScriptRoot/hra.ps1" seed
& "$PSScriptRoot/hra.ps1" init-balances --param LeaveYear=2026-27

# Catch up monthly Earned Leave accrual for the months already passed in this leave year.
foreach ($month in 4..10) {
    & "$PSScriptRoot/hra.ps1" run-api hra_RunLeaveAccrual --param ("Period=2026-{0:D2}" -f $month)
}

Write-Host "`nPhase 2 automated steps finished. Continue with the manual steps in docs/phase-2-runbook.md." -ForegroundColor Green
