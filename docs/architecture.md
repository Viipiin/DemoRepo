# HR Automation: Phase 0 Architecture

| | |
|---|---|
| Solution | `hrautomation` (HR Automation), publisher prefix `hra`, choice value prefix `81799` |
| Environments | Dev: `https://viipiin.crm.dynamics.com/` (Developer). Prod: **Viipiin-Prod**. Test: none yet |
| Company | India, about 100 employees, 1 legal entity |
| Status | **Draft for review.** Nothing has been built yet. Answer the questions in section 9, and then `data-model.md` is updated with the agreed changes in section 2.3 |

This document covers the six Phase 0 deliverables:
1. Key design decisions
2. Data model review: column specifications, cascade rules and proposed changes
3. Security model
4. Automation catalogue
5. App design
6. ALM, environment variables and connection references
7. Licensing, risks and open questions

---

## 1. Key design decisions

| # | Decision | Why |
|---|---|---|
| D1 | **The employee record is a custom table (`hra_Employee`), not the built-in `systemuser`, `contact` or HR tables.** It links to `systemuser` for licensed staff and to `contact` for portal users. | Not every employee will be a licensed Dataverse user (see D3). A custom table keeps all 100 employees in one place. |
| D2 | **Ownership follows the reporting line.** An employee's record and their transactions (leave, expenses, reviews) are owned by the reporting manager's user account, set by a plugin. HR has organization-wide access. | Managers can then see their own team with plain User-level access, without giving every employee a licence. Manager hierarchy security covers the manager's manager. |
| D3 | **Licences are split by persona (recommended).** HR, managers, recruiters, IT and Finance (roughly 20–25 people) get Power Apps Premium and use the model-driven apps. The other employees use self-service through **Power Pages** and/or a **Copilot Studio agent in Teams**. | 100 Power Apps Premium licences cost much more than a portal and an agent. See section 7 and question Q1. |
| D4 | **Configuration comes first.** Business rules, formula and rollup columns, and Power Fx come before plugins. Plugins are used only for transactional or security-critical logic: leave day calculation, balance updates, ownership. | Easier to maintain. Logic that can't be bypassed lives on the server. |
| D5 | **Balances change only through transactions.** Leave balances change only through Leave Requests and a new `hra_LeaveAdjustment` table, never by editing the numbers directly. | Gives an audit trail for every day of leave, which matters for encashment and full-and-final settlement. |
| D6 | **The leave year runs April–March** and is stored as text such as "2026-27". The start month is an environment variable. | Matches the Indian financial year, and can be changed per company. |
| D7 | **Configurable values live in environment variables** (escalation days, team emails, URLs). Every flow uses connection references. Nothing is hard-coded. | The solution then imports into Prod as a managed solution without edits. |
| D8 | **People data is never deleted.** Records are deactivated. Delete is set to Restrict on Employee relationships. Candidate data is purged only by the retention flow. | Required for audit, and for the DPDP Act rules on retention and deletion. |
| D9 | **Currency is INR.** INR is added as a currency and set as every user's default currency. | `crm.dynamics.com` environments are in the North America region and usually have USD as the base currency, which can't be changed after the environment is created. See Q4. |

---

## 2. Data model review

### 2.1 Column specifications

**Standard column types used:**
- **Text** (length in brackets, for example Text(100)), with formats Email, Phone and URL
- **Multiline text** (4000)
- **Autonumber**
- **Whole number** and **Decimal** (precision in brackets)
- **Currency** (INR)
- **Date only** (behaviour: Date only) and **Date and time** (behaviour: User local)
- **Choice**: a global choice from `data-model.md`, or local if marked (L)
- **Yes/No**
- **Lookup**, **File** (max size in brackets), **Formula** (Power Fx) and **Rollup**

The **Req** column means: **R** = business required, **Rec** = business recommended, blank = optional. 🔒 marks a secured column.

Every table also gets the standard system columns: Owner, Status, Status Reason, Created On and Modified On.

#### Core HR

**`hra_Department`** (Org-owned). Primary column: `hra_Name`.
| Column | Type | Req | Notes |
|---|---|---|---|
| Name | Text(100) | R | |
| Code | Text(20) | R | Alternate key |
| Parent Department | Lookup → hra_Department | | Self-referential, so departments can be nested |
| Department Head | Lookup → hra_Employee | Rec | |
| Cost Centre | Text(20) | | |
| Owner Team | Lookup → team | Rec | The department's Dataverse team. Used for checklist tasks and reporting |

**`hra_Designation`** (Org-owned). Primary column: `hra_Name`.
| Column | Type | Req | Notes |
|---|---|---|---|
| Name | Text(100) | R | For example "Senior Software Engineer" |
| Grade | Choice (L): L1–L8 | R | Used by expense limits later |
| Department | Lookup → hra_Department | | Leave blank if used across departments |

**`hra_Location`** (Org-owned). Primary column: `hra_Name`.
| Column | Type | Req | Notes |
|---|---|---|---|
| Name | Text(100) | R | For example "Bengaluru – HQ" |
| City | Text(50) | R | |
| State | Choice `hra_indianstate` | R | |
| Address | Multiline(500) | | |
| Holiday Calendar | Lookup → hra_HolidayCalendar | R* | *Added in Phase 2. Points to the current year's calendar, which the yearly rollover flow updates |
| Weekly Off Pattern | Choice (L): Sat+Sun, Sun only, Sun + 2nd & 4th Sat, Sun + alternate Sat | R | **New** (see section 2.3). Used by the leave day calculation |

**`hra_Employee`** (User-owned; owner is the reporting manager, see D2). Primary column: `hra_FullName`.
| Column | Type | Req | Notes |
|---|---|---|---|
| Employee Number | Autonumber `EMP-{SEQNUM:4}` | R | Alternate key 1 |
| First Name / Middle Name / Last Name | Text(50) each | R / – / R | |
| Full Name | Text(160) | R | Set by plugin from the name parts. Primary column |
| Work Email | Text(100), Email | R | Alternate key 2 |
| Personal Email | Text(100), Email | Rec | Used for offboarding and alumni communication |
| Mobile | Text(20), Phone | R | |
| Date of Birth | Date only | R | |
| Gender | Choice `hra_gender` | R | Drives which leave types apply (maternity/paternity) |
| Date of Joining | Date only | R | |
| Probation End Date | Date only | R | Defaults to joining date + `hra_ProbationMonths` (environment variable) |
| Confirmation Date | Date only | | |
| Employment Type | Choice `hra_employmenttype` | R | |
| Employment Status | Choice `hra_employmentstatus` | R | Default Active |
| Department | Lookup → hra_Department | R | |
| Designation | Lookup → hra_Designation | R | |
| Location | Lookup → hra_Location | R | |
| Reporting Manager | Lookup → hra_Employee | R* | Self-referential. *Not required for the CEO. Drives ownership and approvals |
| HR Business Partner | Lookup → hra_Employee | | |
| System User | Lookup → systemuser | | Set only for licensed users. Alternate key 3 (optional) |
| Portal Contact | Lookup → contact | | **New.** The employee's Power Pages identity |
| Leave Policy | Lookup → hra_LeavePolicy | R* | *Added in Phase 2. Defaults to the policy marked as default |
| Notice Period (days) | Whole number (0–180) | R | Default 30, or 90 for senior grades (business rule) |
| Last Working Day | Date only | | Set from the approved Separation |
| Years of Service | Formula, Decimal(1) | | `DateDiff(Date of Joining, Today(), Days) / 365.25`. If Dataverse formula columns don't support `Today()` in this environment, a nightly flow calculates it instead (to verify in Phase 1) |
| Gratuity Eligible | Formula, Yes/No | | Years of Service ≥ 5 |
| Is People Manager | Yes/No | | **New.** Kept up to date by plugin when someone is set as a reporting manager |
| 🔒 PAN | Text(10) | Rec | Format checked by plugin: `[A-Z]{5}[0-9]{4}[A-Z]` |
| 🔒 Aadhaar Last 4 | Text(4) | | Digits only. **The full Aadhaar number is never stored** |
| 🔒 UAN | Text(12) | | 12 digits |
| 🔒 ESIC Number | Text(17) | | Only for employees covered by ESIC |
| 🔒 Bank Account Number | Text(20) | Rec | |
| 🔒 IFSC | Text(11) | Rec | Format `[A-Z]{4}0[A-Z0-9]{6}` |
| 🔒 Bank Name | Text(100) | | |
| 🔒 Annual CTC | Currency (INR) | Rec | |

**`hra_EmergencyContact`** (User-owned, same owner as the employee). Primary column: Name.
| Column | Type | Req | Notes |
|---|---|---|---|
| Employee | Lookup → hra_Employee | R | Parental relationship (see section 2.2) |
| Name | Text(100) | R | |
| Relationship | Choice (L): Spouse, Parent, Sibling, Child, Friend, Other | R | |
| Phone | Text(20), Phone | R | |
| Is Primary | Yes/No | | |

**`hra_EmployeeDocument`** (User-owned). Primary column: Title.
| Column | Type | Req | Notes |
|---|---|---|---|
| Employee | Lookup → hra_Employee | R | Parental |
| Title | Text(100) | R | |
| Document Type | Choice `hra_documenttype` | R | |
| File | File (10 MB) | R | |
| Issue Date / Expiry Date | Date only | | A reminder flow runs 30 days before expiry |
| Verified | Yes/No | | |
| Verified By | Lookup → systemuser | | |

**`hra_EmploymentHistory`** (User-owned; created only by plugins and flows). Primary column: an autonumber.
| Column | Type | Req | Notes |
|---|---|---|---|
| Employee | Lookup → hra_Employee | R | Delete: Restrict |
| Change Type | Choice (L): Joining, Confirmation, Promotion, Transfer, Manager Change, Salary Revision, Separation | R | |
| Effective Date | Date only | R | |
| From/To Department, From/To Designation, From/To Location, From/To Manager | Lookups | | |
| 🔒 From CTC / To CTC | Currency | | |
| Remarks | Multiline(2000) | | |

**`hra_HRCase`** (User-owned; owner is the HR team). Primary column: Subject.
| Column | Type | Req | Notes |
|---|---|---|---|
| Case Number | Autonumber `HRC-{SEQNUM:5}` | R | |
| Employee | Lookup → hra_Employee | R | |
| Category | Choice (L): Payroll, Leave, Policy, Documents, IT Access, Other | R | |
| Subject | Text(200) | R | |
| Description | Multiline(4000) | R | |
| Priority | Choice (L): Low, Normal, High | R | Default Normal |
| Status Reason | New, In Progress, Waiting on Employee, Resolved, Closed | | Uses the built-in Status Reason column |
| Assigned To | Lookup → systemuser | | |
| Source | Choice (L): App, Portal, Teams Agent, Email | | |

#### Leave & Attendance

**`hra_HolidayCalendar`** (Org-owned).
| Column | Type | Req | Notes |
|---|---|---|---|
| Name | Text(100) | R | For example "Karnataka 2026" |
| Year | Whole number | R | |
| State | Choice `hra_indianstate` | R | Alternate key: State + Year |

**`hra_Holiday`** (Org-owned).
| Column | Type | Req | Notes |
|---|---|---|---|
| Holiday Calendar | Lookup → hra_HolidayCalendar | R | Parental |
| Name | Text(100) | R | |
| Date | Date only | R | Alternate key: Calendar + Date |
| Type | Choice (L): National, State, Optional/Restricted | R | Optional holidays aren't excluded automatically. Employees take them as RH leave |

**`hra_LeaveType`** (Org-owned).
| Column | Type | Req | Notes |
|---|---|---|---|
| Name | Text(100) | R | |
| Code | Text(5) | R | Alternate key. EL, CL, SL, ML, PTL, BL, CO, LOP, **RH** (new: restricted holiday) |
| Is Paid | Yes/No | R | |
| Allow Half Day | Yes/No | R | |
| Applicable Gender | Choice (L): All, Female, Male | R | |
| Max Consecutive Days | Whole number | | For example CL = 3 |
| Document Required After (days) | Whole number | | For example SL beyond 2 days needs a medical certificate |
| Is Encashable | Yes/No | | |
| Count Sandwiched Holidays | Yes/No | | If yes, weekends and holidays between leave days count as leave |
| Min Notice (days) | Whole number | | **New.** For example EL needs 7 days' notice. Shows a warning, doesn't block |

**`hra_LeavePolicy`** (Org-owned).
| Column | Type | Req | Notes |
|---|---|---|---|
| Name | Text(100) | R | |
| Applicable Employment Type | Choice `hra_employmenttype` | | |
| Is Default | Yes/No | | Only one can be the default (enforced by plugin) |

The leave year start month is the environment variable `hra_LeaveYearStartMonth`, not a column, so it's the same everywhere (change from `data-model.md`).

**`hra_LeavePolicyLine`** (Org-owned).
| Column | Type | Req | Notes |
|---|---|---|---|
| Leave Policy | Lookup | R | Parental |
| Leave Type | Lookup | R | Alternate key: Policy + Type |
| Annual Entitlement | Decimal(1) | R | |
| Accrual Frequency | Choice (L): Upfront, Monthly, Quarterly | R | |
| Max Carry Forward / Max Encashment / Max Balance | Decimal(1) | | Max Balance is **new**: a cap on accumulation |
| Available During Probation | Yes/No | R | |
| Prorate on Joining | Yes/No | R | **New.** Upfront entitlement is prorated for mid-year joiners |

**`hra_LeaveBalance`** (User-owned; created by the yearly or onboarding flow).
| Column | Type | Req | Notes |
|---|---|---|---|
| Employee, Leave Type | Lookups | R | |
| Leave Year | Text(7) | R | "2026-27". Alternate key: Employee + Type + Year |
| Opening, Accrued, Taken, Adjusted, Carried Forward, Encashed | Decimal(1) | | **Changed only by plugins** (D5). Read-only on forms |
| Pending Approval | Decimal(1) | | **New.** Days in submitted, not-yet-approved requests |
| Available | Formula, Decimal(1) | | Opening + Accrued + Carried Forward + Adjusted − Taken − Encashed − Pending Approval |

**`hra_LeaveRequest`** (User-owned; owner is the reporting manager).
| Column | Type | Req | Notes |
|---|---|---|---|
| Request Number | Autonumber `LR-{SEQNUM:6}` | R | |
| Employee, Leave Type | Lookups | R | |
| From Date / To Date | Date only | R | |
| First Day Half / Last Day Half | Yes/No | | |
| Number of Days | Decimal(1) | | Set by plugin. Read-only |
| Leave Year | Text(7) | | Set by plugin. **New** |
| Reason | Multiline(1000) | R | |
| Attachment | File (5 MB) | | Required by plugin when the Leave Type rule applies |
| Status | Choice `hra_approvalstatus` | R | |
| Approver | Lookup → hra_Employee | | Defaults to the reporting manager |
| Actioned On | Date and time | | |
| Approver Comments | Multiline(1000) | | |
| Cancellation Reason | Multiline(500) | | **New** |
| Source | Choice (L): App, Portal, Teams Agent | | **New** |

**`hra_LeaveAdjustment`** (**new table**, User-owned, HR only).
| Column | Type | Req | Notes |
|---|---|---|---|
| Employee, Leave Type | Lookups | R | |
| Leave Year | Text(7) | R | |
| Type | Choice (L): Comp-off Credit, Encashment, Correction, Carry Forward, Lapse | R | |
| Days | Decimal(1) | R | Positive or negative |
| Reason | Multiline(500) | R | |
| Related Attendance | Lookup → hra_AttendanceRecord | | Links a comp-off to the day that was worked |

**`hra_AttendanceRecord`** (User-owned).
| Column | Type | Req | Notes |
|---|---|---|---|
| Employee | Lookup | R | |
| Date | Date only | R | Alternate key: Employee + Date |
| Check In / Check Out | Date and time | | |
| Hours Worked | Formula, Decimal(2) | | |
| Status | Choice (L): Present, Work From Home, Half Day, Absent, On Leave, Holiday, Weekly Off | R | Leave and holiday rows are written by flows |
| Source | Choice (L): Manual, Device Import, Teams Shifts | | |

#### Recruitment, Onboarding & Separation

The tables are as listed in `data-model.md`, with these specifications added:

| Table | Column specifications and rules |
|---|---|
| `hra_JobRequisition` | Number: autonumber `REQ-{SEQNUM:4}`. Job Title: Text(150), R. Positions: Whole number (1–50), R. 🔒 Budget CTC Min/Max: Currency. Job Description: Multiline rich text (10000). Target Join Date: Date only. Approval Status: `hra_approvalstatus`. Status Reason: Open, On Hold, Filled, Cancelled. Publish on Careers Site: Yes/No. Hiring Manager is R. Delete: Restrict when applications exist |
| `hra_Candidate` | Full Name: Text(150), R. Email: Text(100), Email, R, alternate key. Phone: R. Experience: Decimal(1). 🔒 Current/Expected CTC: Currency. Resume: File (5 MB). **Consent Given (R, must be Yes) and Consent Date.** Retain Until: set to consent date + `hra_CandidateRetentionMonths`. Owner: Recruiter |
| `hra_JobApplication` | Candidate and Requisition are R. Alternate key: Candidate + Requisition. Business process flow `hra_RecruitmentProcess`: Screening → Interview → Offer → Hired. Rejection Reason: Choice (L) |
| `hra_Interview` | Start/End: Date and time, R. Teams Link: URL. Panel: N:N with `hra_Employee` (`hra_interview_panel`). Result: Choice (L) |
| `hra_InterviewFeedback` | Four scores, each Whole number 1–5, R. Overall Score: Formula (average). Recommendation: R. Alternate key: Interview + Interviewer |
| `hra_Offer` | 🔒 Offered CTC: R. Proposed Join Date: R. Offer Letter: File, generated from a Word template. Approval needed if Offered CTC is above the requisition budget maximum. On Accepted, the "Hired" flow runs (see section 4) |
| `hra_ChecklistTemplate` / `Item` | Due Offset: Whole number (−30 to +30) days from joining date or last working day. Sequence: Whole number |
| `hra_EmployeeChecklist` | Completion %: Rollup (Done ÷ total tasks). **New:** Separation lookup for offboarding checklists |
| `hra_ChecklistTask` | Owner: the **team** for the responsible group (HR, IT, Admin, Finance) or the manager's user. Due Date: R |
| `hra_Separation` | Resignation Date: R. Approved Last Working Day: set by the manager or HR. Notice Shortfall: Formula, (joining notice days) − (LWD − resignation date). Full and Final Status: Choice (L). On approval: sets Employee.Last Working Day and Status = On Notice, and creates the offboarding checklist |

#### Performance, Assets & Expenses

| Table | Column specifications and rules |
|---|---|
| `hra_AppraisalCycle` | Name: Text(50), R, alternate key. All due dates are Date only. Status: Choice (L). Opening a cycle creates a Performance Review for every Active employee who passed probation before the cycle start |
| `hra_PerformanceReview` | Alternate key: Cycle + Employee. Business process flow `hra_AppraisalProcess`: Self Review → Manager Review → Calibration → Closed. Ratings: `hra_rating`. 🔒 Increment %: Decimal(2). Final Rating is editable only at the Calibration stage (business rule plus role check) |
| `hra_Goal` | Weightage: Whole number (0–100). Plugin checks that the weightages of one review add up to 100 when the employee submits the self review |
| `hra_Asset` | Asset Tag: Text(30), R, alternate key. Serial Number: Text(50). Cost: Currency. Warranty End: Date only. Status Reason values as listed in `data-model.md` |
| `hra_AssetAssignment` | Only one open assignment per asset (plugin). Returning an asset sets the Asset status to Available. The offboarding checklist adds an "Return assets" task listing open assignments |
| `hra_ExpenseClaim` | Number: autonumber `EXP-{SEQNUM:5}`. Total Amount: Rollup of Expense Line amounts. Status: `hra_approvalstatus` plus Paid. **Lines can't be edited after the claim is submitted** (plugin) |
| `hra_ExpenseLine` | Amount: Currency, R (> 0). GST Amount: Currency. Receipt: File (5 MB), required above ₹500 (environment variable `hra_ReceiptRequiredAbove`) |

### 2.2 Relationship behaviour

| Parent → Child | Type | Delete | Assign | Share | Reparent |
|---|---|---|---|---|---|
| Employee → Emergency Contact, Employee Document | Parental | Cascade* | Cascade All | Cascade All | Cascade All |
| Employee → Leave Request, Leave Balance, Leave Adjustment, Attendance, Expense Claim, Performance Review, Goal, Asset Assignment, Separation, Employee Checklist | Referential, custom | **Restrict** | Cascade All | Cascade All | None |
| Employee → Employment History, HR Case | Referential | Restrict | None | None | None |
| Employee → Employee (Reporting Manager) | Referential | Remove Link | None | None | None |
| Expense Claim → Expense Line | Parental | Cascade | Cascade | Cascade | Cascade |
| Employee Checklist → Checklist Task | Referential, custom | Cascade | **None** (tasks are owned by teams) | None | None |
| Holiday Calendar → Holiday, Leave Policy → Policy Line, Checklist Template → Item, Interview → Feedback | Parental | Cascade | Cascade | Cascade | Cascade |
| Job Requisition → Application, Candidate → Application, Application → Interview / Offer | Referential | Restrict | None | None | None |
| Appraisal Cycle → Performance Review, Review → Goal | Referential / Parental | Restrict / Cascade | None / Cascade | None / Cascade | None / Cascade |
| Asset → Asset Assignment | Referential | Restrict | None | None | None |
| Reference lookups (Department, Designation, Location, Leave Type, Leave Policy) | Referential | Restrict | None | None | None |

*Parental relationships always cascade delete in Dataverse. That's acceptable here because employees are never deleted (D8), and the HR Admin role has no Delete privilege on Employee.

**Why "Cascade All" on assign matters:** when an employee's reporting manager changes, a plugin reassigns the Employee record to the new manager. The cascade then moves their leave, expenses and reviews too, so the new manager sees them immediately.

### 2.3 Proposed changes to `data-model.md` (need your OK)

1. **Add `hra_LeaveAdjustment`** so comp-off credits, encashment, corrections and carry-forward are recorded as transactions (D5).
2. **Add a Weekly Off Pattern column on Location.** Many Indian offices work 5½ or 6 days (for example 2nd and 4th Saturdays working), which changes the leave day count.
3. **Add a Restricted Holiday (RH) leave type** for optional holidays, typically 2 per year.
4. **Employee:** add Portal Contact and Is People Manager.
5. **Leave Request:** add Leave Year, Cancellation Reason and Source. **Leave Balance:** add Pending Approval. **Leave Type:** add Min Notice. **Policy Line:** add Max Balance and Prorate on Joining.
6. **Move Leave Year Start Month** from Leave Policy to an environment variable.
7. **Add a Separation lookup** on Employee Checklist.
8. **Defer to a later phase:** attendance regularisation requests, expense limits by grade, and loan/advance tracking. These aren't needed for go-live with 100 people.

With these changes there are **35 tables**.

---

## 3. Security model

### 3.1 Structure
- **One business unit.** At about 100 employees, more business units would only add admin work.
- **Teams:**
  - One owner team per function (HR, IT, Admin, Finance, Recruitment), used to own checklist tasks and HR cases.
  - One team per department, used for reporting and sharing.
  - Optionally, Microsoft Entra security group teams so membership is managed in Entra.
- **Manager hierarchy security**, depth 3. Dataverse's manager hierarchy uses the **Manager field on the user record** (`systemuser.parentsystemuserid`), not our custom lookup. Flow **F-01** keeps it in sync with `hra_Employee.Reporting Manager` for licensed users.
- **Ownership by reporting line (D2).** Combined with the manager hierarchy, managers see their direct reports through User-level access and their indirect reports through the hierarchy.

### 3.2 Role matrix

**Scopes:** **O** = Organization, **U** = User (own records plus team and hierarchy), **–** = none.
**Privileges:** C = Create, R = Read, W = Write, D = Delete, A = Append/Append To, S = Assign/Share.

| Table group | HR Admin | HR Manager | Recruiter | Line Manager | Employee (licensed) | IT/Asset Admin | Finance |
|---|---|---|---|---|---|---|---|
| Reference data (Department, Designation, Location, Leave Type and Policy, Holiday, Checklist Templates, Appraisal Cycle) | CRWDAS O | R O | R O | R O | R O | R O | R O |
| Employee | CRWAS O (no D) | CRWAS O | R O (no secured columns) | RW U | R U (own record via owner team) | R O | R O |
| Emergency Contact, Employee Document | CRWDAS O | CRWAS O | – | R U | CRW U | – | – |
| Employment History | R O (plugins write) | R O | – | R U | R U | – | – |
| Leave Request | CRWAS O | CRWAS O | – | RW U (approve) | CRW U | – | – |
| Leave Balance, Leave Adjustment | CRWAS O | CRWAS O | – | R U | R U | – | – |
| Attendance | CRWDAS O | CRWAS O | – | RW U | CRW U | – | – |
| Job Requisition | CRWAS O | CRWAS O | RWAS O | CRW U | – | – | – |
| Candidate, Application, Interview, Offer | CRWDAS O | CRWAS O | CRWAS O | R U (own requisitions and interviews) | – | – | – |
| Interview Feedback | CRWAS O | CRWAS O | R O | CRW U | – | – | – |
| Checklists and Tasks | CRWDAS O | CRWAS O | R O | RW U | R U | RW U (IT team tasks) | RW U (Finance team tasks) |
| Separation | CRWAS O | CRWAS O | – | RW U | CR U (resign) | – | R O |
| Appraisal, Performance Review, Goal | CRWAS O | CRWAS O | – | RW U | CRW U | – | – |
| Asset, Asset Assignment | R O | R O | – | R U | R U | CRWDAS O | R O |
| Expense Claim, Expense Line | CRWAS O | R O | – | RW U (approve) | CRW U | – | RW O (payment fields) |
| HR Case | CRWDAS O | CRWAS O | – | R U | CR U | – | – |

**Notes:**
- "Employee (licensed)" covers managers and HR as employees themselves. A person can hold several roles, for example Line Manager + Employee.
- Non-licensed employees use the portal (section 5.3) with web roles and table permissions instead of these roles.
- Approval is enforced by a plugin: only the record's Approver, or HR, can change Status to Approved or Rejected. Write access alone isn't enough.

### 3.3 Column security profiles
| Profile | Columns | Read | Update |
|---|---|---|---|
| `hra_HRSensitive` | Employee: PAN, Aadhaar Last 4, UAN, ESIC, bank details, Annual CTC. Employment History: From/To CTC | HR Admin, HR Manager | HR Admin, HR Manager |
| `hra_Compensation` | Candidate: Current/Expected CTC. Offer: Offered CTC. Requisition: Budget Min/Max. Performance Review: Increment % | HR Admin, HR Manager, Recruiter (except Increment %) | HR Admin, HR Manager |

### 3.4 Auditing and privacy
- Turn on auditing at the environment level and on Employee, Employment History, Leave Request, Leave Balance, Leave Adjustment, Offer, Separation and Performance Review, including every secured column.
- **DPDP Act:**
  - Candidate consent is required at capture (portal checkbox, stored with its date).
  - Retention is enforced by **F-30**.
  - Leavers' secured data is masked a set time after their last working day (environment variable `hra_LeaverRetentionMonths`, default 84 months, to cover typical statutory record-keeping; confirm with your legal or finance team).
- **Sample data** is always synthetic. No real PAN, Aadhaar or bank numbers are ever loaded into Dev.

---

## 4. Automation catalogue

### 4.1 Plugins (C#, synchronous unless noted)
| ID | Table / message | Stage | Logic |
|---|---|---|---|
| P-01 | Employee Create/Update | Pre-op | Builds Full Name. Validates PAN and IFSC formats. Defaults Probation End Date, Notice Period and Leave Policy |
| P-02 | Employee Create/Update (Reporting Manager, System User) | Post-op | Assigns the record to the reporting manager's user (or the HR team if the manager isn't licensed). Updates Is People Manager. Writes Employment History on a manager, department, designation, location or CTC change |
| P-03 | Leave Request Create/Update | Pre-op | Calculates Number of Days: excludes the location's weekly offs and that state's holidays unless the leave type counts sandwiched days, and handles half days. Sets Leave Year. Blocks overlapping requests, gender-ineligible types, probation-ineligible types, insufficient balance (LOP is exempt), more than Max Consecutive Days, and a missing attachment when required |
| P-04 | Leave Request status change | Post-op | Updates Leave Balance: Submitted adds to Pending; Approved moves Pending to Taken; Rejected or Withdrawn releases Pending; cancelling after approval reverses Taken. Only the Approver or HR may approve or reject |
| P-05 | Leave Adjustment Create | Post-op | Applies the adjustment to the matching Leave Balance, creating the balance row if it doesn't exist |
| P-06 | Asset Assignment Create/Update | Pre/Post | One open assignment per asset. Keeps the Asset status in sync |
| P-07 | Expense Claim/Line | Pre-op | Locks lines after submission. Enforces the receipt rule. Only the Approver or Finance can change status |
| P-08 | Goal / Performance Review | Pre-op | Goal weightages must total 100 at self-review submission. Final Rating editable only at Calibration |
| P-09 | Leave Policy | Pre-op | Only one default policy |

Plugins are built in one project (`src/plugins/HRAutomation.Plugins`), registered with the solution, and have unit tests (FakeXrmEasy) for P-03 and P-04 at minimum.

### 4.2 Power Automate cloud flows
| ID | Name | Trigger | Logic |
|---|---|---|---|
| F-01 | Sync manager hierarchy | Employee: Reporting Manager or System User changed | Sets `systemuser.parentsystemuserid` for licensed users |
| F-02 | New employee setup | Employee created | Creates Leave Balances for the current leave year (prorated), creates the onboarding checklist from the default template, notifies IT and Admin, and sends a welcome email on the joining date |
| F-10 | Leave approval | Leave Request: Status = Submitted | Starts an approval to the Approver (Outlook and Teams). Escalates to HR after `hra_LeaveEscalationDays` (2) working days. Writes back Status, Actioned On and comments. Notifies the employee |
| F-11 | Monthly leave accrual | Recurrence: 1st of each month, 02:00 IST | Adds monthly or quarterly accruals per policy line, respecting Max Balance and probation rules |
| F-12 | Year-end rollover | Recurrence: 1 April, 01:00 IST | Creates new-year balances. Carries forward up to the maximum. Records lapsed days as Leave Adjustments. Moves each Location to the next holiday calendar |
| F-13 | Attendance from approved leave and holidays | Leave approved / daily | Writes On Leave and Holiday attendance rows |
| F-14 | Daily leave digest | Recurrence: weekdays, 09:00 IST | Sends each manager a Teams message listing who's on leave today |
| F-20 | Requisition approval | Requisition submitted | Approval chain: Hiring Manager's manager, then HR Manager |
| F-21 | Offer approval and letter | Offer submitted | Approval if above budget. Generates the offer letter from a Word template into the File column |
| F-22 | Candidate hired | Offer: Status = Accepted | Marks the application as Hired, creates the Employee record (status Active, joining date from the offer), which then runs F-02, and closes the requisition when all positions are filled |
| F-23 | Interview scheduling notice | Interview created or updated | Sends Teams/Outlook invitations to the panel and reminds them to submit feedback |
| F-24 | Separation approved | Separation: Approved | Updates the Employee (On Notice, LWD). Creates the offboarding checklist and the asset return task. Notifies IT to disable access on the LWD |
| F-25 | Last working day | Daily, 18:00 IST | For employees whose LWD is today: Employment Status = Separated, calculates leave encashment as a Leave Adjustment, and notifies Finance for full and final |
| F-26 | Probation and contract reminders | Daily | Notifies manager and HR 15 days before probation end or contract end |
| F-27 | Document expiry reminder | Daily | 30 days before a document's Expiry Date |
| F-30 | Candidate data retention | Weekly | Deactivates candidates past Retain Until, then deletes them or removes personal data, depending on Q8 |
| F-40 | Appraisal cycle kick-off | Appraisal Cycle: Status = Goal Setting | Creates Performance Reviews and notifies employees and managers |
| F-41 | Appraisal reminders | Daily during a cycle | Reminders before each due date |
| F-50 | Expense approval | Expense Claim submitted | Approval to the manager, then Finance. Paid status is set by Finance |
| F-60 | HR case routing | HR Case created | Assigns by category, acknowledges the employee, and escalates after 3 days without action |

All flows live in the solution, use connection references, run as the service account (Q6) where possible, and have the HR Hub app as their context (see section 7.1).

### 4.3 Business rules and client scripts
- **Business rules:**
  - Notice Period default by grade
  - Show or hide ESIC Number
  - Require Rejection Reason when an application is rejected
  - Lock Leave Request fields after submission
  - Lock Final Rating outside Calibration
- **TypeScript form scripts** (`src/webresources`):
  - Leave Request: shows the available balance as you type dates, and a half-day toggle
  - Employee: masks secured fields in the header
  - Interview: "Join Teams" button
  - Expense Line: GST hint

---

## 5. App design

### 5.1 "HR Hub" model-driven app (HR, Recruiter, IT, Finance)
| Sitemap area | Groups and subareas |
|---|---|
| **Home** | HR Dashboard, My Work (tasks and approvals assigned to me) |
| **People** | Employees, Departments, Designations, Locations, Documents, Employment History, HR Cases |
| **Leave & Attendance** | Leave Requests, Leave Balances, Leave Adjustments, Attendance, Holiday Calendars |
| **Recruitment** | Requisitions, Candidates, Applications, Interviews, Offers |
| **Onboarding & Exit** | Checklists, Checklist Tasks, Separations |
| **Performance** | Appraisal Cycles, Performance Reviews, Goals |
| **Assets & Expenses** | Assets, Asset Assignments, Expense Claims |
| **Settings** (HR Admin only) | Leave Types, Leave Policies, Checklist Templates |

### 5.2 "Manager Self-Service" model-driven app (Line Managers, licensed staff)
Areas:
- **My Team:** Employees with My Team / My Indirect Team views
- **Approvals:** Leave, Expenses and Requisitions pending me
- **Team Leave Calendar**
- **Hiring:** my requisitions and interviews, plus a feedback form
- **Performance:** my team's reviews
- **Me:** my profile, leave, expenses and goals

### 5.3 Self-service for non-licensed employees (Phase 6, depending on Q1)
- **Power Pages: Employee Portal**
  - Sign-in: Microsoft Entra ID, matched to `hra_Employee.Portal Contact`
  - Pages: My Profile (limited edits), Apply / My Leave, Leave Balance, Holidays, My Expenses, My Goals, My Documents, Raise HR Case, Resign
  - Security: table permissions scoped by the Contact relationship
- **Power Pages: Careers (public)** lists open requisitions with Publish on Careers Site = Yes. The application form creates a Candidate and an Application, with the DPDP consent checkbox, a CAPTCHA and a CV upload.
- **Copilot Studio agent "HR Assistant" in Teams** can show my leave balance, apply for leave, explain holidays, answer HR policy questions from the SharePoint site, and raise an HR case.

### 5.4 Forms, views and dashboards (standards)
- **Main forms** use tabs. Employee: Summary, Job, Personal, Statutory & Bank (secured), Leave, Assets, Documents, History.
- **Quick view forms** for Employee (photo, designation, manager) are reused on every transaction form.
- **Views on every transaction table:** Active, My, My Team, Pending My Approval, plus status-specific views.
- **Dashboards:**
  - HR Overview: headcount by department, joiners and leavers this month, open requisitions, pending approvals
  - Leave: on leave today, utilisation by type
  - Recruitment pipeline (funnel)
- **Power BI** (Phase 5): headcount trend, attrition %, leave utilisation, time-to-hire, cost per hire, expense spend.

---

## 6. ALM

### 6.1 Repository layout
```
/src/solution/            unpacked hrautomation solution (pac solution clone/sync)
/src/plugins/             C# plugin project + tests
/src/webresources/        TypeScript sources -> compiled JS
/src/powerbi/             Power BI project (.pbip)
/data/sample/             synthetic CSVs + import script (Configuration Migration format)
/deploy/settings/         deploymentsettings.prod.json (env vars + connection refs)
/.github/workflows/       export-dev.yml, build-deploy.yml
/docs/                    architecture, data model, guides
```

### 6.2 Environments and pipeline
1. **Dev** (`viipiin.crm.dynamics.com`, Developer): all changes are made here, inside `hrautomation` only.
2. **Export workflow** (manual run): `pac solution export` (unmanaged + managed) → `pac solution unpack` → pull request.
3. **Build and deploy workflow** (on merge to `master`):
   - build plugins and web resources
   - `pac solution pack --managed`
   - `pac solution check`, which fails on Critical or High findings
   - import to **Test** (once it exists), then to **Viipiin-Prod** after manual approval, using `deploymentsettings.prod.json`
4. **Authentication:** an app registration (service principal) is added as an application user in each environment, with its secrets stored in GitHub Actions secrets.
5. **Strongly recommended:** create a **Test sandbox** before go-live. Importing straight from a Developer environment into Production leaves no place to test with Production-like licences and data.

### 6.3 Environment variables
| Schema name | Type | Dev value | Notes |
|---|---|---|---|
| `hra_LeaveEscalationDays` | Number | 2 | Working days before HR escalation |
| `hra_LeaveYearStartMonth` | Number | 4 | April |
| `hra_ProbationMonths` | Number | 6 | |
| `hra_CandidateRetentionMonths` | Number | 12 | DPDP retention for unsuccessful candidates |
| `hra_LeaverRetentionMonths` | Number | 84 | Confirm with legal |
| `hra_ReceiptRequiredAbove` | Number | 500 | INR |
| `hra_HRTeamEmail` | Text | (shared mailbox) | |
| `hra_ITTeamEmail` / `hra_FinanceTeamEmail` / `hra_AdminTeamEmail` | Text | | |
| `hra_HRHubAppUrl` | Text | (Dev app URL) | Used for links in emails |
| `hra_CareersSiteUrl` | Text | | Phase 6 |
| `hra_HRPolicySiteUrl` | Text | | SharePoint site for policies (Phase 6) |

### 6.4 Connection references
| Schema name | Connector | Used by |
|---|---|---|
| `hra_Dataverse` | Microsoft Dataverse | All flows |
| `hra_Office365Outlook` | Office 365 Outlook | Notifications (sent from the HR shared mailbox) |
| `hra_Teams` | Microsoft Teams | Approvals cards, digests |
| `hra_Approvals` | Approvals | F-10, F-20, F-21, F-50 |
| `hra_Office365Users` | Office 365 Users | Manager and profile lookups |
| `hra_WordOnlineBusiness` | Word Online (Business) | Offer letters (F-21) |
| `hra_SharePoint` | SharePoint | Document storage, if chosen (Q9) |

---

## 7. Licensing, risks and open questions

### 7.1 Licensing notes (confirm with your Microsoft partner or admin)
- **Users of the model-driven apps** (HR, managers, recruiters, IT, Finance) each need a **Power Apps Premium** licence or a per-app licence in Viipiin-Prod.
- **Flows:** flows that are associated with the HR Hub app run under the app's licence rights. Flows that run outside any app context, or use premium connectors standalone, may need Power Automate Premium or a Process licence.
- **Dev:** the Developer environment is free for building. Users there need the Power Apps Developer Plan, and the environment isn't for production use.
- **Power Pages** is licensed by monthly active authenticated and anonymous users. **Copilot Studio** is licensed by messages or capacity. **Power BI** needs Pro or Premium Per User for everyone who views reports, unless the reports are embedded in the app with another licensing model. These are **not confirmed** in Prod, so they aren't built until you confirm.
- **Dataverse capacity:** files (resumes, documents) use file capacity. That's small for 100 employees, but worth watching.

### 7.2 Risks
| # | Risk | Impact | Mitigation |
|---|---|---|---|
| R1 | Licence cost if every employee needs Power Apps Premium | High | D3: portal and Teams agent for self-service. Decide Q1 before Phase 1 security is built |
| R2 | Dev region is North America with USD base currency, and Prod's region and currency may differ | Medium | Check both (Q4). Use INR as transaction currency. Report in INR |
| R3 | No Test environment | Medium | Create a sandbox before the first Prod import |
| R4 | Developer environment can be cleaned up after long inactivity | Low | Solution is in Git after every phase. Rebuild with `pac solution import` |
| R5 | Leave rules differ from company policy (sandwich, accrual, carry-forward) | Medium | Everything is configurable per Leave Type and Policy Line. HR signs off rules before Phase 2 |
| R6 | Personal data in Dev during testing | High | Synthetic data only. Never copy Prod data to Dev |
| R7 | Flows sending email from a personal account | Medium | Service account and shared mailbox (Q6) |
| R8 | Managers who aren't licensed users (owner model D2) | Medium | Records owned by the HR team instead. Approvals still go through Outlook/Teams |

---

## 8. Phase plan (after sign-off)

| Phase | Builds |
|---|---|
| 1 Core HR | Global choices. Department, Designation, Location, Employee, Emergency Contact, Employee Document, Employment History, HR Case. P-01, P-02, F-01. Roles and column security. HR Hub app (People area). 50 synthetic employees |
| 2 Leave | Holiday Calendar, Holiday, Leave Type, Leave Policy and Lines, Balance, Adjustment, Request, Attendance. P-03, P-04, P-05, P-09. F-02 (leave part), F-10 to F-14. Indian holiday seed data for 2026 and 2027 (states as per Q3) |
| 3 Recruitment and Onboarding | Recruitment tables, checklists, Separation, the BPF. F-02 (checklist part), F-20 to F-27, F-30 |
| 4 Performance, Assets, Expenses | Appraisal tables, Assets, Expenses, the BPF. P-06 to P-08. F-40, F-41, F-50, F-60. Manager Self-Service app |
| 5 Analytics | Dashboards, Power BI |
| 6 Self-service | Power Pages (Employee and Careers), Copilot Studio agent, after licence confirmation |
| 7 ALM and go-live | Pipelines, Test sandbox, Prod deployment, user guides |

---

## 9. Questions for you (answers needed before Phase 1)

| # | Question | Default if you don't answer |
|---|---|---|
| **Q1** | **How will non-HR employees use the system?** (a) Everyone gets Power Apps Premium and uses the apps. (b) **Recommended:** only HR, managers, recruiters, IT and Finance are licensed, and everyone else uses the Power Pages portal and/or a Teams agent. (c) Self-service later. HR enters leave on employees' behalf for now | (b) |
| Q2 | Roughly how many people managers and HR/recruiter/IT/Finance users are there? | About 20 |
| Q3 | Which states are your offices in? (needed for holiday calendars and weekly-off patterns) | Karnataka only |
| Q4 | What region and base currency is **Viipiin-Prod** in? (Power Platform admin centre → Environments → Viipiin-Prod) | Assume INR isn't the base currency |
| Q5 | Leave policy numbers: EL, CL and SL entitlement per year, accrual (monthly or upfront), carry-forward and encashment limits, and sandwich rule | EL 18 (monthly 1.5, carry forward 30, encash 15), CL 8 (upfront, lapses), SL 8 (upfront, lapses), RH 2, ML 26 weeks, PTL 5 days, BL 3 days, no sandwich rule |
| Q6 | Is there a service account and an HR shared mailbox (for example hr@yourcompany) for flows and notifications? | Create both |
| Q7 | Do you use Microsoft Teams? (for approvals, digests and the agent) | Yes |
| Q8 | Unsuccessful candidates after the retention period: delete the record, or anonymise it and keep it for statistics? | Anonymise |
| Q9 | Store employee documents in Dataverse file columns, or in SharePoint? | Dataverse (simpler, and secured by roles) |
| Q10 | Does attendance come from a biometric device or system that we should import from? | Manual or Teams only in v1 |

Once you've answered (even just "go with defaults"), the next step is to update `data-model.md` with section 2.3 and start **Phase 1**.
