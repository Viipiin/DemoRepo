using Microsoft.Xrm.Sdk;

namespace HRAutomation.Plugins
{
    /// <summary>
    /// P-06. hra_leavebalance Create and Update, PreOperation, synchronous.
    /// Recalculates Available from the balance's columns and names the row, e.g. "EMP-0012 EL 2026-27".
    /// Update step needs a PreImage with all balance columns.
    /// </summary>
    public class LeaveBalancePreOperation : PluginBase
    {
        protected override void ExecuteInternal(LocalContext local)
        {
            var target = local.Target;
            if (target == null || target.LogicalName != LeaveBalance.EntityName) return;

            decimal Value(string column) => local.Merged<decimal?>(column).GetValueOrDefault();
            target[LeaveBalance.Available] = LeaveRules.Available(
                Value(LeaveBalance.Opening), Value(LeaveBalance.Accrued), Value(LeaveBalance.CarriedForward),
                Value(LeaveBalance.Adjusted), Value(LeaveBalance.Taken), Value(LeaveBalance.Encashed),
                Value(LeaveBalance.PendingApproval));

            if (local.IsCreate)
            {
                var employee = target.GetAttributeValue<EntityReference>(LeaveBalance.Employee);
                var leaveType = target.GetAttributeValue<EntityReference>(LeaveBalance.LeaveType);
                if (employee != null && leaveType != null)
                {
                    var number = local.SystemService.Retrieve(Employee.EntityName, employee.Id,
                        new Microsoft.Xrm.Sdk.Query.ColumnSet("hra_employeenumber")).GetAttributeValue<string>("hra_employeenumber");
                    var code = local.SystemService.Retrieve(LeaveType.EntityName, leaveType.Id,
                        new Microsoft.Xrm.Sdk.Query.ColumnSet(LeaveType.Code)).GetAttributeValue<string>(LeaveType.Code);
                    target[LeaveBalance.Name] = $"{number} {code} {target.GetAttributeValue<string>(LeaveBalance.LeaveYear)}".Trim();
                }
            }
        }
    }
}
