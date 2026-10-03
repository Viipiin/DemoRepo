using System;

namespace HRAutomation.Plugins
{
    /// <summary>
    /// P-07. hra_attendancerecord Create and Update, PreOperation, synchronous.
    /// Calculates Hours Worked from check-in and check-out.
    /// Update step needs a PreImage with check-in and check-out.
    /// </summary>
    public class AttendancePreOperation : PluginBase
    {
        protected override void ExecuteInternal(LocalContext local)
        {
            var target = local.Target;
            if (target == null || target.LogicalName != Attendance.EntityName) return;
            if (!local.IsCreate && !target.Contains(Attendance.CheckIn) && !target.Contains(Attendance.CheckOut)) return;

            var checkIn = local.Merged<DateTime?>(Attendance.CheckIn);
            var checkOut = local.Merged<DateTime?>(Attendance.CheckOut);
            if (checkIn.HasValue && checkOut.HasValue)
            {
                if (checkOut < checkIn) throw new Microsoft.Xrm.Sdk.InvalidPluginExecutionException("Check-out can't be before check-in.");
                target[Attendance.HoursWorked] = Math.Round((decimal)(checkOut.Value - checkIn.Value).TotalHours, 2);
            }
            else
            {
                target[Attendance.HoursWorked] = null;
            }
        }
    }
}
