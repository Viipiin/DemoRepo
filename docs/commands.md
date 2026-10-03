# Command Cheat Sheet

Run these from the repo folder in the VS Code terminal (PowerShell).

| Task | Command |
|---|---|
| Get the latest merged code | `git checkout master; git pull` |
| Check the pac connection | `pac org who` |
| Build plugins and run tests | `./scripts/build-plugins.ps1` |
| Check the connection and solution | `./scripts/hra.ps1 check` |
| Create or update tables, forms, views, roles and settings | `./scripts/hra.ps1 provision` |
| Upload plugins after a change | `./scripts/build-plugins.ps1; ./scripts/hra.ps1 register-plugins` |
| Load synthetic sample data | `./scripts/hra.ps1 seed` |
| Phase 1 in one go | `./scripts/run-phase1.ps1` |
| Export the solution to Git | `./scripts/export-solution.ps1` |
| Show detailed errors | `$env:HRA_VERBOSE = "1"` before running a command |
| Sign in as a different account | Delete the `.hra-token-cache` folder |

Phase runbooks: [Phase 1](phase-1-runbook.md)
