using System;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace HRAutomation.Plugins
{
    /// <summary>
    /// P-02. hra_employee Create and Update, PostOperation (stage 40), synchronous.
    /// - Ownership follows the reporting line: the record is owned by the reporting manager's user,
    ///   or by the HR team when the manager has no Dataverse user.
    /// - Shares the record (read) with the employee's own user, so they can see their profile.
    /// - Keeps the Dataverse manager hierarchy (systemuser.parentsystemuserid) in sync.
    /// - Maintains Is People Manager on the old and new manager.
    /// - Writes an Employment History row on joining and on job changes.
    /// Update step needs a PreImage named "PreImage" with the job fields and ownerid.
    /// </summary>
    public class EmployeePostOperation : PluginBase
    {
        protected override void ExecuteInternal(LocalContext local)
        {
            var target = local.Target;
            if (target == null || target.LogicalName != Employee.EntityName) return;

            var employeeId = local.Context.PrimaryEntityId;
            var svc = local.SystemService;

            var manager = local.Merged<EntityReference>(Employee.ReportingManager);
            var user = local.Merged<EntityReference>(Employee.SystemUser);
            var managerChanged = local.Changed(Employee.ReportingManager);
            var userChanged = local.Changed(Employee.SystemUser);

            var managerUser = manager != null ? GetSystemUser(svc, manager.Id) : null;

            // 1. Ownership.
            EntityReference owner = local.Merged<EntityReference>(Employee.Owner);
            if (local.IsCreate || managerChanged)
            {
                var newOwner = managerUser ?? GetHrTeam(svc);
                if (newOwner != null && (owner == null || owner.Id != newOwner.Id))
                {
                    svc.Update(new Entity(Employee.EntityName, employeeId) { [Employee.Owner] = newOwner });
                    owner = newOwner;
                    local.Trace.Trace("Owner set to {0} {1}", newOwner.LogicalName, newOwner.Id);
                }
            }

            // 2. Share with the employee's own user.
            if (user != null && (local.IsCreate || userChanged) && (owner == null || owner.Id != user.Id))
            {
                svc.Execute(new GrantAccessRequest
                {
                    Target = new EntityReference(Employee.EntityName, employeeId),
                    PrincipalAccess = new PrincipalAccess
                    {
                        Principal = user,
                        AccessMask = AccessRights.ReadAccess | AccessRights.AppendToAccess
                    }
                });
            }

            // 3. Manager hierarchy for licensed users, and ownership of direct reports when the user link changes.
            if (user != null && (local.IsCreate || managerChanged || userChanged))
            {
                SetUserManager(svc, user.Id, managerUser?.Id);
            }
            if (userChanged)
            {
                // People who report to this employee move to the new user (or to the HR team when it was removed).
                var reportOwner = user ?? GetHrTeam(svc);
                foreach (var report in GetDirectReports(svc, employeeId))
                {
                    if (reportOwner != null)
                    {
                        svc.Update(new Entity(Employee.EntityName, report.Id) { [Employee.Owner] = reportOwner });
                    }
                    var reportUser = report.GetAttributeValue<EntityReference>(Employee.SystemUser);
                    if (reportUser != null) SetUserManager(svc, reportUser.Id, user?.Id);
                }
            }

            // 4. Is People Manager.
            if (managerChanged)
            {
                if (manager != null)
                {
                    svc.Update(new Entity(Employee.EntityName, manager.Id) { [Employee.IsPeopleManager] = true });
                }
                var oldManager = local.PreImage?.GetAttributeValue<EntityReference>(Employee.ReportingManager);
                if (!local.IsCreate && oldManager != null && (manager == null || oldManager.Id != manager.Id))
                {
                    var stillManages = CountActiveReports(svc, oldManager.Id) > 0;
                    svc.Update(new Entity(Employee.EntityName, oldManager.Id) { [Employee.IsPeopleManager] = stillManages });
                }
            }

            // 5. Employment history.
            WriteHistory(local, employeeId, owner, managerChanged);
        }

        private static void WriteHistory(LocalContext local, Guid employeeId, EntityReference owner, bool managerChanged)
        {
            int? changeType;
            if (local.IsCreate)
            {
                changeType = ChangeType.Joining;
            }
            else
            {
                changeType = HrRules.ClassifyChange(
                    local.Changed(Employee.Department),
                    local.Changed(Employee.Designation),
                    local.Changed(Employee.Location),
                    managerChanged,
                    local.Changed(Employee.AnnualCtc));
            }
            if (!changeType.HasValue) return;

            var pre = local.PreImage;
            var history = new Entity(EmploymentHistory.EntityName)
            {
                [EmploymentHistory.Employee] = new EntityReference(Employee.EntityName, employeeId),
                [EmploymentHistory.ChangeType] = new OptionSetValue(changeType.Value),
                [EmploymentHistory.EffectiveDate] = local.IsCreate
                    ? local.Merged<DateTime?>(Employee.DateOfJoining) ?? DateTime.UtcNow.Date
                    : DateTime.UtcNow.Date,
                [EmploymentHistory.ToDepartment] = local.Merged<EntityReference>(Employee.Department),
                [EmploymentHistory.ToDesignation] = local.Merged<EntityReference>(Employee.Designation),
                [EmploymentHistory.ToLocation] = local.Merged<EntityReference>(Employee.Location),
                [EmploymentHistory.ToManager] = local.Merged<EntityReference>(Employee.ReportingManager),
                [EmploymentHistory.ToCtc] = local.Merged<Money>(Employee.AnnualCtc),
            };
            if (pre != null)
            {
                history[EmploymentHistory.FromDepartment] = pre.GetAttributeValue<EntityReference>(Employee.Department);
                history[EmploymentHistory.FromDesignation] = pre.GetAttributeValue<EntityReference>(Employee.Designation);
                history[EmploymentHistory.FromLocation] = pre.GetAttributeValue<EntityReference>(Employee.Location);
                history[EmploymentHistory.FromManager] = pre.GetAttributeValue<EntityReference>(Employee.ReportingManager);
                history[EmploymentHistory.FromCtc] = pre.GetAttributeValue<Money>(Employee.AnnualCtc);
            }
            if (owner != null) history["ownerid"] = owner;

            local.SystemService.Create(history);
        }

        private static EntityReference GetSystemUser(IOrganizationService svc, Guid employeeId)
        {
            var employee = svc.Retrieve(Employee.EntityName, employeeId, new ColumnSet(Employee.SystemUser));
            var user = employee.GetAttributeValue<EntityReference>(Employee.SystemUser);
            return user != null ? new EntityReference("systemuser", user.Id) : null;
        }

        private static EntityReference GetHrTeam(IOrganizationService svc)
        {
            var query = new QueryExpression("team") { ColumnSet = new ColumnSet("teamid"), TopCount = 1 };
            query.Criteria.AddCondition("name", ConditionOperator.Equal, Settings.HrTeamName);
            query.Criteria.AddCondition("teamtype", ConditionOperator.Equal, 0); // Owner team
            var teams = svc.RetrieveMultiple(query).Entities;
            return teams.Count > 0 ? teams[0].ToEntityReference() : null;
        }

        private static void SetUserManager(IOrganizationService svc, Guid userId, Guid? managerUserId)
        {
            if (managerUserId == userId) return; // A user can't be their own manager.
            var current = svc.Retrieve("systemuser", userId, new ColumnSet("parentsystemuserid"))
                .GetAttributeValue<EntityReference>("parentsystemuserid");
            if (current?.Id == managerUserId || (current == null && managerUserId == null)) return;

            svc.Update(new Entity("systemuser", userId)
            {
                ["parentsystemuserid"] = managerUserId.HasValue ? new EntityReference("systemuser", managerUserId.Value) : null
            });
        }

        private static DataCollection<Entity> GetDirectReports(IOrganizationService svc, Guid employeeId)
        {
            var query = new QueryExpression(Employee.EntityName) { ColumnSet = new ColumnSet(Employee.SystemUser) };
            query.Criteria.AddCondition(Employee.ReportingManager, ConditionOperator.Equal, employeeId);
            return svc.RetrieveMultiple(query).Entities;
        }

        private static int CountActiveReports(IOrganizationService svc, Guid managerId)
        {
            var query = new QueryExpression(Employee.EntityName) { ColumnSet = new ColumnSet(false), TopCount = 1 };
            query.Criteria.AddCondition(Employee.ReportingManager, ConditionOperator.Equal, managerId);
            query.Criteria.AddCondition(Employee.StateCode, ConditionOperator.Equal, 0);
            return svc.RetrieveMultiple(query).Entities.Count;
        }
    }
}
