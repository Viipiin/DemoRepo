namespace HRAutomation.Plugins
{
    // Logical names and option values for Phase 2 (Leave & Attendance).
    // Option values come from the order of options in model/*.json: first = 817990000, second = 817990001, ...

    public static class LeaveRequest
    {
        public const string EntityName = "hra_leaverequest";
        public const string Employee = "hra_employee";
        public const string LeaveType = "hra_leavetype";
        public const string FromDate = "hra_fromdate";
        public const string ToDate = "hra_todate";
        public const string FirstDayHalf = "hra_firstdayhalf";
        public const string LastDayHalf = "hra_lastdayhalf";
        public const string NumberOfDays = "hra_numberofdays";
        public const string LeaveYear = "hra_leaveyear";
        public const string Status = "hra_status";
        public const string Approver = "hra_approver";
        public const string ActionedOn = "hra_actionedon";
    }

    public static class LeaveType
    {
        public const string EntityName = "hra_leavetype";
        public const string Code = "hra_code";
        public const string IsPaid = "hra_ispaid";
        public const string AllowHalfDay = "hra_allowhalfday";
        public const string ApplicableGender = "hra_applicablegender";
        public const string MaxConsecutiveDays = "hra_maxconsecutivedays";
        public const string CountSandwichedHolidays = "hra_countsandwichedholidays";
    }

    public static class LeavePolicy
    {
        public const string EntityName = "hra_leavepolicy";
        public const string IsDefault = "hra_isdefault";
    }

    public static class LeavePolicyLine
    {
        public const string EntityName = "hra_leavepolicyline";
        public const string LeavePolicy = "hra_leavepolicy";
        public const string LeaveType = "hra_leavetype";
        public const string AnnualEntitlement = "hra_annualentitlement";
        public const string AccrualFrequency = "hra_accrualfrequency";
        public const string MaxBalance = "hra_maxbalance";
        public const string AvailableDuringProbation = "hra_availableduringprobation";
        public const string ProrateOnJoining = "hra_prorateonjoining";
    }

    public static class LeaveBalance
    {
        public const string EntityName = "hra_leavebalance";
        public const string Name = "hra_name";
        public const string Employee = "hra_employee";
        public const string LeaveType = "hra_leavetype";
        public const string LeaveYear = "hra_leaveyear";
        public const string Opening = "hra_opening";
        public const string Accrued = "hra_accrued";
        public const string Taken = "hra_taken";
        public const string Adjusted = "hra_adjusted";
        public const string CarriedForward = "hra_carriedforward";
        public const string Encashed = "hra_encashed";
        public const string PendingApproval = "hra_pendingapproval";
        public const string Available = "hra_available";
        public const string LastAccrualPeriod = "hra_lastaccrualperiod";

        public static readonly string[] Components = { Opening, Accrued, Taken, Adjusted, CarriedForward, Encashed, PendingApproval };
    }

    public static class LeaveAdjustment
    {
        public const string EntityName = "hra_leaveadjustment";
        public const string Employee = "hra_employee";
        public const string LeaveType = "hra_leavetype";
        public const string LeaveYear = "hra_leaveyear";
        public const string Type = "hra_type";
        public const string Days = "hra_days";
    }

    public static class Holiday
    {
        public const string EntityName = "hra_holiday";
        public const string Calendar = "hra_holidaycalendar";
        public const string Date = "hra_date";
        public const string Type = "hra_type";
    }

    public static class HolidayCalendar
    {
        public const string EntityName = "hra_holidaycalendar";
        public const string State = "hra_state";
    }

    public static class Location
    {
        public const string EntityName = "hra_location";
        public const string State = "hra_state";
        public const string WeeklyOffPattern = "hra_weeklyoffpattern";
    }

    public static class Attendance
    {
        public const string EntityName = "hra_attendancerecord";
        public const string CheckIn = "hra_checkin";
        public const string CheckOut = "hra_checkout";
        public const string HoursWorked = "hra_hoursworked";
    }

    /// <summary>hra_approvalstatus (global choice).</summary>
    public enum ApprovalStatus
    {
        Draft = 817990000,
        Submitted = 817990001,
        Approved = 817990002,
        Rejected = 817990003,
        Cancelled = 817990004,
        Withdrawn = 817990005,
    }

    /// <summary>hra_location.hra_weeklyoffpattern.</summary>
    public enum WeeklyOffPattern
    {
        SaturdayAndSunday = 817990000,
        SundayOnly = 817990001,
        SundayAndSecondFourthSaturday = 817990002,
        SundayAndAlternateSaturdays = 817990003,
    }

    /// <summary>hra_holiday.hra_type.</summary>
    public enum HolidayType
    {
        National = 817990000,
        State = 817990001,
        Optional = 817990002,
    }

    /// <summary>hra_leavetype.hra_applicablegender.</summary>
    public enum ApplicableGender
    {
        All = 817990000,
        Female = 817990001,
        Male = 817990002,
    }

    /// <summary>hra_gender (global choice).</summary>
    public static class GenderValue
    {
        public const int Female = 817990000;
        public const int Male = 817990001;
    }

    /// <summary>hra_leavepolicyline.hra_accrualfrequency.</summary>
    public enum AccrualFrequency
    {
        Upfront = 817990000,
        Monthly = 817990001,
        Quarterly = 817990002,
    }

    /// <summary>hra_leaveadjustment.hra_type.</summary>
    public enum AdjustmentType
    {
        CompOffCredit = 817990000,
        Encashment = 817990001,
        Correction = 817990002,
        CarryForward = 817990003,
        Lapse = 817990004,
    }
}
