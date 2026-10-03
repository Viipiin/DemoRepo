# Phase 2b (leave approvals, notifications, accrual and year-end flows) in one go. Safe to re-run.
$ErrorActionPreference = 'Stop'

& "$PSScriptRoot/hra.ps1" validate
& "$PSScriptRoot/build-plugins.ps1"
& "$PSScriptRoot/hra.ps1" provision
& "$PSScriptRoot/hra.ps1" register-plugins

Write-Host "`nPhase 2b automated steps finished. Link the connection references and turn the flows on: docs/phase-2b-runbook.md." -ForegroundColor Green
