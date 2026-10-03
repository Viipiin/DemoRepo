using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace HRAutomation.Plugins
{
    /// <summary>Employee facts the leave rules need, read in one go.</summary>
    public sealed class EmployeeInfo
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Number { get; set; }
        public EntityReference Owner { get; set; }
        public EntityReference SystemUser { get; set; }
        public EntityReference ReportingManager { get; set; }
        public EntityReference LeavePolicy { get; set; }
        public int? Gender { get; set; }
        public DateTime? DateOfJoining { get; set; }
        public DateTime? ProbationEndDate { get; set; }
        public DateTime? ConfirmationDate { get; set; }
        public int? State { get; set; }
        public WeeklyOffPattern WeeklyOff { get; set; } = WeeklyOffPattern.SaturdayAndSunday;
        public bool IsActive { get; set; }

        public bool OnProbation(DateTime onDate) =>
            ConfirmationDate == null && ProbationEndDate.HasValue && onDate.Date <= ProbationEndDate.Value.Date;
    }

    /// <summary>Dataverse reads and writes shared by the leave plugins and custom APIs. Runs as SYSTEM.</summary>
    public sealed class LeaveService
    {
        private const string LeaveYearStartMonthVariable = "hra_LeaveYearStartMonth";
        private readonly LocalContext _local;
        private readonly IOrganizationService _svc;
        private int? _startMonth;

        public LeaveService(LocalContext local)
        {
            _local = local;
            _svc = local.SystemService;
        }

        public int LeaveYearStartMonth
        {
            get
            {
                if (!_startMonth.HasValue)
                {
                    var raw = _local.GetEnvironmentVariable(LeaveYearStartMonthVariable);
                    _startMonth = int.TryParse(raw, out var month) && month >= 1 && month <= 12 ? month : 4;
                }
                return _startMonth.Value;
            }
        }

        public EmployeeInfo GetEmployee(Guid employeeId)
        {
            var e = _svc.Retrieve(Employee.EntityName, employeeId, new ColumnSet(
                Employee.FullName, "hra_employeenumber", Employee.Owner, Employee.SystemUser, Employee.ReportingManager,
                "hra_leavepolicy", "hra_gender", Employee.DateOfJoining, Employee.ProbationEndDate, "hra_confirmationdate",
                Employee.Location, Employee.StateCode));

            var info = new EmployeeInfo
            {
                Id = employeeId,
                Name = e.GetAttributeValue<string>(Employee.FullName),
                Number = e.GetAttributeValue<string>("hra_employeenumber"),
                Owner = e.GetAttributeValue<EntityReference>(Employee.Owner),
                SystemUser = e.GetAttributeValue<EntityReference>(Employee.SystemUser),
                ReportingManager = e.GetAttributeValue<EntityReference>(Employee.ReportingManager),
                LeavePolicy = e.GetAttributeValue<EntityReference>("hra_leavepolicy"),
                Gender = e.GetAttributeValue<OptionSetValue>("hra_gender")?.Value,
                DateOfJoining = e.GetAttributeValue<DateTime?>(Employee.DateOfJoining),
                ProbationEndDate = e.GetAttributeValue<DateTime?>(Employee.ProbationEndDate),
                ConfirmationDate = e.GetAttributeValue<DateTime?>("hra_confirmationdate"),
                IsActive = e.GetAttributeValue<OptionSetValue>(Employee.StateCode)?.Value == 0,
            };

            var location = e.GetAttributeValue<EntityReference>(Employee.Location);
            if (location != null)
            {
                var l = _svc.Retrieve(Location.EntityName, location.Id, new ColumnSet(Location.State, Location.WeeklyOffPattern));
                info.State = l.GetAttributeValue<OptionSetValue>(Location.State)?.Value;
                var pattern = l.GetAttributeValue<OptionSetValue>(Location.WeeklyOffPattern)?.Value;
                if (pattern.HasValue) info.WeeklyOff = (WeeklyOffPattern)pattern.Value;
            }
            return info;
        }

        public Entity GetLeaveType(Guid leaveTypeId) =>
            _svc.Retrieve(LeaveType.EntityName, leaveTypeId, new ColumnSet(true));

        public Entity GetPolicyLine(Guid policyId, Guid leaveTypeId)
        {
            var query = new QueryExpression(LeavePolicyLine.EntityName) { ColumnSet = new ColumnSet(true), TopCount = 1 };
            query.Criteria.AddCondition(LeavePolicyLine.LeavePolicy, ConditionOperator.Equal, policyId);
            query.Criteria.AddCondition(LeavePolicyLine.LeaveType, ConditionOperator.Equal, leaveTypeId);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            return _svc.RetrieveMultiple(query).Entities.FirstOrDefault();
        }

        public List<Entity> GetPolicyLines(Guid policyId)
        {
            var query = new QueryExpression(LeavePolicyLine.EntityName) { ColumnSet = new ColumnSet(true) };
            query.Criteria.AddCondition(LeavePolicyLine.LeavePolicy, ConditionOperator.Equal, policyId);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            return _svc.RetrieveMultiple(query).Entities.ToList();
        }

        public EntityReference GetDefaultPolicy()
        {
            var query = new QueryExpression(LeavePolicy.EntityName) { ColumnSet = new ColumnSet(false), TopCount = 1 };
            query.Criteria.AddCondition(LeavePolicy.IsDefault, ConditionOperator.Equal, true);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            return _svc.RetrieveMultiple(query).Entities.FirstOrDefault()?.ToEntityReference();
        }

        /// <summary>National and state holidays (not optional ones) for a state between two dates.</summary>
        public ISet<DateTime> GetHolidays(int? state, DateTime from, DateTime to)
        {
            var result = new HashSet<DateTime>();
            if (!state.HasValue) return result;

            var query = new QueryExpression(Holiday.EntityName) { ColumnSet = new ColumnSet(Holiday.Date) };
            query.Criteria.AddCondition(Holiday.Date, ConditionOperator.OnOrAfter, from.Date);
            query.Criteria.AddCondition(Holiday.Date, ConditionOperator.OnOrBefore, to.Date);
            query.Criteria.AddCondition(Holiday.Type, ConditionOperator.NotEqual, (int)HolidayType.Optional);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            var calendar = query.AddLink(HolidayCalendar.EntityName, Holiday.Calendar, "hra_holidaycalendarid");
            calendar.LinkCriteria.AddCondition(HolidayCalendar.State, ConditionOperator.Equal, state.Value);

            foreach (var holiday in _svc.RetrieveMultiple(query).Entities)
            {
                var date = holiday.GetAttributeValue<DateTime?>(Holiday.Date);
                if (date.HasValue) result.Add(date.Value.Date);
            }
            return result;
        }

        public Entity FindBalance(Guid employeeId, Guid leaveTypeId, string leaveYear)
        {
            var query = new QueryExpression(LeaveBalance.EntityName) { ColumnSet = new ColumnSet(true), TopCount = 1 };
            query.Criteria.AddCondition(LeaveBalance.Employee, ConditionOperator.Equal, employeeId);
            query.Criteria.AddCondition(LeaveBalance.LeaveType, ConditionOperator.Equal, leaveTypeId);
            query.Criteria.AddCondition(LeaveBalance.LeaveYear, ConditionOperator.Equal, leaveYear);
            return _svc.RetrieveMultiple(query).Entities.FirstOrDefault();
        }

        /// <summary>
        /// Creates a balance row owned like the employee record, and shares it (read) with the employee's own user.
        /// </summary>
        public Guid CreateBalance(EmployeeInfo employee, Guid leaveTypeId, string leaveYear, decimal opening)
        {
            var balance = new Entity(LeaveBalance.EntityName)
            {
                [LeaveBalance.Employee] = new EntityReference(Employee.EntityName, employee.Id),
                [LeaveBalance.LeaveType] = new EntityReference(LeaveType.EntityName, leaveTypeId),
                [LeaveBalance.LeaveYear] = leaveYear,
                [LeaveBalance.Opening] = opening,
            };
            if (employee.Owner != null) balance["ownerid"] = employee.Owner;
            var id = _svc.Create(balance);
            ShareWithEmployee(new EntityReference(LeaveBalance.EntityName, id), employee, AccessRights.ReadAccess);
            return id;
        }

        /// <summary>Adds the given amounts to a balance's columns. The balance plugin recalculates Available.</summary>
        public void ApplyToBalance(Guid balanceId, IDictionary<string, decimal> deltas)
        {
            var nonZero = deltas.Where(d => d.Value != 0m).ToList();
            if (nonZero.Count == 0) return;

            var current = _svc.Retrieve(LeaveBalance.EntityName, balanceId, new ColumnSet(nonZero.Select(d => d.Key).ToArray()));
            var update = new Entity(LeaveBalance.EntityName, balanceId);
            foreach (var delta in nonZero)
            {
                update[delta.Key] = current.GetAttributeValue<decimal?>(delta.Key).GetValueOrDefault() + delta.Value;
            }
            _svc.Update(update);
        }

        public void ShareWithEmployee(EntityReference record, EmployeeInfo employee, AccessRights rights)
        {
            if (employee.SystemUser == null) return;
            if (employee.Owner != null && employee.Owner.LogicalName == "systemuser" && employee.Owner.Id == employee.SystemUser.Id) return;
            _svc.Execute(new GrantAccessRequest
            {
                Target = record,
                PrincipalAccess = new PrincipalAccess
                {
                    Principal = new EntityReference("systemuser", employee.SystemUser.Id),
                    AccessMask = rights,
                },
            });
        }

        /// <summary>System administrators and members of the HR team can act on any leave request.</summary>
        public bool IsHrOrAdmin(Guid userId)
        {
            var team = new QueryExpression("teammembership") { ColumnSet = new ColumnSet(false), TopCount = 1 };
            team.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
            team.AddLink("team", "teamid", "teamid").LinkCriteria.AddCondition("name", ConditionOperator.Equal, Settings.HrTeamName);
            if (_svc.RetrieveMultiple(team).Entities.Count > 0) return true;

            var admin = new QueryExpression("systemuserroles") { ColumnSet = new ColumnSet(false), TopCount = 1 };
            admin.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
            admin.AddLink("role", "roleid", "roleid").LinkCriteria.AddCondition("name", ConditionOperator.Equal, "System Administrator");
            return _svc.RetrieveMultiple(admin).Entities.Count > 0;
        }

        public Guid? SystemUserOf(Guid employeeId) =>
            _svc.Retrieve(Employee.EntityName, employeeId, new ColumnSet(Employee.SystemUser))
                .GetAttributeValue<EntityReference>(Employee.SystemUser)?.Id;

        /// <summary>Other submitted or approved requests of the employee that overlap the dates.</summary>
        public bool HasOverlap(Guid employeeId, DateTime from, DateTime to, Guid excludeId)
        {
            var query = new QueryExpression(LeaveRequest.EntityName) { ColumnSet = new ColumnSet(false), TopCount = 1 };
            query.Criteria.AddCondition(LeaveRequest.Employee, ConditionOperator.Equal, employeeId);
            query.Criteria.AddCondition(LeaveRequest.FromDate, ConditionOperator.OnOrBefore, to.Date);
            query.Criteria.AddCondition(LeaveRequest.ToDate, ConditionOperator.OnOrAfter, from.Date);
            query.Criteria.AddCondition(LeaveRequest.Status, ConditionOperator.In,
                (int)ApprovalStatus.Submitted, (int)ApprovalStatus.Approved);
            if (excludeId != Guid.Empty) query.Criteria.AddCondition("hra_leaverequestid", ConditionOperator.NotEqual, excludeId);
            return _svc.RetrieveMultiple(query).Entities.Count > 0;
        }

        /// <summary>
        /// Creates missing balances for the leave year from each employee's policy. Upfront entitlement is
        /// prorated for joiners; monthly and quarterly entitlement starts at 0 and comes from accrual.
        /// Returns the number of balances created.
        /// </summary>
        public int InitializeBalances(Guid? employeeId, string leaveYear)
        {
            var start = LeaveYearStart(leaveYear);
            var defaultPolicy = GetDefaultPolicy();
            var created = 0;
            foreach (var id in employeeId.HasValue ? new[] { employeeId.Value } : ActiveEmployeeIds())
            {
                var employee = GetEmployee(id);
                if (!employee.IsActive) continue;
                var policy = employee.LeavePolicy ?? defaultPolicy;
                if (policy == null) continue;

                foreach (var line in GetPolicyLines(policy.Id))
                {
                    var leaveTypeRef = line.GetAttributeValue<EntityReference>(LeavePolicyLine.LeaveType);
                    if (!AppliesToGender(GetLeaveType(leaveTypeRef.Id), employee.Gender)) continue;
                    if (FindBalance(employee.Id, leaveTypeRef.Id, leaveYear) != null) continue;

                    var annual = line.GetAttributeValue<decimal?>(LeavePolicyLine.AnnualEntitlement).GetValueOrDefault();
                    var frequency = (AccrualFrequency)(line.GetAttributeValue<OptionSetValue>(LeavePolicyLine.AccrualFrequency)?.Value
                        ?? (int)AccrualFrequency.Upfront);
                    var opening = 0m;
                    if (frequency == AccrualFrequency.Upfront)
                    {
                        var prorate = line.GetAttributeValue<bool>(LeavePolicyLine.ProrateOnJoining);
                        opening = prorate && employee.DateOfJoining.HasValue
                            ? LeaveRules.Prorate(annual, employee.DateOfJoining.Value, start)
                            : annual;
                    }
                    CreateBalance(employee, leaveTypeRef.Id, leaveYear, opening);
                    created++;
                }
            }
            return created;
        }

        /// <summary>
        /// Adds monthly or quarterly accrual for a period (yyyy-MM) to every balance of that leave year that
        /// hasn't had it yet. Employees who join after the period don't accrue. Returns balances updated.
        /// </summary>
        public int RunAccrual(DateTime periodMonth)
        {
            var leaveYear = LeaveRules.LeaveYear(periodMonth, LeaveYearStartMonth);
            var monthOfYear = LeaveRules.MonthOfLeaveYear(periodMonth, LeaveYearStartMonth);
            var period = periodMonth.ToString("yyyy-MM");
            var periodEnd = new DateTime(periodMonth.Year, periodMonth.Month, 1).AddMonths(1).AddDays(-1);
            var defaultPolicy = GetDefaultPolicy();
            var updated = 0;

            // Caches keep this to roughly one update per balance, well inside the 2-minute plugin limit.
            var employees = new Dictionary<Guid, EmployeeInfo>();
            var lines = new Dictionary<(Guid Policy, Guid LeaveType), Entity>();

            var query = new QueryExpression(LeaveBalance.EntityName) { ColumnSet = new ColumnSet(true) };
            query.Criteria.AddCondition(LeaveBalance.LeaveYear, ConditionOperator.Equal, leaveYear);
            query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
            foreach (var balance in RetrieveAll(query).ToList())
            {
                if (balance.GetAttributeValue<string>(LeaveBalance.LastAccrualPeriod) == period) continue;

                var employeeId = balance.GetAttributeValue<EntityReference>(LeaveBalance.Employee).Id;
                if (!employees.TryGetValue(employeeId, out var employee))
                {
                    employee = employees[employeeId] = GetEmployee(employeeId);
                }
                if (!employee.IsActive || (employee.DateOfJoining.HasValue && employee.DateOfJoining.Value.Date > periodEnd)) continue;
                var policy = employee.LeavePolicy ?? defaultPolicy;
                if (policy == null) continue;
                var lineKey = (policy.Id, balance.GetAttributeValue<EntityReference>(LeaveBalance.LeaveType).Id);
                if (!lines.TryGetValue(lineKey, out var line))
                {
                    line = lines[lineKey] = GetPolicyLine(lineKey.Item1, lineKey.Item2);
                }
                if (line == null) continue;

                var frequency = (AccrualFrequency)(line.GetAttributeValue<OptionSetValue>(LeavePolicyLine.AccrualFrequency)?.Value
                    ?? (int)AccrualFrequency.Upfront);
                if (frequency == AccrualFrequency.Upfront) continue;

                var amount = LeaveRules.AccrualAmount(
                    frequency,
                    line.GetAttributeValue<decimal?>(LeavePolicyLine.AnnualEntitlement).GetValueOrDefault(),
                    monthOfYear,
                    balance.GetAttributeValue<decimal?>(LeaveBalance.Available).GetValueOrDefault(),
                    line.GetAttributeValue<decimal?>(LeavePolicyLine.MaxBalance).GetValueOrDefault());

                _svc.Update(new Entity(LeaveBalance.EntityName, balance.Id)
                {
                    [LeaveBalance.Accrued] = balance.GetAttributeValue<decimal?>(LeaveBalance.Accrued).GetValueOrDefault() + amount,
                    [LeaveBalance.LastAccrualPeriod] = period,
                });
                updated++;
            }
            return updated;
        }

        public string CurrentLeaveYear() => LeaveRules.LeaveYear(DateTime.UtcNow.AddHours(5.5), LeaveYearStartMonth); // IST

        public DateTime LeaveYearStart(string leaveYear) =>
            new DateTime(int.Parse(leaveYear.Substring(0, 4)), LeaveYearStartMonth, 1);

        public static bool AppliesToGender(Entity leaveType, int? gender)
        {
            var applicable = (ApplicableGender)(leaveType.GetAttributeValue<OptionSetValue>(LeaveType.ApplicableGender)?.Value
                ?? (int)ApplicableGender.All);
            switch (applicable)
            {
                case ApplicableGender.Female: return gender == GenderValue.Female;
                case ApplicableGender.Male: return gender == GenderValue.Male;
                default: return true;
            }
        }

        private IEnumerable<Guid> ActiveEmployeeIds()
        {
            var query = new QueryExpression(Employee.EntityName) { ColumnSet = new ColumnSet(false) };
            query.Criteria.AddCondition(Employee.StateCode, ConditionOperator.Equal, 0);
            return RetrieveAll(query).Select(e => e.Id).ToList();
        }

        private IEnumerable<Entity> RetrieveAll(QueryExpression query)
        {
            query.PageInfo = new PagingInfo { Count = 500, PageNumber = 1 };
            while (true)
            {
                var page = _svc.RetrieveMultiple(query);
                foreach (var entity in page.Entities) yield return entity;
                if (!page.MoreRecords) yield break;
                query.PageInfo.PageNumber++;
                query.PageInfo.PagingCookie = page.PagingCookie;
            }
        }
    }
}
