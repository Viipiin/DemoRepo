using HRA.Provisioner.Model;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace HRA.Provisioner;

/// <summary>Environment variables, auditing and INR currency.</summary>
public sealed class EnvironmentSettings
{
    private const int NumberType = 100000001;
    // Used only when the base currency is USD. Update it in Settings > Business Management > Currencies if needed.
    private const decimal InrPerUsd = 88.0m;

    private readonly IOrganizationService _service;

    public EnvironmentSettings(IOrganizationService service) => _service = service;

    public void Run()
    {
        EnsureEnvironmentVariables();
        EnsureAuditing();
        EnsureRupee();
    }

    private void EnsureEnvironmentVariables()
    {
        Log.Step("Environment variables");
        foreach (var variable in Phase1.EnvironmentVariables)
        {
            if (_service.FindOne("environmentvariabledefinition", new[] { "schemaname" }, ("schemaname", variable.SchemaName)) != null)
            {
                Log.Skip($"{variable.SchemaName} exists");
                continue;
            }
            var create = new CreateRequest
            {
                Target = new Entity("environmentvariabledefinition")
                {
                    ["schemaname"] = variable.SchemaName,
                    ["displayname"] = variable.Label,
                    ["description"] = variable.Description,
                    ["type"] = new OptionSetValue(NumberType),
                    ["defaultvalue"] = variable.DefaultValue,
                },
            };
            create.Parameters["SolutionUniqueName"] = Conventions.SolutionUniqueName;
            _service.Execute(create);
            Log.Ok($"{variable.SchemaName} = {variable.DefaultValue}");
        }
    }

    private void EnsureAuditing()
    {
        Log.Step("Auditing");
        var organization = _service.RetrieveMultiple(new QueryExpression("organization") { ColumnSet = new ColumnSet("isauditenabled") }).Entities.Single();
        if (organization.GetAttributeValue<bool>("isauditenabled"))
        {
            Log.Skip("Environment auditing already on");
            return;
        }
        _service.Update(new Entity("organization", organization.Id) { ["isauditenabled"] = true });
        Log.Ok("Environment auditing turned on");
    }

    private void EnsureRupee()
    {
        Log.Step("Currency");
        var organization = _service.RetrieveMultiple(new QueryExpression("organization") { ColumnSet = new ColumnSet("basecurrencyid") }).Entities.Single();
        var baseCurrency = _service.Retrieve("transactioncurrency", organization.GetAttributeValue<EntityReference>("basecurrencyid").Id,
            new ColumnSet("isocurrencycode"));
        var baseCode = baseCurrency.GetAttributeValue<string>("isocurrencycode");

        Guid rupeeId;
        if (baseCode == "INR")
        {
            rupeeId = baseCurrency.Id;
            Log.Ok("Base currency is INR");
        }
        else
        {
            var rupee = _service.FindOne("transactioncurrency", new[] { "transactioncurrencyid" }, ("isocurrencycode", "INR"));
            if (rupee != null)
            {
                rupeeId = rupee.Id;
                Log.Skip($"Base currency is {baseCode}; INR already added");
            }
            else
            {
                if (baseCode != "USD")
                {
                    Log.Warn($"Base currency is {baseCode}. INR is added with rate {InrPerUsd}; correct the rate under Settings > Currencies.");
                }
                rupeeId = _service.Create(new Entity("transactioncurrency")
                {
                    ["currencyname"] = "Indian Rupee",
                    ["isocurrencycode"] = "INR",
                    ["currencysymbol"] = "₹",
                    ["currencyprecision"] = 2,
                    ["exchangerate"] = InrPerUsd,
                });
                Log.Ok($"Base currency is {baseCode}; INR added (exchange rate {InrPerUsd} per {baseCode})");
            }
        }

        var me = ((WhoAmIResponse)_service.Execute(new WhoAmIRequest())).UserId;
        _service.Update(new Entity("usersettings", me)
        {
            ["transactioncurrencyid"] = new EntityReference("transactioncurrency", rupeeId),
        });
        Log.Ok("Your default currency is INR (other users set it under Personalization Settings)");
    }
}
