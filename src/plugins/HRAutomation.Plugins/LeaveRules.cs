using System;
using System.Collections.Generic;

namespace HRAutomation.Plugins
{
    /// <summary>
    /// Pure leave rules with no Dataverse dependency, so they can be unit tested.
    /// </summary>
    public static class LeaveRules
    {
        /// <summary>Leave year label for a date, e.g. 2026-10-03 with an April start gives "2026-27".</summary>
        public static string LeaveYear(DateTime date, int startMonth)
        {
            var startYear = date.Month >= startMonth ? date.Year : date.Year - 1;
            return startMonth == 1
                ? startYear.ToString()
                : $"{startYear}-{(startYear + 1) % 100:00}";
        }

        /// <summary>First day of the leave year that contains the date.</summary>
        public static DateTime LeaveYearStart(DateTime date, int startMonth)
        {
            var startYear = date.Month >= startMonth ? date.Year : date.Year - 1;
            return new DateTime(startYear, startMonth, 1);
        }

        public static bool IsWeeklyOff(DateTime date, WeeklyOffPattern pattern)
        {
            if (date.DayOfWeek == DayOfWeek.Sunday) return true;
            if (date.DayOfWeek != DayOfWeek.Saturday) return false;

            var saturdayOfMonth = (date.Day - 1) / 7 + 1; // 1st, 2nd, ... Saturday
            switch (pattern)
            {
                case WeeklyOffPattern.SaturdayAndSunday:
                    return true;
                case WeeklyOffPattern.SundayAndSecondFourthSaturday:
                    return saturdayOfMonth == 2 || saturdayOfMonth == 4;
                case WeeklyOffPattern.SundayAndAlternateSaturdays:
                    return saturdayOfMonth % 2 == 1; // 1st, 3rd and 5th Saturdays off
                default:
                    return false;
            }
        }

        /// <summary>
        /// Leave days between two dates (inclusive). Weekly offs and holidays are skipped unless
        /// countAllDays is set (e.g. maternity leave counts calendar days). Half days take 0.5 off.
        /// </summary>
        public static decimal CountLeaveDays(DateTime from, DateTime to, bool firstDayHalf, bool lastDayHalf,
            WeeklyOffPattern pattern, ISet<DateTime> holidays, bool countAllDays)
        {
            from = from.Date;
            to = to.Date;
            if (to < from) return 0m;

            bool Counts(DateTime day) => countAllDays || (!IsWeeklyOff(day, pattern) && !holidays.Contains(day));

            decimal days = 0m;
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                if (Counts(day)) days += 1m;
            }

            if (from == to)
            {
                return Counts(from) && (firstDayHalf || lastDayHalf) ? 0.5m : days;
            }
            if (firstDayHalf && Counts(from)) days -= 0.5m;
            if (lastDayHalf && Counts(to)) days -= 0.5m;
            return days;
        }

        /// <summary>How a request in a given status counts against the balance: (pending, taken).</summary>
        public static (decimal Pending, decimal Taken) Effect(ApprovalStatus? status, decimal days)
        {
            switch (status)
            {
                case ApprovalStatus.Submitted: return (days, 0m);
                case ApprovalStatus.Approved: return (0m, days);
                default: return (0m, 0m);
            }
        }

        /// <summary>Status changes users may make. HR can override through the same rules.</summary>
        public static bool IsAllowedTransition(ApprovalStatus from, ApprovalStatus to)
        {
            if (from == to) return true;
            switch (from)
            {
                case ApprovalStatus.Draft:
                    return to == ApprovalStatus.Submitted || to == ApprovalStatus.Withdrawn;
                case ApprovalStatus.Submitted:
                    return to == ApprovalStatus.Approved || to == ApprovalStatus.Rejected || to == ApprovalStatus.Withdrawn;
                case ApprovalStatus.Approved:
                    return to == ApprovalStatus.Cancelled;
                default:
                    return false; // Rejected, Cancelled and Withdrawn are final.
            }
        }

        /// <summary>Which balance column an adjustment changes, and by how much.</summary>
        public static (string Column, decimal Delta) AdjustmentEffect(AdjustmentType type, decimal days)
        {
            switch (type)
            {
                case AdjustmentType.CompOffCredit: return (LeaveBalance.Adjusted, Math.Abs(days));
                case AdjustmentType.Encashment: return (LeaveBalance.Encashed, Math.Abs(days));
                case AdjustmentType.CarryForward: return (LeaveBalance.CarriedForward, days);
                case AdjustmentType.Lapse: return (LeaveBalance.Adjusted, -Math.Abs(days));
                default: return (LeaveBalance.Adjusted, days); // Correction: positive or negative
            }
        }

        public static decimal Available(decimal opening, decimal accrued, decimal carriedForward, decimal adjusted,
            decimal taken, decimal encashed, decimal pending) =>
            opening + accrued + carriedForward + adjusted - taken - encashed - pending;

        /// <summary>Rounds to the nearest half day, so prorated and accrued values stay usable.</summary>
        public static decimal RoundToHalf(decimal value) => Math.Round(value * 2m, MidpointRounding.AwayFromZero) / 2m;

        /// <summary>
        /// Upfront entitlement for someone who joins during the leave year: the share of whole months
        /// left, counting the joining month, rounded to half a day.
        /// </summary>
        public static decimal Prorate(decimal annual, DateTime joining, DateTime leaveYearStart)
        {
            if (joining <= leaveYearStart) return annual;
            var leaveYearEnd = leaveYearStart.AddYears(1);
            if (joining >= leaveYearEnd) return 0m;
            var monthsLeft = (leaveYearEnd.Year - joining.Year) * 12 + leaveYearEnd.Month - joining.Month;
            return RoundToHalf(annual * monthsLeft / 12m);
        }

        /// <summary>
        /// Days to accrue for a period (month number within the leave year, 1-12), capped so the
        /// balance doesn't go over maxBalance (0 = no cap).
        /// </summary>
        public static decimal AccrualAmount(AccrualFrequency frequency, decimal annual, int monthOfLeaveYear,
            decimal currentAvailable, decimal maxBalance)
        {
            decimal amount;
            switch (frequency)
            {
                case AccrualFrequency.Monthly:
                    amount = annual / 12m;
                    break;
                case AccrualFrequency.Quarterly:
                    amount = (monthOfLeaveYear - 1) % 3 == 0 ? annual / 4m : 0m;
                    break;
                default:
                    return 0m; // Upfront entitlement is given when the balance is created.
            }
            if (maxBalance > 0m) amount = Math.Min(amount, Math.Max(0m, maxBalance - currentAvailable));
            return Math.Round(amount, 2);
        }

        /// <summary>Month number within the leave year (1 = first month).</summary>
        public static int MonthOfLeaveYear(DateTime date, int startMonth) => (date.Month - startMonth + 12) % 12 + 1;
    }
}
