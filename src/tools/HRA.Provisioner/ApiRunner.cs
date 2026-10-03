using System.Globalization;
using HRA.Provisioner.Model;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace HRA.Provisioner;

/// <summary>Calls the Custom APIs defined in model/custom-apis.json.</summary>
public sealed class ApiRunner
{
    private readonly IOrganizationService _service;
    private readonly HrModel _model;

    public ApiRunner(IOrganizationService service, HrModel model)
    {
        _service = service;
        _model = model;
    }

    public void Run(string name, IDictionary<string, string> parameters)
    {
        var api = _model.CustomApis.FirstOrDefault(a => a.UniqueName.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"{name} is not in model/custom-apis.json.");
        Log.Step($"Running {api.UniqueName}");
        var response = Execute(api, parameters);
        foreach (var output in api.Responses)
        {
            Log.Ok($"{output.Name}: {(response.Results.Contains(output.Name) ? response.Results[output.Name] : "(none)")}");
        }
    }

    /// <summary>
    /// Calls hra_InitializeLeaveBalances once per active employee, so each call stays well inside the
    /// 2-minute plugin limit.
    /// </summary>
    public void InitializeBalancesForAll(IDictionary<string, string> parameters)
    {
        var api = _model.CustomApis.FirstOrDefault(a => a.UniqueName == "hra_InitializeLeaveBalances")
            ?? throw new ArgumentException("hra_InitializeLeaveBalances is not in model/custom-apis.json.");

        var query = new QueryExpression("hra_employee") { ColumnSet = new ColumnSet("hra_fullname") };
        query.Criteria.AddCondition("statecode", ConditionOperator.Equal, 0);
        query.AddOrder("hra_fullname", OrderType.Ascending);
        var employees = _service.RetrieveMultiple(query).Entities;

        Log.Step($"Initializing leave balances for {employees.Count} active employees");
        var total = 0;
        foreach (var employee in employees)
        {
            var values = new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase)
            {
                ["EmployeeId"] = employee.Id.ToString(),
            };
            var created = (int)Execute(api, values).Results["Created"];
            total += created;
            if (created > 0) Log.Ok($"{employee.GetAttributeValue<string>("hra_fullname")}: {created} balance(s)");
            else Log.Skip($"{employee.GetAttributeValue<string>("hra_fullname")}: already has balances");
        }
        Log.Ok($"{total} balance(s) created");
    }

    private OrganizationResponse Execute(CustomApiDef api, IDictionary<string, string> parameters)
    {
        var request = new OrganizationRequest(api.UniqueName);
        foreach (var (name, text) in parameters)
        {
            var field = api.Parameters.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"{api.UniqueName} has no parameter {name}.");
            request[field.Name] = field.Type switch
            {
                "Guid" => Guid.Parse(text),
                "Integer" => int.Parse(text, CultureInfo.InvariantCulture),
                "Decimal" => decimal.Parse(text, CultureInfo.InvariantCulture),
                "Boolean" => bool.Parse(text),
                "DateTime" => DateTime.Parse(text, CultureInfo.InvariantCulture),
                "Money" => new Money(decimal.Parse(text, CultureInfo.InvariantCulture)),
                "StringArray" => text.Split(','),
                _ => text,
            };
        }
        foreach (var missing in api.Parameters.Where(p => !p.Optional && !request.Parameters.ContainsKey(p.Name)))
        {
            throw new ArgumentException($"{api.UniqueName} needs --param {missing.Name}=...");
        }
        return _service.Execute(request);
    }
}
