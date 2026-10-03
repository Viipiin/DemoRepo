using System.Collections.Generic;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;

namespace HRAutomation.Plugins
{
    /// <summary>
    /// P-04. hra_leaverequest Create and Update, PostOperation, synchronous.
    /// On create: the request is owned like the employee record (manager's user or HR team) and shared with
    /// the employee so they can follow and withdraw it.
    /// On every change: moves days between Pending Approval and Taken on the leave balance.
    /// Update step needs a PreImage with status, number of days, leave type, leave year and employee.
    /// </summary>
    public class LeaveRequestPostOperation : PluginBase
    {
        protected override void ExecuteInternal(LocalContext local)
        {
            var target = local.Target;
            if (target == null || target.LogicalName != LeaveRequest.EntityName) return;

            var leave = new LeaveService(local);
            var requestRef = new EntityReference(LeaveRequest.EntityName, local.Context.PrimaryEntityId);
            var employeeRef = local.Merged<EntityReference>(LeaveRequest.Employee);
            var employee = leave.GetEmployee(employeeRef.Id);

            if (local.IsCreate)
            {
                if (employee.Owner != null)
                {
                    local.SystemService.Update(new Entity(LeaveRequest.EntityName, requestRef.Id) { ["ownerid"] = employee.Owner });
                }
                leave.ShareWithEmployee(requestRef, employee,
                    AccessRights.ReadAccess | AccessRights.WriteAccess | AccessRights.AppendAccess | AccessRights.AppendToAccess);
            }

            var pre = local.PreImage;
            var oldEffect = pre == null
                ? (Pending: 0m, Taken: 0m)
                : LeaveRules.Effect((ApprovalStatus?)pre.GetAttributeValue<OptionSetValue>(LeaveRequest.Status)?.Value,
                    pre.GetAttributeValue<decimal?>(LeaveRequest.NumberOfDays).GetValueOrDefault());
            var newEffect = LeaveRules.Effect((ApprovalStatus?)local.Merged<OptionSetValue>(LeaveRequest.Status)?.Value,
                local.Merged<decimal?>(LeaveRequest.NumberOfDays).GetValueOrDefault());

            var oldKey = pre == null ? null : BalanceKey(pre.GetAttributeValue<EntityReference>(LeaveRequest.Employee),
                pre.GetAttributeValue<EntityReference>(LeaveRequest.LeaveType), pre.GetAttributeValue<string>(LeaveRequest.LeaveYear));
            var newKey = BalanceKey(employeeRef, local.Merged<EntityReference>(LeaveRequest.LeaveType), local.Merged<string>(LeaveRequest.LeaveYear));

            if (oldKey != null && newKey != null && SameKey(oldKey.Value, newKey.Value))
            {
                Apply(leave, employee, newKey.Value, newEffect.Pending - oldEffect.Pending, newEffect.Taken - oldEffect.Taken);
                return;
            }
            if (oldKey != null)
            {
                Apply(leave, leave.GetEmployee(oldKey.Value.Employee.Id), oldKey.Value, -oldEffect.Pending, -oldEffect.Taken);
            }
            if (newKey != null)
            {
                Apply(leave, employee, newKey.Value, newEffect.Pending, newEffect.Taken);
            }
        }

        private static void Apply(LeaveService leave, EmployeeInfo employee, (EntityReference Employee, EntityReference LeaveType, string Year) key,
            decimal pendingDelta, decimal takenDelta)
        {
            if (pendingDelta == 0m && takenDelta == 0m) return;
            if (!leave.GetLeaveType(key.LeaveType.Id).GetAttributeValue<bool>(LeaveType.IsPaid)) return;

            var balance = leave.FindBalance(key.Employee.Id, key.LeaveType.Id, key.Year);
            var balanceId = balance?.Id ?? leave.CreateBalance(employee, key.LeaveType.Id, key.Year, 0m);
            leave.ApplyToBalance(balanceId, new Dictionary<string, decimal>
            {
                [LeaveBalance.PendingApproval] = pendingDelta,
                [LeaveBalance.Taken] = takenDelta,
            });
        }

        private static bool SameKey((EntityReference Employee, EntityReference LeaveType, string Year) a,
            (EntityReference Employee, EntityReference LeaveType, string Year) b) =>
            a.Employee.Id == b.Employee.Id && a.LeaveType.Id == b.LeaveType.Id && a.Year == b.Year;

        private static (EntityReference Employee, EntityReference LeaveType, string Year)? BalanceKey(
            EntityReference employee, EntityReference leaveType, string year) =>
            employee == null || leaveType == null || string.IsNullOrEmpty(year)
                ? ((EntityReference, EntityReference, string)?)null
                : (employee, leaveType, year);
    }
}
