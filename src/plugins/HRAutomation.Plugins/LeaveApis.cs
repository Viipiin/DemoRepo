using System;
using System.Globalization;

namespace HRAutomation.Plugins
{
    /// <summary>
    /// Custom API hra_InitializeLeaveBalances. Inputs: EmployeeId (Guid, optional), LeaveYear (String, optional,
    /// e.g. "2026-27"). Output: Created (Integer). Creates the missing balances from each employee's leave policy.
    /// </summary>
    public class InitializeLeaveBalancesApi : PluginBase
    {
        protected override void ExecuteInternal(LocalContext local)
        {
            var leave = new LeaveService(local);
            var input = local.Context.InputParameters;
            Guid? employeeId = input.Contains("EmployeeId") && input["EmployeeId"] is Guid id && id != Guid.Empty ? id : (Guid?)null;
            var year = input.Contains("LeaveYear") && input["LeaveYear"] is string text && !string.IsNullOrWhiteSpace(text)
                ? text.Trim()
                : leave.CurrentLeaveYear();
            local.Context.OutputParameters["Created"] = leave.InitializeBalances(employeeId, year);
        }
    }

    /// <summary>
    /// Custom API hra_RunLeaveAccrual. Input: Period (String, optional, "yyyy-MM", default the current month in IST).
    /// Output: Updated (Integer). Adds monthly or quarterly accrual once per balance per period.
    /// Call it monthly from a scheduled cloud flow.
    /// </summary>
    public class RunLeaveAccrualApi : PluginBase
    {
        protected override void ExecuteInternal(LocalContext local)
        {
            var input = local.Context.InputParameters;
            var text = input.Contains("Period") ? input["Period"] as string : null;
            DateTime period;
            if (string.IsNullOrWhiteSpace(text))
            {
                var ist = DateTime.UtcNow.AddHours(5.5);
                period = new DateTime(ist.Year, ist.Month, 1);
            }
            else if (!DateTime.TryParseExact(text.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out period))
            {
                throw new Microsoft.Xrm.Sdk.InvalidPluginExecutionException("Period must look like 2026-10.");
            }
            local.Context.OutputParameters["Updated"] = new LeaveService(local).RunAccrual(period);
        }
    }

    /// <summary>
    /// Custom API hra_RunYearEndRollover. Inputs: EmployeeId (Guid, optional), FromLeaveYear (String, optional,
    /// default the leave year before the current one). Output: RolledOver (Integer).
    /// Run on 1 April, one call per employee (the yearly flow does this).
    /// </summary>
    public class RunYearEndRolloverApi : PluginBase
    {
        protected override void ExecuteInternal(LocalContext local)
        {
            var leave = new LeaveService(local);
            var input = local.Context.InputParameters;
            Guid? employeeId = input.Contains("EmployeeId") && input["EmployeeId"] is Guid id && id != Guid.Empty ? id : (Guid?)null;
            var fromYear = input.Contains("FromLeaveYear") && input["FromLeaveYear"] is string text && !string.IsNullOrWhiteSpace(text)
                ? text.Trim()
                : LeaveRules.PreviousLeaveYear(leave.CurrentLeaveYear());
            local.Context.OutputParameters["RolledOver"] = leave.RunYearEndRollover(employeeId, fromYear);
        }
    }
}
