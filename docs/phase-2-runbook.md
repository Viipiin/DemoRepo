# Phase 2 Runbook: Leave & Attendance

Run everything from the repo folder in the VS Code terminal (PowerShell).

**What Phase 2 adds to Viipiin-Dev:**

| Area | Details |
|---|---|
| Tables (`model/tables/09`–`17`) | Holiday Calendar, Holiday, Leave Type, Leave Policy, Leave Policy Line, Leave Balance, Leave Adjustment, Leave Request, Attendance Record. Also Leave Policy on Employee and Holiday Calendar on Location |
| Views | Pending My Approval, My Leave Requests, My Team's Leave, My Leave Balances, My Team's Leave Balances, My Attendance |
| Plugins (`model/plugins.json`) | See the list below |
| Custom APIs (`model/custom-apis.json`) | `hra_InitializeLeaveBalances` and `hra_RunLeaveAccrual` |
| Sample data (`model/sample-data.json`) | 9 leave types, Standard India Policy, Karnataka 2026 and 2027 calendars, and the policy given to all 50 employees |

**The plugins:**
- **Leave request checks:** count days (skipping weekly offs and Karnataka holidays, handling half days), set the leave year, check balance, overlaps, gender, probation, policy and maximum length, and make sure only the approver or HR can approve.
- **Balance updates:** keep Pending and Taken in sync with the request status, and share the request with the employee.
- **Leave adjustments:** apply comp-off credits, encashment, corrections, carry forward and lapse to the balance.
- **Leave balance:** recalculate Available whenever a balance changes.
- **Attendance:** calculate hours worked from check-in and check-out.
- **New employees:** get the default leave policy and their balances automatically.

**Standard India Policy** (all in `model/sample-data.json`, so you can edit it):

| Code | Leave | Entitlement | Notes |
|---|---|---|---|
| EL | Earned | 18/year, **1.5 per month** | Carry forward 30, encash 15, max balance 45. Not during probation |
| CL | Casual | 8, upfront | Prorated for joiners. Max 3 days at a time |
| SL | Sick | 8, upfront | Prorated for joiners |
| RH | Restricted Holiday | 2, upfront | One day at a time |
| ML | Maternity | 182 days (26 weeks) | Female only. Counts calendar days |
| PTL | Paternity | 5 | Male only |
| BL | Bereavement | 3 | |
| CO | Comp-off | 0 | Credited by Leave Adjustment |
| LOP | Loss of Pay | No balance | Unpaid |

> **Holidays:** the sample calendars contain only fixed-date holidays (Republic Day, May Day, Independence Day, Gandhi Jayanti, Kannada Rajyotsava, Christmas, and New Year as optional). Add festival holidays (Ugadi, Ganesh Chaturthi, Ayudha Pooja, Deepavali and others) from your company's official Karnataka list, either in the app or in `sample-data.json`.

---

## Step 1: Run the automated steps

```powershell
git checkout master
git pull
./scripts/run-phase2.ps1
```

| # | Step | What you should see |
|---|---|---|
| 1 | `validate` | `17 tables ... 13 plugin steps` and `✔ Model is valid.` |
| 2 | `provision` | 9 new tables, columns and relationships created. Forms, views and roles updated. It should end with `✔ Done.` The quick-find warnings are harmless |
| 3 | `build-plugins` | `Passed! ... Passed: 55` |
| 4 | `register-plugins` | 13 steps and 2 Custom APIs (with parameters) registered |
| 5 | `seed` | Phase 1 data shows "exists". New: 2 calendars, 14 holidays, 9 leave types, 1 policy, 8 policy lines, and 50 employees updated with the policy |
| 6 | `init-balances` | Each employee gets 5–6 balances (gender decides ML or PTL) |
| 7 | Accrual for 2026-04 to 2026-10 | Each month: `Updated:` up to 50, one EL balance per employee. It's fewer in early months, because people who joined later don't accrue for months before they joined |

Each step can also be run on its own, for example `./scripts/hra.ps1 init-balances` or `./scripts/hra.ps1 run-api hra_RunLeaveAccrual --param Period=2026-11`. Re-running is safe: accrual runs only once per balance per month.

## Step 2: Add the leave pages to HR Hub (manual, about 3 minutes)

1. In **make.powerapps.com**, go to **Solutions**, then **HR Automation**, then **Apps**, then **HR Hub**, then **Edit**.
2. Use **Add page**, then **Dataverse table**, and add the 9 new tables.
3. Group them in **Navigation**:
   - **Leave:** Leave Requests, Leave Balances, Leave Adjustments, Attendance Records
   - **Leave Settings:** Leave Types, Leave Policies, Leave Policy Lines, Holiday Calendars, Holidays
4. Click **Save**, then **Publish**.

**Optional:** add the **Attachment** file column to the Leave Request form (like the File column on Employee Document in Phase 1).

## Step 3: Schedule monthly accrual (manual, about 3 minutes)

1. In **make.powerapps.com**, go to **Solutions**, then **HR Automation**, then **New**, then **Automation**, then **Cloud flow**, then **Scheduled**.
2. Name the flow `HRA - Monthly Leave Accrual`. Set it to repeat every **1 Month**, starting on the 1st of next month at **02:00**, time zone **(UTC+05:30) Chennai, Kolkata, Mumbai, New Delhi**.
3. Add a step: **Microsoft Dataverse**, then **Perform an unbound action**, then **Action name** `hra_RunLeaveAccrual`. Leave **Period** empty, which means the current month.
4. Click **Save**. This flow is part of the solution, so it's exported with it.

## Step 4: Check it works

You're linked to **Nikhil Sharma (Head of HR)**, so the 3 HR Executives report to you.

| Check | Expected |
|---|---|
| **Leave Balances**, then **My Team's Leave Balances** | Balances for your team. **EL Available = 10.5** (7 months × 1.5). CL 8 and SL 8, prorated for anyone who joined this leave year |
| An HR Executive on probation | EL shows accrual, but applying for EL is blocked with "isn't available during probation" |
| New **Leave Request**: one of your HR Executives, CL, Mon 5 Oct to Wed 7 Oct 2026 | **Number of Days = 3**, Leave Year 2026-27, Approver = Nikhil Sharma, Status Submitted. Their CL balance shows Pending 3, Available 5 |
| Request **Thu 1 Oct to Mon 5 Oct 2026** (CL) | **2 days** (Thu and Mon): Gandhi Jayanti (Fri 2 Oct) and the weekend are skipped |
| CL for 4 working days | Error: "can be at most 3 day(s) at a time" |
| **Pending My Approval**, open a request, set Status to **Approved** | Balance moves from Pending to **Taken**. Actioned On is filled |
| Set an approved request to **Cancelled** | Taken goes back down |
| A second request overlapping the first | Error: "overlap another submitted or approved leave request" |
| **Leave Adjustment**: Comp-off Credit, 1 day, type CO | That employee's CO balance shows **Available 1** |
| **Attendance Record** with check-in 09:30 and check-out 18:00 | **Hours Worked = 8.5** |
| Create a new employee | They get the Standard India Policy and their balances automatically |

## Step 5: Save to Git

```powershell
./scripts/export-solution.ps1
git checkout -b phase2-export
git add src/solution
git commit -m "Phase 2: export HRAutomation solution (Leave & Attendance)"
git push -u origin phase2-export
```
Then open and merge a PR from `phase2-export`.

## Coming next (Phase 2b)

These need Power Automate connections, so they come once the core is confirmed working:
- **Approval notifications:** Outlook or Teams approvals to the manager, with escalation to HR after `hra_LeaveEscalationDays`.
- **Year-end rollover (1 April):** carry forward up to the limit, record lapses, and create next year's balances. This uses the same Custom API approach.
