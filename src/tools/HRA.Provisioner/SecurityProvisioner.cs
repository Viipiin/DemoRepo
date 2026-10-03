using HRA.Provisioner.Model;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace HRA.Provisioner;

/// <summary>Owner teams, security roles and column security profiles. Idempotent.</summary>
public sealed class SecurityProvisioner
{
    private const int RoleComponentType = 20;
    private const int FieldSecurityProfileComponentType = 70;
    private const int Allowed = 4;

    private readonly IOrganizationService _service;
    private readonly HrModel _model;

    public SecurityProvisioner(IOrganizationService service, HrModel model)
    {
        _service = service;
        _model = model;
    }

    public void Run()
    {
        var rootBusinessUnit = _service.RootBusinessUnitId();
        var me = ((WhoAmIResponse)_service.Execute(new WhoAmIRequest())).UserId;

        Log.Step("Owner teams");
        var teams = _model.OwnerTeams.ToDictionary(name => name, name => EnsureTeam(name, rootBusinessUnit, me));
        AddMember(teams["HR"], me, "HR");

        Log.Step("Security roles");
        foreach (var role in _model.Roles) EnsureRole(role, rootBusinessUnit);

        Log.Step("Column security profiles");
        foreach (var profile in _model.FieldSecurityProfiles) EnsureFieldSecurityProfile(profile, teams[profile.TeamName]);
    }

    private Guid EnsureTeam(string name, Guid businessUnit, Guid administrator)
    {
        var existing = _service.FindOne("team", new[] { "teamid" }, ("name", name), ("teamtype", 0));
        if (existing != null)
        {
            Log.Skip($"Team '{name}' exists");
            return existing.Id;
        }
        var id = _service.Create(new Entity("team")
        {
            ["name"] = name,
            ["businessunitid"] = new EntityReference("businessunit", businessUnit),
            ["administratorid"] = new EntityReference("systemuser", administrator),
            ["teamtype"] = new OptionSetValue(0),
            ["description"] = $"HR Automation owner team for {name}.",
        });
        Log.Ok($"Team '{name}' created");
        return id;
    }

    private void AddMember(Guid teamId, Guid userId, string teamName)
    {
        var query = new QueryExpression("teammembership") { ColumnSet = new ColumnSet(false), TopCount = 1 };
        query.Criteria.AddCondition("teamid", ConditionOperator.Equal, teamId);
        query.Criteria.AddCondition("systemuserid", ConditionOperator.Equal, userId);
        if (_service.RetrieveMultiple(query).Entities.Count > 0) return;

        _service.Execute(new AddMembersTeamRequest { TeamId = teamId, MemberIds = new[] { userId } });
        Log.Ok($"You were added to the '{teamName}' team");
    }

    private void EnsureRole(RoleDef role, Guid businessUnit)
    {
        var existing = _service.FindOne("role", new[] { "roleid" }, ("name", role.Name), ("businessunitid", businessUnit));
        Guid roleId;
        if (existing != null)
        {
            roleId = existing.Id;
            Log.Skip($"Role '{role.Name}' exists, updating privileges");
        }
        else
        {
            roleId = _service.Create(new Entity("role")
            {
                ["name"] = role.Name,
                ["businessunitid"] = new EntityReference("businessunit", businessUnit),
                ["description"] = role.Description,
            });
            Log.Ok($"Role '{role.Name}' created");
        }

        _service.Execute(new AddSolutionComponentRequest
        {
            ComponentType = RoleComponentType,
            ComponentId = roleId,
            SolutionUniqueName = Conventions.SolutionUniqueName,
            AddRequiredComponents = false,
        });

        var privileges = new List<RolePrivilege>();
        foreach (var (table, access) in role.Tables)
        {
            var tablePrivileges = ((RetrieveEntityResponse)_service.Execute(new RetrieveEntityRequest
            {
                LogicalName = table,
                EntityFilters = EntityFilters.Privileges,
            })).EntityMetadata.Privileges;

            foreach (var privilege in tablePrivileges)
            {
                if (!Grants(access.Privileges, privilege.PrivilegeType)) continue;
                var depth = access.Depth;
                // Organization-owned tables only support organization-level privileges.
                if (!privilege.CanBeBasic && !privilege.CanBeLocal && !privilege.CanBeDeep) depth = Depth.Organization;
                privileges.Add(new RolePrivilege((int)ToSdkDepth(depth), privilege.PrivilegeId));
            }
        }

        _service.Execute(new AddPrivilegesRoleRequest { RoleId = roleId, Privileges = privileges.ToArray() });
        Log.Ok($"Role '{role.Name}': {privileges.Count} privileges on {role.Tables.Count} tables");
    }

    private static bool Grants(string letters, PrivilegeType type) => type switch
    {
        PrivilegeType.Create => letters.Contains('C'),
        PrivilegeType.Read => letters.Contains('R'),
        PrivilegeType.Write => letters.Contains('W'),
        PrivilegeType.Delete => letters.Contains('D'),
        PrivilegeType.Append => letters.Contains('A'),
        PrivilegeType.AppendTo => letters.Contains('T'),
        PrivilegeType.Assign or PrivilegeType.Share => letters.Contains('S'),
        _ => false,
    };

    private static PrivilegeDepth ToSdkDepth(Depth depth) => depth switch
    {
        Depth.User => PrivilegeDepth.Basic,
        Depth.BusinessUnit => PrivilegeDepth.Local,
        _ => PrivilegeDepth.Global,
    };

    private void EnsureFieldSecurityProfile(FieldSecurityProfileDef profile, Guid teamId)
    {
        var existing = _service.FindOne("fieldsecurityprofile", new[] { "fieldsecurityprofileid" }, ("name", profile.Name));
        Guid profileId;
        if (existing != null)
        {
            profileId = existing.Id;
            Log.Skip($"Profile '{profile.Name}' exists");
        }
        else
        {
            profileId = _service.Create(new Entity("fieldsecurityprofile")
            {
                ["name"] = profile.Name,
                ["description"] = profile.Description,
            });
            Log.Ok($"Profile '{profile.Name}' created");
        }

        _service.Execute(new AddSolutionComponentRequest
        {
            ComponentType = FieldSecurityProfileComponentType,
            ComponentId = profileId,
            SolutionUniqueName = Conventions.SolutionUniqueName,
            AddRequiredComponents = false,
        });

        foreach (var (table, column) in profile.Columns)
        {
            var permission = _service.FindOne("fieldpermission", new[] { "fieldpermissionid" },
                ("fieldsecurityprofileid", profileId), ("entityname", table), ("attributelogicalname", column));
            if (permission != null) continue;

            _service.Create(new Entity("fieldpermission")
            {
                ["fieldsecurityprofileid"] = new EntityReference("fieldsecurityprofile", profileId),
                ["entityname"] = table,
                ["attributelogicalname"] = column,
                ["canread"] = new OptionSetValue(Allowed),
                ["cancreate"] = new OptionSetValue(Allowed),
                ["canupdate"] = new OptionSetValue(Allowed),
            });
            Log.Ok($"Profile '{profile.Name}': full access to {table}.{column}");
        }

        try
        {
            _service.Associate("fieldsecurityprofile", profileId, new Relationship("teamprofiles_association"),
                new EntityReferenceCollection { new EntityReference("team", teamId) });
            Log.Ok($"Profile '{profile.Name}' given to the '{profile.TeamName}' team");
        }
        catch (System.ServiceModel.FaultException<OrganizationServiceFault> ex) when (ex.Detail.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase)
            || ex.Detail.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            Log.Skip($"Profile '{profile.Name}' already given to the '{profile.TeamName}' team");
        }
        catch (System.ServiceModel.FaultException<OrganizationServiceFault> ex)
        {
            Log.Warn($"Couldn't give profile '{profile.Name}' to the '{profile.TeamName}' team automatically: {ex.Detail.Message}");
            Log.Warn($"Do it manually: Power Platform admin center > Viipiin-Dev > Settings > Users + permissions > Column security profiles > {profile.Name} > Teams > Add '{profile.TeamName}'.");
        }
    }
}
