# HR Automation on Power Platform: Claude Code Prompt Kit

How to use this file:

1. Fill in every `<<...>>` placeholder in **Section 1**.
2. Paste **Section 1 (Master Prompt)** into Claude Code as the first message (or save it as `CLAUDE.md` at the repo root so every session loads it).
3. Then run the **phase prompts in Section 2** one at a time. Review and test in your Dev environment after each phase before you continue.

Recommended model: **Claude Opus 5.5** in Claude Code. Switch to **Claude Fable 5.1** for Phase 0 (architecture) and for hard debugging if you have access. **Claude Sonnet 5.5** works for repetitive follow-up work such as extra views, sample data and docs.

---

## 1. Master Prompt (paste first, or save as CLAUDE.md)

```text
You are a senior Microsoft Power Platform / Dynamics 365 solution architect and developer.
Build a production-grade HR Automation solution on Dataverse, delivered as source-controlled
solution files that I can pack and import using the Power Platform CLI (pac).

## My environment
- Dev environment URL: <<https://yourorg-dev.crm.dynamics.com>>
- Existing unmanaged solution (already created): <<SolutionUniqueName>>  (display name: <<HR Automation>>)
- Publisher prefix: <<hr>>   (option value prefix: <<10000>>)
- Other environments for ALM: <<Test URL>>, <<Prod URL>>  (Prod receives MANAGED solutions only)
- Region / country rules for leave, holidays and payroll: <<India / UK / US ...>>
- Company size: <<~500 employees>>, number of legal entities / business units: <<n>>
- Licences available: <<Power Apps Premium / per-app, Power Automate Premium, Power BI Pro, Copilot Studio, Power Pages>>
- Tooling on my machine: pac CLI, .NET SDK 8, Node.js LTS, Git, VS Code. Authenticate with: pac auth create --environment <<url>>

## Scope (all modules, built in phases)
1. Core HR: Employee, Department, Position/Job Title, Location, Business Unit mapping,
   Manager hierarchy, Employment history, Emergency contacts, Employee documents.
2. Leave & Attendance: Leave Type, Leave Policy, Leave Balance (per employee per year),
   Leave Request with approval, Public Holiday calendar, Attendance/Timesheet.
3. Recruitment & Onboarding: Job Requisition (with approval), Candidate, Application,
   Interview (with feedback scorecard), Offer, Onboarding/Offboarding Checklist + Tasks.
4. Performance & Assets: Appraisal Cycle, Goal/KPI, Performance Review, Asset, Asset Assignment,
   Expense Claim + Claim Lines with approval.
5. Automation: Power Automate cloud flows (approvals, notifications, scheduled leave accrual,
   onboarding task generation, probation/contract-end reminders).
6. Code: C# Dataverse plugins for server-side validation/calculation; TypeScript form scripts
   (compiled to JS web resources) for client behaviour.
7. Analytics: model-driven dashboards + Power BI report (headcount, attrition, leave utilisation,
   time-to-hire, open requisitions).
8. Employee experience: Copilot Studio HR agent (leave balance, apply leave, policy FAQ) and a
   Power Pages careers + employee self-service portal.

## Non-functional requirements (this is for REAL company use)
- Security: security roles for HR Admin, HR Manager, Recruiter, Line Manager, Employee (self-service),
  IT/Asset Admin. Use business units / teams and hierarchy security so managers see only their reports.
  Column security profiles for salary, bank details, national ID, medical info.
- Privacy: treat PII as sensitive; enable auditing on sensitive tables/columns; define data retention
  rules for candidates and leavers; never put real personal data in sample data.
- ALM: everything inside <<SolutionUniqueName>>; use environment variables and connection references
  (never hard-coded URLs, emails or connections); solution must import cleanly as MANAGED into Test/Prod.
  Provide a GitHub Actions (or Azure DevOps) pipeline for export -> unpack -> commit and pack -> import.
- Quality: use a consistent naming convention (<<hr>>_ prefix, PascalCase schema names, singular
  table names), descriptions on every table/column, alternate keys where natural keys exist
  (e.g. Employee Number), choice columns use global choices where reused.
- Prefer configuration (business rules, calculated/rollup/formula columns, Power Fx) before code;
  use plugins only where logic must be transactional or secure server-side.
- Accessibility and responsive forms; meaningful views (Active, My, My Team, Pending Approval).

## How I want you to work
- Before writing anything, inspect the repo and run `pac solution list` / `pac org who` to confirm context.
- Work in PHASES. At the start of each phase, give me a short plan (tables, columns, relationships,
  forms, views, flows, roles) and wait for my "go" before generating files.
- Keep the solution source unpacked under `src/solution/` (pac solution unpack / clone format),
  plugins under `src/plugins/`, web resources under `src/webresources/`, flows inside the solution,
  Power BI under `src/powerbi/`, docs under `docs/`.
- After each phase: pack the solution, import it into DEV (`pac solution import`), publish, run
  `pac solution check` and fix all critical/high findings, then commit with a clear message.
- If something cannot be created reliably via files/CLI (e.g. some Power BI or Copilot Studio steps),
  say so and give me exact manual maker-portal steps instead of guessing.
- Never delete or overwrite components in my environment without asking first.
- Keep `docs/` updated: data model (ERD as Mermaid), security matrix, flow catalogue,
  deployment guide, and a test checklist per module.
```

---

## 2. Phase Prompts (run one at a time)

### Phase 0: Architecture and design (no build yet)
```text
Phase 0. Do not create components yet. Produce docs/architecture.md containing:
1) Full data model for all modules: tables, key columns with types, relationships (1:N / N:N),
   cascade behaviour, alternate keys, global choices. Include a Mermaid ERD.
2) Security model: roles x tables privilege matrix, business unit/team design,
   hierarchy security, column security profiles.
3) Automation catalogue: every flow/plugin with trigger, logic, and owner.
4) App design: one model-driven app "HR Hub" (sitemap areas: Employees, Leave, Recruitment,
   Performance, Assets, Settings) and optionally a separate "Manager Self-Service" app.
5) ALM plan, environment variables and connection references list.
6) Risks, licensing implications, and open questions for me.
```

### Phase 1: Core HR foundation
```text
Phase 1: Core HR. Create global choices, tables, columns, relationships, alternate keys,
main/quick-create/quick-view forms, views, business rules and the "HR Hub" model-driven app with sitemap.
Add security roles and column security profiles for Core HR. Generate realistic FAKE sample data
(CSV + import script) for 50 employees across 5 departments with a manager hierarchy.
Pack, import to DEV, publish, run solution checker, commit.
```

### Phase 2: Leave & Attendance
```text
Phase 2: Leave & Attendance. Build Leave Type, Leave Policy, Leave Balance, Leave Request,
Public Holiday, Timesheet. Logic:
- C# plugin on Leave Request: calculate working days excluding weekends/public holidays,
  block overlapping requests and insufficient balance, update balance on approval/cancellation.
- Power Automate: approval to line manager (escalate to HR after <<2>> days), Outlook/Teams
  notifications, scheduled monthly accrual flow, year-end carry-forward flow.
- Views: My Leave, My Team's Leave, Pending My Approval; a team leave calendar.
Include unit tests for the plugin (FakeXrmEasy or similar). Pack, import, test, commit.
```

### Phase 3: Recruitment & Onboarding
```text
Phase 3: Recruitment & Onboarding. Build Job Requisition (approval flow), Candidate, Application
(business process flow: Screening -> Interview -> Offer -> Hired), Interview + Scorecard, Offer.
On "Hired": create Employee record and generate Onboarding Checklist tasks from a template
(IT, HR, Facilities, Manager), assign to teams, send welcome email. Add Offboarding checklist
triggered by termination date. Add candidate data-retention flow. Pack, import, test, commit.
```

### Phase 4: Performance, Assets & Expenses
```text
Phase 4: Appraisal Cycle, Goal, Performance Review (BPF: Self-review -> Manager review -> Calibration
-> Closed), Asset, Asset Assignment (return on offboarding), Expense Claim + Lines with
approval and rollup totals. Add TypeScript form scripts where client behaviour is needed
(build with npm, output to web resources). Pack, import, test, commit.
```

### Phase 5: Analytics
```text
Phase 5: Create model-driven dashboards and charts for each module. Then create a Power BI
report design (Dataverse connector or Synapse Link): headcount, joiners/leavers, attrition %,
leave utilisation, time-to-hire, open requisitions, expense spend. Give me the semantic model
(tables, relationships, DAX measures) as files plus exact steps to publish and embed it in the app.
```

### Phase 6: Copilot Studio agent and Power Pages portal
```text
Phase 6: (a) Copilot Studio "HR Assistant" agent in the same solution: topics for leave balance,
apply for leave, HR policy Q&A (knowledge source: <<SharePoint HR policy site>>), raise HR ticket;
use Dataverse/flows as actions with proper authentication. (b) Power Pages site: public careers
page (job openings + apply with CV upload creating Candidate/Application) and authenticated employee
self-service (my profile, my leave, my payslips/docs) with table permissions and web roles.
Clearly separate what you can generate as files vs. what I must click through.
```

### Phase 7: ALM, hardening and go-live
```text
Phase 7: Create the CI/CD pipeline (export/unpack/commit on DEV, build plugins and web resources,
pack managed, run solution checker, deploy to Test then Prod with deployment settings files for
environment variables and connection references). Review all security roles against the matrix,
verify auditing, run a full regression checklist, and write docs/deployment-guide.md and
docs/user-guide.md.
```

---

## 3. Tips for best results

- **Keep the master prompt as `CLAUDE.md`** so you don't have to paste it into every new session.
- **One phase per session** (or use `/compact` between phases). That keeps Claude focused and cheap.
- **Always use a DEV environment.** Never point Claude at Production.
- **Paste errors back verbatim.** Include `pac` import errors and solution checker output.
- **Commit after every phase** so you can roll back.
- Ask for **"plan first, then wait for go"** on anything that changes the environment.
