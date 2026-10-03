namespace HRAutomation.Plugins
{
    /// <summary>Logical names used by the plugins. Must match the provisioner model.</summary>
    public static class Employee
    {
        public const string EntityName = "hra_employee";
        public const string Id = "hra_employeeid";
        public const string FirstName = "hra_firstname";
        public const string MiddleName = "hra_middlename";
        public const string LastName = "hra_lastname";
        public const string FullName = "hra_fullname";
        public const string DateOfJoining = "hra_dateofjoining";
        public const string ProbationEndDate = "hra_probationenddate";
        public const string NoticePeriodDays = "hra_noticeperioddays";
        public const string YearsOfService = "hra_yearsofservice";
        public const string GratuityEligible = "hra_gratuityeligible";
        public const string IsPeopleManager = "hra_ispeoplemanager";
        public const string Department = "hra_department";
        public const string Designation = "hra_designation";
        public const string Location = "hra_location";
        public const string ReportingManager = "hra_reportingmanager";
        public const string SystemUser = "hra_systemuser";
        public const string Pan = "hra_pan";
        public const string AadhaarLast4 = "hra_aadhaarlast4";
        public const string Uan = "hra_uan";
        public const string Ifsc = "hra_ifsc";
        public const string AnnualCtc = "hra_annualctc";
        public const string Owner = "ownerid";
        public const string StateCode = "statecode";
    }

    public static class EmploymentHistory
    {
        public const string EntityName = "hra_employmenthistory";
        public const string Employee = "hra_employee";
        public const string ChangeType = "hra_changetype";
        public const string EffectiveDate = "hra_effectivedate";
        public const string FromDepartment = "hra_fromdepartment";
        public const string ToDepartment = "hra_todepartment";
        public const string FromDesignation = "hra_fromdesignation";
        public const string ToDesignation = "hra_todesignation";
        public const string FromLocation = "hra_fromlocation";
        public const string ToLocation = "hra_tolocation";
        public const string FromManager = "hra_frommanager";
        public const string ToManager = "hra_tomanager";
        public const string FromCtc = "hra_fromctc";
        public const string ToCtc = "hra_toctc";
        public const string Remarks = "hra_remarks";
    }

    /// <summary>Option values of the local choice hra_employmenthistory.hra_changetype.</summary>
    public static class ChangeType
    {
        public const int Joining = 817990000;
        public const int Confirmation = 817990001;
        public const int Promotion = 817990002;
        public const int Transfer = 817990003;
        public const int ManagerChange = 817990004;
        public const int SalaryRevision = 817990005;
        public const int Separation = 817990006;
    }

    public static class Settings
    {
        /// <summary>Owner team used when an employee's manager has no Dataverse user.</summary>
        public const string HrTeamName = "HR";
        public const string ProbationMonthsVariable = "hra_ProbationMonths";
        public const int DefaultProbationMonths = 6;
        public const int DefaultNoticePeriodDays = 30;
    }
}
