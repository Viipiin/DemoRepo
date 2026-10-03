# Command Cheat Sheet

Run these from the repo folder in the VS Code terminal (PowerShell).

| Task | Command |
|---|---|
| Get the latest merged code | `git checkout master; git pull` |
| Check the pac connection | `pac org who` |
| Check the JSON model (offline) | `./scripts/hra.ps1 validate` |
| Build plugins and run tests | `./scripts/build-plugins.ps1` |
| Check the connection and solution | `./scripts/hra.ps1 check` |
| Apply the JSON model in `model/` (tables, columns, choices, forms, views, roles, settings) | `./scripts/hra.ps1 provision` |
| Upload plugins after a change | `./scripts/build-plugins.ps1; ./scripts/hra.ps1 register-plugins` |
| Load the sample data in `model/sample-data.json` | `./scripts/hra.ps1 seed` |
| Phase 1 in one go | `./scripts/run-phase1.ps1` |
| Phase 2 in one go | `./scripts/run-phase2.ps1` |
| Create missing leave balances | `./scripts/hra.ps1 init-balances` (add `--param LeaveYear=2026-27` for a specific year) |
| Run leave accrual for a month | `./scripts/hra.ps1 run-api hra_RunLeaveAccrual --param Period=2026-11` |
| Year-end rollover (close a leave year) | `./scripts/hra.ps1 rollover --param FromLeaveYear=2026-27` |
| Phase 2b in one go | `./scripts/run-phase2b.ps1` |
| Export the solution to Git | `./scripts/export-solution.ps1` |
| Show detailed errors | `$env:HRA_VERBOSE = "1"` before running a command |
| Sign in as a different account | Delete the `.hra-token-cache` folder |

Phase runbooks: [Phase 1](phase-1-runbook.md), [Phase 2](phase-2-runbook.md), [Phase 2b](phase-2b-runbook.md). How to edit the model: [model/README.md](../model/README.md)
