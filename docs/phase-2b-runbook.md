# Phase 2b Runbook: Leave Approvals, Notifications and Year-End

Run everything from the repo folder in the VS Code terminal (PowerShell).

**What Phase 2b adds (all defined in JSON under `model/`):**

| Item | File | What it does |
|---|---|---|
| **HRA - Leave Approval** (flow) | `flows/01-leave-approval.json` | When a request is **Submitted**, sends an Approvals card (Teams and Outlook) to the approver's user. If the approver has no Dataverse user, it goes to the HR mailbox. **Approve** or **Reject** updates the request, and the plugins move the balance. If the request was already decided in HR Hub, the flow leaves it alone. If nobody responds within **2 days**, the HR mailbox gets an escalation email |
| **HRA - Leave Decision Notification** (flow) | `flows/02-leave-decision-notification.json` | Emails the employee when their request is **Approved** or **Rejected**, by the flow or in HR Hub |
| **HRA - Monthly Leave Accrual** (flow) | `flows/03-monthly-leave-accrual.json` | 1st of every month, 02:00 IST: runs `hra_RunLeaveAccrual`. This replaces the manual flow from Phase 2 |
| **HRA - Year-End Leave Rollover** (flow) | `flows/04-year-end-leave-rollover.json` | 1 April, 01:00 IST: runs `hra_RunYearEndRollover` for each active employee |
| `hra_RunYearEndRollover` (Custom API) | `custom-apis.json` | Closes a leave year. See the details below |
| `hra_HRTeamEmail` (environment variable) | `environment-variables.json` | The HR mailbox. Set to `admin@them365dev.onmicrosoft.com` in Dev. Use a shared mailbox in Prod |
| 3 connection references | `connection-references.json` | `hra_Dataverse`, `hra_Approvals`, `hra_Office365Outlook` |

**How the year-end rollover works for each balance of the closing year:**
- It carries forward up to the policy line's **Max Carry Forward** (EL: 30) into next year's balance, and records the rest as a **Lapse**. Both are written as Leave Adjustments, so there's an audit trail.
- Leave types with no carry forward (CL, SL, RH and the others) lapse in full.
- It creates next year's opening balances, then marks the old balance **Rolled Over**, so re-running it is safe.
- **Encashment stays a manual HR decision:** add an Encashment adjustment before the rollover if needed.

> **Approvals email addresses:** approvals and notifications go to users' real Microsoft 365 addresses. In this Dev tenant only your user is real, and it's linked to Nikhil Sharma (Head of HR). Requests from his 3 HR Executives come to **you**. Requests whose approver has no user also go to you, through `hra_HRTeamEmail`. Notification emails to sample employees go to their fake `@example.com` work emails and simply bounce, except Nikhil's, which comes to you.

---

## Step 1: Run the automated steps

```powershell
git checkout master
git pull
./scripts/run-phase2b.ps1
```

| Step | What you should see |
|---|---|
| `validate` | `3 custom APIs, 4 flows` and `✔ Model is valid.` |
| `build-plugins` | `Passed! ... Passed: 62` |
| `provision` | `hra_HRTeamEmail` created. The **Rolled Over** column added to Leave Balance. 3 connection references created. 4 flows created **(off)** |
| `register-plugins` | `hra_RunYearEndRollover` created with 2 parameters and 1 response |

## Step 2: Link the connections and turn the flows on (manual, about 5 minutes)

Connections need your sign-in, so this can't be scripted.

1. In **make.powerapps.com** (Viipiin-Dev), go to **Solutions**, then **HR Automation**, then **Connection References** in the left menu.
2. For each of the three references, open it, choose **+ New connection** (or an existing one), sign in as `admin@them365dev.onmicrosoft.com`, and click **Save**:
   - **HRA Dataverse:** a Microsoft Dataverse connection
   - **HRA Approvals:** an Approvals connection
   - **HRA Office 365 Outlook:** an Office 365 Outlook connection
3. Go to **Cloud flows** in the solution. For each of the four **HRA - …** flows, select it and click **Turn on**.
   - If a flow won't turn on, open it in the designer. The step with a problem shows a red message. **Paste the message to Claude.** The flow definitions are hand-written JSON that hasn't been tested against your environment yet, so a first-run fix is possible.
4. If you created a monthly accrual flow by hand in Phase 2, delete it.

## Step 3: Check it works

| Check | Expected |
|---|---|
| Create a Leave Request for one of **your** HR Executives (CL, 2 days, Submitted) | Within a minute, an approval appears in **Teams → Approvals** and in your Outlook |
| Click **Approve** with a comment | The request goes to **Approved**, with your comment in Approver Comments. The CL balance moves from Pending to Taken. You get the **decision email** (as Nikhil's approval request was routed to you; the employee's email bounces) |
| Create a request for an **Engineer**, whose manager has no user | The approval goes to the HR mailbox, `hra_HRTeamEmail`, which is you |
| Create a request, then approve it **in HR Hub** before answering the card | The request stays Approved, and the flow doesn't change it. Then cancel the card in Teams |
| Run the accrual flow: open **HRA - Monthly Leave Accrual** and click **Run** | It succeeds. The balances' **Last Accrual Period** is the current month (no double accrual if it already ran) |
| Try the year-end rollover on the sample year (run it from VS Code, see below) | EL: up to 30 carried into 2027-28, and the rest lapses. CL/SL/RH lapse. New 2027-28 balances exist. Old balances show **Rolled Over = Yes** |

**Trying the rollover now (optional):** it closes 2026-27, which is still the current year in this Dev environment. Only do this if you're fine with the sample balances moving to 2027-28:
```powershell
./scripts/hra.ps1 rollover --param FromLeaveYear=2026-27
```
Re-running it does nothing more, because balances already rolled over are skipped.

## Step 4: Save to Git

```powershell
./scripts/export-solution.ps1
git checkout -b phase2b-export
git add src/solution
git commit -m "Phase 2b: export HRAutomation solution (leave flows)"
git push -u origin phase2b-export
```
Then open and merge a PR from `phase2b-export`. The flows are now in the exported solution too.

## Changing the flows later

Edit the JSON in `model/flows/`, then **turn the flow off**, run `./scripts/hra.ps1 provision`, and turn it on again. The escalation time is the approval step's `"limit": { "timeout": "P2D" }` (an ISO 8601 duration, so P3D means 3 days).
