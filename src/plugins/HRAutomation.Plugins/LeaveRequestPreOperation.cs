using System;
using Microsoft.Xrm.Sdk;

namespace HRAutomation.Plugins
{
    /// <summary>
    /// P-03. hra_leaverequest Create and Update, PreOperation, synchronous.
    /// Defaults the approver and status, calculates days and leave year, and enforces the leave rules:
    /// dates, gender, probation, policy, half days, maximum length, overlaps, balance and who may approve.
    /// Update step needs a PreImage with the request's dates, type, employee, status and number of days.
    /// </summary>
    public class LeaveRequestPreOperation : PluginBase
    {
        protected override void ExecuteInternal(LocalContext local)
        {
            var target = local.Target;
            if (target == null || target.LogicalName != LeaveRequest.EntityName) return;

            var leave = new LeaveService(local);
            var employeeRef = local.Merged<EntityReference>(LeaveRequest.Employee)
                ?? throw new InvalidPluginExecutionException("Choose the employee the leave is for.");
            var leaveTypeRef = local.Merged<EntityReference>(LeaveRequest.LeaveType)
                ?? throw new InvalidPluginExecutionException("Choose a leave type.");
            var employee = leave.GetEmployee(employeeRef.Id);

            var oldStatus = (ApprovalStatus?)local.PreImage?.GetAttributeValue<OptionSetValue>(LeaveRequest.Status)?.Value;
            if (local.IsCreate && !target.Contains(LeaveRequest.Status))
            {
                target[LeaveRequest.Status] = new OptionSetValue((int)ApprovalStatus.Submitted);
            }
            var status = (ApprovalStatus)(local.Merged<OptionSetValue>(LeaveRequest.Status)?.Value ?? (int)ApprovalStatus.Submitted);

            if (local.IsCreate && local.Merged<EntityReference>(LeaveRequest.Approver) == null && employee.ReportingManager != null)
            {
                target[LeaveRequest.Approver] = employee.ReportingManager;
            }

            CheckStatusChange(local, leave, employee, oldStatus, status);

            var detailsChanged = local.IsCreate
                || target.Contains(LeaveRequest.FromDate) || target.Contains(LeaveRequest.ToDate)
                || target.Contains(LeaveRequest.FirstDayHalf) || target.Contains(LeaveRequest.LastDayHalf)
                || target.Contains(LeaveRequest.LeaveType) || target.Contains(LeaveRequest.Employee);
            if (!detailsChanged && !(oldStatus.HasValue && !Counts(oldStatus) && Counts(status))) return;

            if (!local.IsCreate && detailsChanged && oldStatus.HasValue && oldStatus != ApprovalStatus.Draft && oldStatus != ApprovalStatus.Submitted)
            {
                throw new InvalidPluginExecutionException("Dates and leave type can't be changed after a request is approved, rejected or closed. Cancel it and apply again.");
            }

            var from = (local.Merged<DateTime?>(LeaveRequest.FromDate) ?? throw new InvalidPluginExecutionException("Enter the From date.")).Date;
            var to = (local.Merged<DateTime?>(LeaveRequest.ToDate) ?? throw new InvalidPluginExecutionException("Enter the To date.")).Date;
            if (to < from) throw new InvalidPluginExecutionException("The To date can't be before the From date.");

            var startMonth = leave.LeaveYearStartMonth;
            var leaveYear = LeaveRules.LeaveYear(from, startMonth);
            if (LeaveRules.LeaveYear(to, startMonth) != leaveYear)
            {
                throw new InvalidPluginExecutionException($"The dates cross into the next leave year. Split it into two requests, ending and starting at the leave year boundary ({LeaveRules.LeaveYearStart(to, startMonth):dd-MM-yyyy}).");
            }

            var leaveType = leave.GetLeaveType(leaveTypeRef.Id);
            var typeName = leaveType.GetAttributeValue<string>("hra_name");
            var firstHalf = local.Merged<bool?>(LeaveRequest.FirstDayHalf) ?? false;
            var lastHalf = local.Merged<bool?>(LeaveRequest.LastDayHalf) ?? false;
            if ((firstHalf || lastHalf) && !leaveType.GetAttributeValue<bool>(LeaveType.AllowHalfDay))
            {
                throw new InvalidPluginExecutionException($"{typeName} can't be taken as a half day.");
            }
            if (!LeaveService.AppliesToGender(leaveType, employee.Gender))
            {
                throw new InvalidPluginExecutionException($"{typeName} doesn't apply to this employee.");
            }

            var countAll = leaveType.GetAttributeValue<bool>(LeaveType.CountSandwichedHolidays);
            var holidays = countAll ? new System.Collections.Generic.HashSet<DateTime>() : leave.GetHolidays(employee.State, from, to);
            var days = LeaveRules.CountLeaveDays(from, to, firstHalf, lastHalf, employee.WeeklyOff, holidays, countAll);
            if (days <= 0m)
            {
                throw new InvalidPluginExecutionException("The selected dates are all weekly offs or holidays, so there's nothing to apply for.");
            }
            target[LeaveRequest.NumberOfDays] = days;
            target[LeaveRequest.LeaveYear] = leaveYear;

            var maxDays = leaveType.GetAttributeValue<int?>(LeaveType.MaxConsecutiveDays);
            if (maxDays.HasValue && maxDays.Value > 0 && days > maxDays.Value)
            {
                throw new InvalidPluginExecutionException($"{typeName} can be at most {maxDays.Value} day(s) at a time. This request is {days} day(s).");
            }

            if (!Counts(status)) return;

            if (leave.HasOverlap(employee.Id, from, to, local.Context.PrimaryEntityId))
            {
                throw new InvalidPluginExecutionException("These dates overlap another submitted or approved leave request.");
            }

            if (!leaveType.GetAttributeValue<bool>(LeaveType.IsPaid)) return; // Loss of pay needs no balance.

            var policy = employee.LeavePolicy ?? leave.GetDefaultPolicy();
            var line = policy == null ? null : leave.GetPolicyLine(policy.Id, leaveTypeRef.Id);
            if (line == null)
            {
                throw new InvalidPluginExecutionException($"{typeName} isn't part of this employee's leave policy.");
            }
            if (!line.GetAttributeValue<bool>(LeavePolicyLine.AvailableDuringProbation) && employee.OnProbation(from))
            {
                throw new InvalidPluginExecutionException($"{typeName} isn't available during probation (ends {employee.ProbationEndDate:dd-MM-yyyy}).");
            }

            var balance = leave.FindBalance(employee.Id, leaveTypeRef.Id, leaveYear);
            var available = balance?.GetAttributeValue<decimal?>(LeaveBalance.Available) ?? 0m;
            // This request's own earlier days are already counted in the balance; add them back before comparing.
            var pre = local.PreImage;
            if (pre != null && Counts(oldStatus)
                && pre.GetAttributeValue<EntityReference>(LeaveRequest.LeaveType)?.Id == leaveTypeRef.Id
                && pre.GetAttributeValue<string>(LeaveRequest.LeaveYear) == leaveYear
                && pre.GetAttributeValue<EntityReference>(LeaveRequest.Employee)?.Id == employee.Id)
            {
                available += pre.GetAttributeValue<decimal?>(LeaveRequest.NumberOfDays).GetValueOrDefault();
            }
            if (days > available)
            {
                throw new InvalidPluginExecutionException($"Not enough {typeName} balance for {leaveYear}: {available} day(s) available, {days} requested.");
            }
        }

        private static void CheckStatusChange(LocalContext local, LeaveService leave, EmployeeInfo employee,
            ApprovalStatus? oldStatus, ApprovalStatus status)
        {
            if (local.IsCreate)
            {
                if (status != ApprovalStatus.Draft && status != ApprovalStatus.Submitted)
                {
                    throw new InvalidPluginExecutionException("A new leave request must be Draft or Submitted.");
                }
                return;
            }
            if (!oldStatus.HasValue || oldStatus == status || !local.Target.Contains(LeaveRequest.Status)) return;

            if (!LeaveRules.IsAllowedTransition(oldStatus.Value, status))
            {
                throw new InvalidPluginExecutionException($"A {oldStatus} request can't be changed to {status}.");
            }

            var caller = local.Context.InitiatingUserId;
            var isHr = leave.IsHrOrAdmin(caller);
            if (status == ApprovalStatus.Approved || status == ApprovalStatus.Rejected)
            {
                if (employee.SystemUser?.Id == caller && !isHr)
                {
                    throw new InvalidPluginExecutionException("You can't approve or reject your own leave.");
                }
                var approver = local.Merged<EntityReference>(LeaveRequest.Approver);
                var approverUser = approver == null ? null : leave.SystemUserOf(approver.Id);
                if (approverUser != caller && !isHr)
                {
                    throw new InvalidPluginExecutionException("Only the approver or HR can approve or reject this request.");
                }
                local.Target[LeaveRequest.ActionedOn] = DateTime.UtcNow;
            }
        }

        private static bool Counts(ApprovalStatus? status) =>
            status == ApprovalStatus.Submitted || status == ApprovalStatus.Approved;
    }
}
