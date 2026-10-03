using HRAutomation.Plugins;
using Xunit;

public class LeaveRulesTests
{
    private static readonly HashSet<DateTime> NoHolidays = new();

    [Theory]
    [InlineData("2026-10-03", 4, "2026-27")]
    [InlineData("2027-03-31", 4, "2026-27")]
    [InlineData("2027-04-01", 4, "2027-28")]
    [InlineData("2026-06-15", 1, "2026")]
    public void LeaveYear_UsesStartMonth(string date, int startMonth, string expected)
    {
        Assert.Equal(expected, LeaveRules.LeaveYear(DateTime.Parse(date), startMonth));
    }

    [Theory]
    [InlineData("2026-10-03", WeeklyOffPattern.SaturdayAndSunday, true)]   // Saturday
    [InlineData("2026-10-04", WeeklyOffPattern.SundayOnly, true)]          // Sunday
    [InlineData("2026-10-03", WeeklyOffPattern.SundayOnly, false)]
    [InlineData("2026-10-10", WeeklyOffPattern.SundayAndSecondFourthSaturday, true)]  // 2nd Saturday
    [InlineData("2026-10-03", WeeklyOffPattern.SundayAndSecondFourthSaturday, false)] // 1st Saturday
    [InlineData("2026-10-03", WeeklyOffPattern.SundayAndAlternateSaturdays, true)]    // 1st Saturday
    [InlineData("2026-10-10", WeeklyOffPattern.SundayAndAlternateSaturdays, false)]   // 2nd Saturday
    [InlineData("2026-10-05", WeeklyOffPattern.SaturdayAndSunday, false)]  // Monday
    public void IsWeeklyOff(string date, WeeklyOffPattern pattern, bool expected)
    {
        Assert.Equal(expected, LeaveRules.IsWeeklyOff(DateTime.Parse(date), pattern));
    }

    [Fact]
    public void CountLeaveDays_SkipsWeekendsAndHolidays()
    {
        // Mon 28 Sep to Fri 9 Oct 2026, with Gandhi Jayanti on Fri 2 Oct.
        var holidays = new HashSet<DateTime> { new(2026, 10, 2) };
        var days = LeaveRules.CountLeaveDays(new(2026, 9, 28), new(2026, 10, 9), false, false,
            WeeklyOffPattern.SaturdayAndSunday, holidays, countAllDays: false);
        Assert.Equal(9m, days);
    }

    [Fact]
    public void CountLeaveDays_HalfDays()
    {
        var from = new DateTime(2026, 10, 5); // Monday
        Assert.Equal(0.5m, LeaveRules.CountLeaveDays(from, from, true, false, WeeklyOffPattern.SaturdayAndSunday, NoHolidays, false));
        Assert.Equal(1.5m, LeaveRules.CountLeaveDays(from, from.AddDays(1), true, false, WeeklyOffPattern.SaturdayAndSunday, NoHolidays, false));
        Assert.Equal(1m, LeaveRules.CountLeaveDays(from, from.AddDays(1), true, true, WeeklyOffPattern.SaturdayAndSunday, NoHolidays, false));
    }

    [Fact]
    public void CountLeaveDays_WeekendOnlyIsZero_UnlessCountingAllDays()
    {
        var saturday = new DateTime(2026, 10, 3);
        Assert.Equal(0m, LeaveRules.CountLeaveDays(saturday, saturday.AddDays(1), false, false, WeeklyOffPattern.SaturdayAndSunday, NoHolidays, false));
        Assert.Equal(2m, LeaveRules.CountLeaveDays(saturday, saturday.AddDays(1), false, false, WeeklyOffPattern.SaturdayAndSunday, NoHolidays, true));
    }

    [Theory]
    [InlineData(ApprovalStatus.Submitted, 3, 3, 0)]
    [InlineData(ApprovalStatus.Approved, 3, 0, 3)]
    [InlineData(ApprovalStatus.Rejected, 3, 0, 0)]
    [InlineData(ApprovalStatus.Cancelled, 3, 0, 0)]
    public void Effect(ApprovalStatus status, int days, int pending, int taken)
    {
        Assert.Equal(((decimal)pending, (decimal)taken), LeaveRules.Effect(status, days));
    }

    [Theory]
    [InlineData(ApprovalStatus.Submitted, ApprovalStatus.Approved, true)]
    [InlineData(ApprovalStatus.Submitted, ApprovalStatus.Withdrawn, true)]
    [InlineData(ApprovalStatus.Approved, ApprovalStatus.Cancelled, true)]
    [InlineData(ApprovalStatus.Approved, ApprovalStatus.Rejected, false)]
    [InlineData(ApprovalStatus.Rejected, ApprovalStatus.Approved, false)]
    [InlineData(ApprovalStatus.Draft, ApprovalStatus.Approved, false)]
    public void IsAllowedTransition(ApprovalStatus from, ApprovalStatus to, bool expected)
    {
        Assert.Equal(expected, LeaveRules.IsAllowedTransition(from, to));
    }

    [Fact]
    public void AdjustmentEffect()
    {
        Assert.Equal((LeaveBalance.Adjusted, 1m), LeaveRules.AdjustmentEffect(AdjustmentType.CompOffCredit, 1m));
        Assert.Equal((LeaveBalance.Encashed, 5m), LeaveRules.AdjustmentEffect(AdjustmentType.Encashment, -5m));
        Assert.Equal((LeaveBalance.Adjusted, -2m), LeaveRules.AdjustmentEffect(AdjustmentType.Lapse, 2m));
        Assert.Equal((LeaveBalance.Adjusted, -1.5m), LeaveRules.AdjustmentEffect(AdjustmentType.Correction, -1.5m));
        Assert.Equal((LeaveBalance.CarriedForward, 10m), LeaveRules.AdjustmentEffect(AdjustmentType.CarryForward, 10m));
    }

    [Fact]
    public void Available_SumsComponents()
    {
        Assert.Equal(12.5m, LeaveRules.Available(8, 9, 2, 1, 5, 2, 0.5m));
    }

    [Theory]
    [InlineData("2026-03-01", 8, 8)]    // joined before the leave year
    [InlineData("2026-04-01", 8, 8)]    // first day
    [InlineData("2026-10-03", 8, 4)]    // 6 months left (Oct..Mar)
    [InlineData("2027-03-15", 8, 0.5)]  // 1 month left
    [InlineData("2027-04-01", 8, 0)]    // after the leave year
    public void Prorate(string joining, double annual, double expected)
    {
        Assert.Equal((decimal)expected, LeaveRules.Prorate((decimal)annual, DateTime.Parse(joining), new DateTime(2026, 4, 1)));
    }

    [Fact]
    public void AccrualAmount()
    {
        Assert.Equal(1.5m, LeaveRules.AccrualAmount(AccrualFrequency.Monthly, 18m, 7, 10m, 45m));
        Assert.Equal(1m, LeaveRules.AccrualAmount(AccrualFrequency.Monthly, 18m, 7, 44m, 45m));   // capped
        Assert.Equal(0m, LeaveRules.AccrualAmount(AccrualFrequency.Monthly, 18m, 7, 45m, 45m));
        Assert.Equal(2m, LeaveRules.AccrualAmount(AccrualFrequency.Quarterly, 8m, 4, 0m, 0m));     // start of Q2
        Assert.Equal(0m, LeaveRules.AccrualAmount(AccrualFrequency.Quarterly, 8m, 5, 0m, 0m));
        Assert.Equal(0m, LeaveRules.AccrualAmount(AccrualFrequency.Upfront, 8m, 1, 0m, 0m));
    }

    [Theory]
    [InlineData("2026-04-10", 4, 1)]
    [InlineData("2026-10-03", 4, 7)]
    [InlineData("2027-03-01", 4, 12)]
    public void MonthOfLeaveYear(string date, int startMonth, int expected)
    {
        Assert.Equal(expected, LeaveRules.MonthOfLeaveYear(DateTime.Parse(date), startMonth));
    }
}
