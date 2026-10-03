# Phase 1 Runbook: Core HR

Run everything from the repo folder in the VS Code terminal (PowerShell), for example `D:\Claude Projects\DemoRepo`.

**What Phase 1 builds in Viipiin-Dev (solution `HRAutomation`):**
- 8 global choices
- 8 tables: Department, Designation, Location, Employee, Emergency Contact, Employee Document, Employment History, HR Case
- Their columns, relationships, alternate keys, main forms, views and quick find
- 6 environment variables
- 5 owner teams: HR, IT, Admin, Finance, Recruitment
- 7 security roles
- The "HR Sensitive Data" column security profile
- Auditing and INR currency
- 2 plugins: P-01 and P-02
- 50 synthetic employees

---

## Step 0: Get the latest code

```powershell
git checkout master
git pull
```

If PowerShell refuses to run scripts ("running scripts is disabled on this system"), allow local scripts once for your user:

```powershell
Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
```

## Step 1: Run the automated steps

```powershell
./scripts/run-phase1.ps1
```

This runs five things in order. If anything fails, you can run each one separately (see the table below).

| # | What it does | Command to run it alone | What you should see |
|---|---|---|---|
| 1 | Builds the plugins and runs 19 unit tests | `./scripts/build-plugins.ps1` | `Passed! ... Passed: 19` |
| 2 | Connects and checks the solution | `./scripts/hra.ps1 check` | A browser opens for sign-in the first time, then `✔ Solution HRAutomation ... found` |
| 3 | Creates choices, tables, columns, relationships, keys, forms, views, roles, teams, column security, environment variables, auditing and INR | `./scripts/hra.ps1 provision` | A long list of `✔ ... created`, ending with `✔ Published` and `✔ Done.` Takes about 5–15 minutes the first time |
| 4 | Uploads the plugin assembly and registers 4 steps | `./scripts/hra.ps1 register-plugins` | `✔ HRAutomation.Plugins 1.0.0.0 registered` and 4 `Step ... registered` lines |
| 5 | Loads sample data | `./scripts/hra.ps1 seed` | 7 departments, 12 designations, Bengaluru - HQ, 50 employees, then `Your user is linked to the Head of Human Resources employee record` |

**Notes:**
- Every command is safe to re-run. Existing items are skipped or updated.
- The sign-in token is cached in `.hra-token-cache/`, which isn't committed to Git. To sign in as a different account, delete that folder.
- **If a command fails, paste the full output back to Claude.** For extra detail, run `$env:HRA_VERBOSE = "1"` first, then run the command again.

## Step 2: Turn on manager hierarchy security (manual, 1 minute)

Ownership follows the reporting line (plugin P-02). This setting lets a manager also see the people below their direct reports.

1. Open the [Power Platform admin center](https://admin.powerplatform.microsoft.com) and go to **Environments**, then **Viipiin-Dev**, then **Settings**.
2. Go to **Users + permissions**, then **Hierarchy security**.
3. Turn on **Hierarchy Modeling**, select **Manager Hierarchy**, set **Depth** to `3`, and click **Save**.

## Step 3: Create the "HR Hub" app (manual, about 5 minutes)

1. Open [make.powerapps.com](https://make.powerapps.com) and make sure **Viipiin-Dev** is selected (top right).
2. Go to **Solutions**, then **HR Automation**, then **New**, then **App**, then **Model-driven app**.
3. Enter the name `HR Hub` and click **Create**.
4. In the app designer, use **Add page**, then **Dataverse table**, and add these tables:
   - Employees, Departments, Designations, Locations
   - Emergency Contacts, Employee Documents, Employment History
   - HR Cases
5. In **Navigation**, group them:
   - **People:** Employees, Departments, Designations, Locations
   - **Employee Records:** Emergency Contacts, Employee Documents, Employment History
   - **Helpdesk:** HR Cases
6. Click **Save**, then **Publish**, then **Play** to open the app.

## Step 4: Add the file upload to Employee Documents (manual, 1 minute)

The provisioner lays out forms automatically, but it doesn't place file columns.

1. In **Solutions**, go to **HR Automation**, then **Tables**, then **Employee Document**, then **Forms**, then **Information**.
2. Drag the **File** column onto the form, under Document Type.
3. Click **Save and publish**.

> Re-running `provision` rewrites this form, so repeat this step after every re-run.

## Step 5: Check that it works

Open **HR Hub** and check each of the following.

| Check | Expected |
|---|---|
| **People**, then **Employees** | 50 active employees, with Employee Numbers `EMP-0001` and up |
| Open an **HR Executive** | **Owner** is you, because you're linked to their manager, the Head of HR. **Related**, then **Employment History** shows a "Joining" row |
| Open an **Engineer** | **Owner** is the **HR** team, because their manager has no user |
| Change an employee's **Designation** and save | A new "Promotion" row appears in Employment History |
| Type `abc` in **PAN** and save | Error: "PAN must be 10 characters like ABCDE1234F." |
| Type `1234 5678 9012` in **Aadhaar Last 4** | Error telling you to enter only the last 4 digits |
| **Employees** view selector | Shows **My Direct Reports**, **My Team (including indirect)** and **Employees on Probation** |
| Search the employee grid by employee number or email | Matching records are found |

## Step 6: Save the solution to Git

```powershell
pac auth list                    # confirm the * is on HR-Dev / Viipiin-Dev
./scripts/export-solution.ps1
git add src/solution
git commit -m "Phase 1: export HRAutomation solution after Core HR build"
git push
```

You're on `master`. If you'd rather review this in a pull request, create a branch first: `git checkout -b phase1-export`.

## Optional: test as other users

Your tenant (`them365dev`) may have sample users. To test the security roles with one of them:

1. In the admin center, go to **Users**, select the user, then **Manage security roles**. Give them **Basic User** plus one HR role, for example **HR Line Manager** + **HR Employee**.
2. In HR Hub, go to **Share** and add the same roles to the app.
3. Set **System User** on one of the seed employees to that user. Their direct reports then become owned by that user.

## Paste back to Claude

- The output of `./scripts/run-phase1.ps1`, or at least the last 30 lines and any red `✖` lines
- The results of the Step 5 checks
