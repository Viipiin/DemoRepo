# Phase 1 in one go: build and test plugins, create the Core HR tables, register plugins, load sample data.
# Safe to re-run. A browser window opens for sign-in the first time.
$ErrorActionPreference = 'Stop'

& "$PSScriptRoot/hra.ps1" validate
& "$PSScriptRoot/build-plugins.ps1"
& "$PSScriptRoot/hra.ps1" check
& "$PSScriptRoot/hra.ps1" provision
& "$PSScriptRoot/hra.ps1" register-plugins
& "$PSScriptRoot/hra.ps1" seed

Write-Host "`nPhase 1 automated steps finished. Continue with the manual steps in docs/phase-1-runbook.md." -ForegroundColor Green
