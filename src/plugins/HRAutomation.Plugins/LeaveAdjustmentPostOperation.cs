using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace HRAutomation.Plugins
{
    /// <summary>
    /// P-05. hra_leaveadjustment Create, PostOperation, synchronous.
    /// Applies the adjustment (comp-off credit, encashment, correction, carry forward, lapse) to the
    /// matching leave balance, creating the balance if needed. Adjustments are records of history:
    /// to undo one, add an opposite Correction.
    /// </summary>
    public class LeaveAdjustmentPostOperation : PluginBase
    {
        protected override void ExecuteInternal(LocalContext local)
        {
            var target = local.Target;
            if (target == null || target.LogicalName != LeaveAdjustment.EntityName) return;

            var leave = new LeaveService(local);
            var employeeRef = target.GetAttributeValue<EntityReference>(LeaveAdjustment.Employee);
            var leaveTypeRef = target.GetAttributeValue<EntityReference>(LeaveAdjustment.LeaveType);
            var year = target.GetAttributeValue<string>(LeaveAdjustment.LeaveYear);
            var type = (AdjustmentType)(target.GetAttributeValue<OptionSetValue>(LeaveAdjustment.Type)?.Value ?? (int)AdjustmentType.Correction);
            var days = target.GetAttributeValue<decimal?>(LeaveAdjustment.Days).GetValueOrDefault();
            if (employeeRef == null || leaveTypeRef == null || string.IsNullOrWhiteSpace(year) || days == 0m) return;

            var employee = leave.GetEmployee(employeeRef.Id);
            var balance = leave.FindBalance(employee.Id, leaveTypeRef.Id, year);
            var balanceId = balance?.Id ?? leave.CreateBalance(employee, leaveTypeRef.Id, year, 0m);

            var (column, delta) = LeaveRules.AdjustmentEffect(type, days);
            leave.ApplyToBalance(balanceId, new Dictionary<string, decimal> { [column] = delta });
        }
    }
}
