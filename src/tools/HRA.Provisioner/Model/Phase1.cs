using static HRA.Provisioner.Model.Requirement;

namespace HRA.Provisioner.Model;

/// <summary>
/// Phase 1: Core HR. Mirrors docs/architecture.md section 2.1 and docs/hr-automation/data-model.md.
/// Later phases add their own model files; everything here is safe to re-run.
/// </summary>
public static class Phase1
{
    // Global choices (data-model.md section 1). Option values are 817990000 + index.
    public static readonly ChoiceDef EmploymentType = new("hra_employmenttype", "Employment Type",
        "Permanent", "Probation", "Contract", "Intern", "Consultant");

    public static readonly ChoiceDef EmploymentStatus = new("hra_employmentstatus", "Employment Status",
        "Active", "On Notice", "Separated", "Absconding", "Retired");

    public static readonly ChoiceDef Gender = new("hra_gender", "Gender",
        "Female", "Male", "Non-binary", "Prefer not to say");

    public static readonly ChoiceDef IndianState = new("hra_indianstate", "Indian State",
        // 28 states
        "Andhra Pradesh", "Arunachal Pradesh", "Assam", "Bihar", "Chhattisgarh", "Goa", "Gujarat", "Haryana",
        "Himachal Pradesh", "Jharkhand", "Karnataka", "Kerala", "Madhya Pradesh", "Maharashtra", "Manipur",
        "Meghalaya", "Mizoram", "Nagaland", "Odisha", "Punjab", "Rajasthan", "Sikkim", "Tamil Nadu", "Telangana",
        "Tripura", "Uttar Pradesh", "Uttarakhand", "West Bengal",
        // 8 union territories
        "Andaman and Nicobar Islands", "Chandigarh", "Dadra and Nagar Haveli and Daman and Diu", "Delhi",
        "Jammu and Kashmir", "Ladakh", "Lakshadweep", "Puducherry");

    public static readonly ChoiceDef ApprovalStatus = new("hra_approvalstatus", "Approval Status",
        "Draft", "Submitted", "Approved", "Rejected", "Cancelled", "Withdrawn");

    public static readonly ChoiceDef ResponsibleTeam = new("hra_responsibleteam", "Responsible Team",
        "HR", "IT", "Admin/Facilities", "Finance", "Reporting Manager", "Employee");

    public static readonly ChoiceDef DocumentType = new("hra_documenttype", "Document Type",
        "PAN Card", "Aadhaar (masked)", "Passport", "Offer Letter", "Appointment Letter", "Relieving Letter",
        "Education Certificate", "Experience Letter", "Medical Certificate", "Other");

    public static readonly ChoiceDef Rating = new("hra_rating", "Rating",
        "1 - Needs Improvement", "2 - Partially Meets", "3 - Meets", "4 - Exceeds", "5 - Outstanding");

    public static readonly ChoiceDef[] GlobalChoices =
    {
        EmploymentType, EmploymentStatus, Gender, IndianState, ApprovalStatus, ResponsibleTeam, DocumentType, Rating
    };

    public const int KarnatakaIndex = 10;

    public static readonly TableDef Department = new("hra_Department", "Department", "Departments")
    {
        Description = "Company departments.",
        OrganizationOwned = true,
        PrimaryName = Col.Name("hra_Name", "Name"),
        Columns =
        {
            Col.Text("hra_Code", "Code", 20, Required),
            Col.Lookup("hra_ParentDepartment", "Parent Department", "hra_department"),
            Col.Lookup("hra_DepartmentHead", "Department Head", "hra_employee", req: Recommended),
            Col.Text("hra_CostCentre", "Cost Centre", 20),
            Col.Lookup("hra_OwnerTeam", "Owner Team", "team"),
        },
        Keys = { new KeyDef("hra_key_department_code", "Department Code", "hra_code") },
        Form = { new FormTab("general", "General", new[] { "hra_name", "hra_code", "hra_parentdepartment" }, new[] { "hra_departmenthead", "hra_costcentre", "hra_ownerteam" }) },
        Views = { new ViewDef(null, new[] { "hra_name", "hra_code", "hra_departmenthead", "hra_parentdepartment", "hra_costcentre" }) { SortColumn = "hra_name" } },
        QuickFind = new[] { "hra_code" },
    };

    public static readonly TableDef Designation = new("hra_Designation", "Designation", "Designations")
    {
        Description = "Job titles and grades.",
        OrganizationOwned = true,
        PrimaryName = Col.Name("hra_Name", "Name"),
        Columns =
        {
            Col.Choice("hra_Grade", "Grade", Required, null, "L1", "L2", "L3", "L4", "L5", "L6", "L7", "L8"),
            Col.Lookup("hra_Department", "Department", "hra_department", LookupBehaviour.Restrict),
        },
        Form = { new FormTab("general", "General", new[] { "hra_name", "hra_grade" }, new[] { "hra_department" }) },
        Views = { new ViewDef(null, new[] { "hra_name", "hra_grade", "hra_department" }) { SortColumn = "hra_name" } },
    };

    public static readonly TableDef Location = new("hra_Location", "Location", "Locations")
    {
        Description = "Office locations. The state decides the holiday calendar; the weekly off pattern decides working days.",
        OrganizationOwned = true,
        PrimaryName = Col.Name("hra_Name", "Name"),
        Columns =
        {
            Col.Text("hra_City", "City", 50, Required),
            Col.Global("hra_State", "State", "hra_indianstate", Required),
            Col.Memo("hra_Address", "Address", 500),
            Col.Choice("hra_WeeklyOffPattern", "Weekly Off Pattern", Required, 0,
                "Saturday and Sunday", "Sunday only", "Sunday + 2nd and 4th Saturday", "Sunday + alternate Saturdays"),
        },
        Form = { new FormTab("general", "General", new[] { "hra_name", "hra_city", "hra_state" }, new[] { "hra_weeklyoffpattern", "hra_address" }) },
        Views = { new ViewDef(null, new[] { "hra_name", "hra_city", "hra_state", "hra_weeklyoffpattern" }) { SortColumn = "hra_name" } },
    };

    public static readonly TableDef Employee = new("hra_Employee", "Employee", "Employees")
    {
        Description = "Employee master record. Owned by the reporting manager's user (or the HR team).",
        HasNotes = true,
        Audited = true,
        PrimaryName = new ColumnDef("hra_FullName", "Full Name", ColumnType.Text)
        {
            MaxLength = 160,
            Description = "Built from first, middle and last name by the EmployeePreOperation plugin.",
            ReadOnlyOnForm = true,
        },
        Columns =
        {
            Col.AutoNumber("hra_EmployeeNumber", "Employee Number", "EMP-{SEQNUM:4}"),
            Col.Text("hra_FirstName", "First Name", 50, Required),
            Col.Text("hra_MiddleName", "Middle Name", 50),
            Col.Text("hra_LastName", "Last Name", 50, Required),
            Col.Email("hra_WorkEmail", "Work Email", Required),
            Col.Email("hra_PersonalEmail", "Personal Email", Recommended),
            Col.Phone("hra_Mobile", "Mobile", Required),
            Col.Date("hra_DateOfBirth", "Date of Birth", Required),
            Col.Global("hra_Gender", "Gender", "hra_gender", Required),
            Col.Date("hra_DateOfJoining", "Date of Joining", Required),
            Col.Date("hra_ProbationEndDate", "Probation End Date", Recommended),
            Col.Date("hra_ConfirmationDate", "Confirmation Date"),
            Col.Global("hra_EmploymentType", "Employment Type", "hra_employmenttype", Required, 0),
            Col.Global("hra_EmploymentStatus", "Employment Status", "hra_employmentstatus", Required, 0),
            Col.Lookup("hra_Department", "Department", "hra_department", LookupBehaviour.Restrict, Required),
            Col.Lookup("hra_Designation", "Designation", "hra_designation", LookupBehaviour.Restrict, Required),
            Col.Lookup("hra_Location", "Location", "hra_location", LookupBehaviour.Restrict, Required),
            Col.Lookup("hra_ReportingManager", "Reporting Manager", "hra_employee", req: Recommended),
            Col.Lookup("hra_HRBusinessPartner", "HR Business Partner", "hra_employee"),
            Col.Lookup("hra_SystemUser", "System User", "systemuser"),
            Col.Int("hra_NoticePeriodDays", "Notice Period (days)", 0, 180, Recommended),
            Col.Date("hra_LastWorkingDay", "Last Working Day"),
            Col.Decimal("hra_YearsOfService", "Years of Service", 1, 0, 100) with { ReadOnlyOnForm = true },
            Col.YesNo("hra_GratuityEligible", "Gratuity Eligible") with { ReadOnlyOnForm = true },
            Col.YesNo("hra_IsPeopleManager", "Is People Manager") with { ReadOnlyOnForm = true },
            Col.Secured("hra_PAN", "PAN", 10),
            Col.Secured("hra_AadhaarLast4", "Aadhaar Last 4", 4),
            Col.Secured("hra_UAN", "UAN", 12),
            Col.Secured("hra_ESICNumber", "ESIC Number", 17),
            Col.Secured("hra_BankAccountNumber", "Bank Account Number", 20),
            Col.Secured("hra_IFSC", "IFSC", 11),
            Col.Secured("hra_BankName", "Bank Name", 100),
            Col.Money("hra_AnnualCTC", "Annual CTC", secured: true),
        },
        Keys =
        {
            new KeyDef("hra_key_employee_number", "Employee Number", "hra_employeenumber"),
            new KeyDef("hra_key_employee_workemail", "Work Email", "hra_workemail"),
        },
        Form =
        {
            new FormTab("summary", "Summary",
                new[] { "hra_employeenumber", "hra_firstname", "hra_middlename", "hra_lastname", "hra_fullname" },
                new[] { "hra_workemail", "hra_mobile", "hra_employmenttype", "hra_employmentstatus", "hra_systemuser" }),
            new FormTab("job", "Job",
                new[] { "hra_department", "hra_designation", "hra_location", "hra_reportingmanager", "hra_hrbusinesspartner" },
                new[] { "hra_dateofjoining", "hra_probationenddate", "hra_confirmationdate", "hra_noticeperioddays", "hra_lastworkingday", "hra_yearsofservice", "hra_gratuityeligible", "hra_ispeoplemanager" }),
            new FormTab("personal", "Personal",
                new[] { "hra_dateofbirth", "hra_gender", "hra_personalemail" }),
            new FormTab("statutory", "Statutory & Bank",
                new[] { "hra_pan", "hra_aadhaarlast4", "hra_uan", "hra_esicnumber" },
                new[] { "hra_bankaccountnumber", "hra_ifsc", "hra_bankname", "hra_annualctc" }),
        },
        Views =
        {
            new ViewDef(null, new[] { "hra_fullname", "hra_employeenumber", "hra_department", "hra_designation", "hra_reportingmanager", "hra_location", "hra_employmentstatus" }) { SortColumn = "hra_fullname" },
            new ViewDef("My Direct Reports", new[] { "hra_fullname", "hra_employeenumber", "hra_designation", "hra_dateofjoining", "hra_employmentstatus" })
            {
                Description = "Active employees owned by me (people who report to me).",
                ExtraConditions = "<condition attribute=\"ownerid\" operator=\"eq-userid\" />",
                SortColumn = "hra_fullname",
            },
            new ViewDef("My Team (including indirect)", new[] { "hra_fullname", "hra_employeenumber", "hra_designation", "hra_reportingmanager", "hra_department" })
            {
                Description = "Active employees owned by me or anyone below me in the manager hierarchy.",
                ExtraConditions = "<condition attribute=\"ownerid\" operator=\"eq-useroruserhierarchy\" />",
                SortColumn = "hra_fullname",
            },
            new ViewDef("Employees on Probation", new[] { "hra_fullname", "hra_department", "hra_reportingmanager", "hra_dateofjoining", "hra_probationenddate" })
            {
                Description = "Active employees with no confirmation date yet.",
                ExtraConditions = "<condition attribute=\"hra_confirmationdate\" operator=\"null\" /><condition attribute=\"hra_probationenddate\" operator=\"not-null\" />",
                SortColumn = "hra_probationenddate",
            },
        },
        QuickFind = new[] { "hra_employeenumber", "hra_workemail", "hra_mobile" },
    };

    public static readonly TableDef EmergencyContact = new("hra_EmergencyContact", "Emergency Contact", "Emergency Contacts")
    {
        Description = "Emergency contacts for an employee.",
        PrimaryName = Col.Name("hra_Name", "Name"),
        Columns =
        {
            Col.Lookup("hra_Employee", "Employee", "hra_employee", LookupBehaviour.Restrict, Required),
            Col.Choice("hra_Relationship", "Relationship", Required, null, "Spouse", "Parent", "Sibling", "Child", "Friend", "Other"),
            Col.Phone("hra_Phone", "Phone", Required),
            Col.YesNo("hra_IsPrimary", "Is Primary"),
        },
        Form = { new FormTab("general", "General", new[] { "hra_employee", "hra_name", "hra_relationship" }, new[] { "hra_phone", "hra_isprimary" }) },
        Views = { new ViewDef(null, new[] { "hra_name", "hra_employee", "hra_relationship", "hra_phone", "hra_isprimary" }) { SortColumn = "hra_name" } },
    };

    public static readonly TableDef EmployeeDocument = new("hra_EmployeeDocument", "Employee Document", "Employee Documents")
    {
        Description = "Documents for an employee (stored in a Dataverse file column).",
        PrimaryName = Col.Name("hra_Title", "Title"),
        Columns =
        {
            Col.Lookup("hra_Employee", "Employee", "hra_employee", LookupBehaviour.Restrict, Required),
            Col.Global("hra_DocumentType", "Document Type", "hra_documenttype", Required),
            Col.File("hra_File", "File", 10240),
            Col.Date("hra_IssueDate", "Issue Date"),
            Col.Date("hra_ExpiryDate", "Expiry Date"),
            Col.YesNo("hra_Verified", "Verified"),
            Col.Lookup("hra_VerifiedBy", "Verified By", "systemuser"),
        },
        // The file column isn't placed by the generator; add it to the form in the designer (see docs/phase-1-runbook.md).
        Form = { new FormTab("general", "General", new[] { "hra_employee", "hra_title", "hra_documenttype" }, new[] { "hra_issuedate", "hra_expirydate", "hra_verified", "hra_verifiedby" }) },
        Views = { new ViewDef(null, new[] { "hra_title", "hra_employee", "hra_documenttype", "hra_expirydate", "hra_verified" }) { SortColumn = "hra_title" } },
    };

    public static readonly TableDef EmploymentHistory = new("hra_EmploymentHistory", "Employment History", "Employment History")
    {
        Description = "Audit trail of job changes. Written by the EmployeePostOperation plugin.",
        Audited = true,
        PrimaryName = new ColumnDef("hra_Name", "Reference", ColumnType.Text) { MaxLength = 20, AutoNumberFormat = "EH-{SEQNUM:6}", ReadOnlyOnForm = true },
        Columns =
        {
            Col.Lookup("hra_Employee", "Employee", "hra_employee", LookupBehaviour.Restrict, Required),
            // Option order must match HRAutomation.Plugins.ChangeType.
            Col.Choice("hra_ChangeType", "Change Type", Required, null,
                "Joining", "Confirmation", "Promotion", "Transfer", "Manager Change", "Salary Revision", "Separation"),
            Col.Date("hra_EffectiveDate", "Effective Date", Required),
            Col.Lookup("hra_FromDepartment", "From Department", "hra_department"),
            Col.Lookup("hra_ToDepartment", "To Department", "hra_department"),
            Col.Lookup("hra_FromDesignation", "From Designation", "hra_designation"),
            Col.Lookup("hra_ToDesignation", "To Designation", "hra_designation"),
            Col.Lookup("hra_FromLocation", "From Location", "hra_location"),
            Col.Lookup("hra_ToLocation", "To Location", "hra_location"),
            Col.Lookup("hra_FromManager", "From Manager", "hra_employee"),
            Col.Lookup("hra_ToManager", "To Manager", "hra_employee"),
            Col.Money("hra_FromCTC", "From CTC", secured: true),
            Col.Money("hra_ToCTC", "To CTC", secured: true),
            Col.Memo("hra_Remarks", "Remarks", 2000),
        },
        Form =
        {
            new FormTab("general", "General",
                new[] { "hra_name", "hra_employee", "hra_changetype", "hra_effectivedate", "hra_remarks" },
                new[] { "hra_fromdepartment", "hra_todepartment", "hra_fromdesignation", "hra_todesignation", "hra_fromlocation", "hra_tolocation", "hra_frommanager", "hra_tomanager", "hra_fromctc", "hra_toctc" }),
        },
        Views = { new ViewDef(null, new[] { "hra_employee", "hra_changetype", "hra_effectivedate", "hra_todesignation", "hra_todepartment", "hra_tomanager" }) { SortColumn = "hra_effectivedate" } },
    };

    public static readonly TableDef HRCase = new("hra_HRCase", "HR Case", "HR Cases")
    {
        Description = "HR helpdesk tickets raised by employees.",
        HasNotes = true,
        PrimaryName = Col.Name("hra_Subject", "Subject", 200),
        Columns =
        {
            Col.AutoNumber("hra_CaseNumber", "Case Number", "HRC-{SEQNUM:5}"),
            Col.Lookup("hra_Employee", "Employee", "hra_employee", LookupBehaviour.Restrict, Required),
            Col.Choice("hra_Category", "Category", Required, null, "Payroll", "Leave", "Policy", "Documents", "IT Access", "Other"),
            Col.Memo("hra_Description", "Description", 4000, Required),
            Col.Choice("hra_Priority", "Priority", Required, 1, "Low", "Normal", "High"),
            Col.Choice("hra_CaseStatus", "Case Status", Required, 0, "New", "In Progress", "Waiting on Employee", "Resolved", "Closed"),
            Col.Lookup("hra_AssignedTo", "Assigned To", "systemuser"),
            Col.Choice("hra_Source", "Source", Optional, 0, "App", "Portal", "Teams Agent", "Email"),
        },
        Form =
        {
            new FormTab("general", "General",
                new[] { "hra_casenumber", "hra_employee", "hra_subject", "hra_category", "hra_description" },
                new[] { "hra_priority", "hra_casestatus", "hra_assignedto", "hra_source" }),
        },
        Views = { new ViewDef(null, new[] { "hra_casenumber", "hra_subject", "hra_employee", "hra_category", "hra_priority", "hra_casestatus", "hra_assignedto" }) { SortColumn = "hra_casenumber" } },
        QuickFind = new[] { "hra_casenumber" },
    };

    /// <summary>Creation order matters only for readability; lookups are created after all tables exist.</summary>
    public static readonly TableDef[] Tables =
    {
        Department, Designation, Location, Employee, EmergencyContact, EmployeeDocument, EmploymentHistory, HRCase
    };

    public static RoleDef[] Roles()
    {
        string[] reference = { "hra_department", "hra_designation", "hra_location" };
        string[] personal = { "hra_emergencycontact", "hra_employeedocument" };

        var hrAdmin = new RoleDef("HR Admin", "Full access to HR Automation, including settings. No delete on employees.");
        var hrManager = new RoleDef("HR Manager", "Organization-wide HR access. Read-only reference data.");
        var recruiter = new RoleDef("HR Recruiter", "Recruitment. Read-only employees without sensitive columns.");
        var lineManager = new RoleDef("HR Line Manager", "Sees and approves for their own team through ownership and the manager hierarchy.");
        var employee = new RoleDef("HR Employee", "Self-service for licensed staff: own profile, contacts, documents and HR cases.");
        var itAdmin = new RoleDef("HR IT Asset Admin", "Assets and IT onboarding/offboarding tasks.");
        var finance = new RoleDef("HR Finance", "Expense payments and Finance checklist tasks.");
        var all = new[] { hrAdmin, hrManager, recruiter, lineManager, employee, itAdmin, finance };

        foreach (var role in all)
        foreach (var table in reference)
        {
            role.Tables[table] = role == hrAdmin ? Access.Org("CRWDATS") : Access.Org("RT");
        }

        hrAdmin.Tables["hra_employee"] = Access.Org("CRWATS");
        hrManager.Tables["hra_employee"] = Access.Org("CRWATS");
        recruiter.Tables["hra_employee"] = Access.Org("RT");
        lineManager.Tables["hra_employee"] = Access.User("RWAT");
        employee.Tables["hra_employee"] = Access.User("RT");
        itAdmin.Tables["hra_employee"] = Access.Org("RT");
        finance.Tables["hra_employee"] = Access.Org("RT");

        foreach (var table in personal)
        {
            hrAdmin.Tables[table] = Access.Org("CRWDATS");
            hrManager.Tables[table] = Access.Org("CRWATS");
            lineManager.Tables[table] = Access.User("R");
            employee.Tables[table] = Access.User("CRWDA");
        }

        hrAdmin.Tables["hra_employmenthistory"] = Access.Org("R");
        hrManager.Tables["hra_employmenthistory"] = Access.Org("R");
        lineManager.Tables["hra_employmenthistory"] = Access.User("R");
        employee.Tables["hra_employmenthistory"] = Access.User("R");

        hrAdmin.Tables["hra_hrcase"] = Access.Org("CRWDATS");
        hrManager.Tables["hra_hrcase"] = Access.Org("CRWATS");
        lineManager.Tables["hra_hrcase"] = Access.User("R");
        employee.Tables["hra_hrcase"] = Access.User("CRA");

        return all;
    }

    public static FieldSecurityProfileDef[] FieldSecurityProfiles()
    {
        var sensitive = new FieldSecurityProfileDef("HR Sensitive Data",
            "PAN, Aadhaar (last 4), UAN, ESIC, bank details and CTC. Members of the HR team only.", "HR");
        foreach (var column in new[] { "hra_pan", "hra_aadhaarlast4", "hra_uan", "hra_esicnumber", "hra_bankaccountnumber", "hra_ifsc", "hra_bankname", "hra_annualctc" })
        {
            sensitive.Columns.Add(("hra_employee", column));
        }
        sensitive.Columns.Add(("hra_employmenthistory", "hra_fromctc"));
        sensitive.Columns.Add(("hra_employmenthistory", "hra_toctc"));
        return new[] { sensitive };
    }

    public static readonly EnvironmentVariableDef[] EnvironmentVariables =
    {
        new("hra_ProbationMonths", "Probation Months", "6", "Default probation length for new employees."),
        new("hra_LeaveEscalationDays", "Leave Escalation Days", "2", "Working days before an unapproved leave request escalates to HR."),
        new("hra_LeaveYearStartMonth", "Leave Year Start Month", "4", "Month the leave year starts (4 = April)."),
        new("hra_CandidateRetentionMonths", "Candidate Retention Months", "12", "Months to keep unsuccessful candidates before anonymising (DPDP Act)."),
        new("hra_LeaverRetentionMonths", "Leaver Retention Months", "84", "Months after the last working day before leavers' sensitive data is masked."),
        new("hra_ReceiptRequiredAbove", "Receipt Required Above (INR)", "500", "Expense lines above this amount need a receipt."),
    };
}
