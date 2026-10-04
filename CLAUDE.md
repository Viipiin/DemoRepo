# CLAUDE.md: HR Automation project memory

Claude Code reads this file at the start of every session. Keep it current: update **Status** and **Next** at the end of each phase.

## Project
A model-driven HR app on Microsoft Dataverse / Power Platform for an **Indian company of about 100 employees** (one legal entity, office in **Karnataka**, Bengaluru). The user is building it phase by phase with Claude.

| Item | Value |
|---|---|
| Dev environment | **Viipiin-Dev**, `https://viipiin.crm.dynamics.com/` (Developer environment, tenant `them365dev.onmicrosoft.com`, signed in as `admin@them365dev.onmicrosoft.com`) |
| Prod | "Viipiin-Prod", **not created yet**. No Test environment |
| Solution | Unique name **`HRAutomation`** (display name "HR Automation") |
| Publisher | `hrautomation`, prefix **`hra`**, choice value prefix **81799** (option values start at 817990000) |
| Base currency | USD. INR added and set as the default currency |

## How we work (important)
- **The user runs every command themselves** in the VS Code terminal (Windows PowerShell), from `D:\Claude Projects\DemoRepo`. Claude writes the files, gives the exact commands, and waits for the pasted output. Claude never assumes a command ran.
- **The user only pulls merged code from `master`.** Every change goes through a PR: commit on the working branch, push, open a PR, the user merges, then runs `git checkout master; git pull`. A branch whose PR is merged is restarted from `origin/master` before new work.
- **JSON wherever possible.** Tables, columns, choices, forms, views, roles, teams, column security, environment variables, plugin steps, Custom APIs, connection references, cloud flows and sample data are all JSON in `model/` (format in `model/README.md`). Only plugin logic and the provisioner are C#. Never hard-code the model in C#.
- Run `validate` after every model change. It checks the JSON offline and reports problems by file and column.
- **Testing:** pure business rules live in `HrRules.cs` / `LeaveRules.cs` and have xUnit tests. The cloud session can build and test (.NET 8 SDK from apt), but **can't reach Dataverse**, so the first real run is always the user's.
- **"Defaults, simplest setup"** (Phase 0, `docs/architecture.md` section 0): one **HR Hub** app for everyone; only licensed staff use it in v1; no pipeline or Test environment until go-live; Power BI, Power Pages and Copilot Studio are optional later.

## Repo map
- `model/`: the JSON model (the source of truth). `tables/NN-<table>.json`, `choices.json`, `security.json`, `environment-variables.json`, `plugins.json`, `custom-apis.json`, `connection-references.json`, `flows/*.json`, `sample-data.json` (synthetic only).
- `src/tools/HRA.Provisioner`: .NET 8 tool. Commands: `validate`, `check`, `provision`, `register-plugins`, `seed`, `run-api`, `init-balances`, `rollover`. Run it with `./scripts/hra.ps1 <command>`.
- `src/plugins/HRAutomation.Plugins`: C# plugins (net462, signed) and Custom APIs. `...Plugins.Tests`: xUnit tests (62).
- `scripts/`: `run-phase1.ps1`, `run-phase2.ps1`, `run-phase2b.ps1`, `build-plugins.ps1`, `export-solution.ps1`, `hra.ps1`.
- `src/solution/`: the unpacked exported solution. The user commits it after each phase through a `phaseN-export` PR.
- `docs/`: `architecture.md` (design and decisions), `hr-automation/data-model.md`, `hr-automation/PROMPT.md`, `phase-*-runbook.md`, `commands.md`.

## Key design decisions
- **Ownership follows the reporting line.** Plugin P-02 makes the reporting manager's user the owner of an employee's records, or the **HR team** if the manager has no user. Records are shared with the employee's own user. Manager hierarchy security is on (depth 3).
- **Owner teams must hold a role** to own records (HR → HR Manager, and so on, in `security.json`).
- **Leave balances change only through plugins:** leave requests (Pending/Taken), Leave Adjustments (comp-off, encashment, correction, carry forward, lapse) and the accrual/rollover Custom APIs. Available is recalculated by plugin.
- **India rules:** leave year April–March ("2026-27"); weekly-off patterns per location; holidays per state calendar; PAN, Aadhaar (last 4 only), UAN, ESIC and bank details are column-secured (HR team); DPDP Act retention.
- **Per-employee API calls** (`init-balances`, `rollover`, the year-end flow) stay inside the 2-minute plugin limit.

## Lessons from the real runs (don't repeat)
- `systemform` has no `createdon` column. Pick the default main form with `isdefault`.
- Replace only `<tabs>` in an existing form's XML. Don't rebuild the whole form.
- Quick find views: only add conditions to Dataverse's own `isquickfindfields` filter, and send `returnedtypecode` and `querytype` with the update. Failures are warnings only.
- Teams need a security role before they can own records.

## Status (as of 2026-10-03)
- ✅ Phase 0: design (`docs/architecture.md`).
- ✅ Phase 1: Core HR. 8 tables, roles, column security, plugins P-01/P-02, 50 sample employees. All checks passed and the solution is exported.
- ✅ Phase 2: Leave & Attendance. 9 tables, leave plugins, `hra_InitializeLeaveBalances`, `hra_RunLeaveAccrual`, Standard India Policy and Karnataka holidays. Solution exported (PR #12).
- 🟡 **Phase 2b: merged (PR #13), not yet confirmed on Dev.** 4 flows (Leave Approval, Decision Notification, Monthly Accrual, Year-End Rollover), the `hra_RunYearEndRollover` API and `hra_HRTeamEmail`. Steps: `./scripts/run-phase2b.ps1`, link the 3 connection references, turn the flows on, test (`docs/phase-2b-runbook.md`). The hand-written flow JSON may need a fix on first activation; ask for the designer's error message.
- ⚠️ The Karnataka sample calendars have fixed-date holidays only. The user should add festival holidays from the official list.

## Next: Phase 3, Recruitment & Onboarding (planned, not started)
**Before starting:** confirm Phase 2b works on Dev (flows on, an approval tested), then give the user a short plan and wait for "go".

**Tables (from `docs/architecture.md` §2.1, model files 18 onwards):** Job Requisition (approval), Candidate (DPDP consent and Retain Until), Job Application (business process flow: Screening → Interview → Offer → Hired), Interview (with an N:N panel to Employee), Interview Feedback (scorecard), Offer (with approval when above budget), Checklist Template and Template Item, Employee Checklist, Checklist Task (owned by the HR/IT/Admin/Finance teams), Separation.

**Logic:**
- Offer Accepted creates the Employee record, which runs the existing plugins (balances and so on).
- The onboarding checklist is generated from a template.
- Separation Approved sets On Notice and Last Working Day and creates the offboarding checklist and asset-return task.
- Candidate anonymisation after `hra_CandidateRetentionMonths`.
- A "Compensation" column security profile for CTC fields (the Recruiter role can read).

**Flows (JSON):** requisition approval (F-20), offer approval (F-21), candidate retention (F-30), probation and contract reminders (F-26).

**Open item:** a business process flow can't be expressed in the current JSON model yet. Either add BPF support to the provisioner, or build the BPF in the designer and keep it in the exported solution.
