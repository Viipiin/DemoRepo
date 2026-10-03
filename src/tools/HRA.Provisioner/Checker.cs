using HRA.Provisioner.Model;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;

namespace HRA.Provisioner;

public sealed class Checker
{
    private readonly IOrganizationService _service;
    private readonly HrModel _model;

    public Checker(IOrganizationService service, HrModel model)
    {
        _service = service;
        _model = model;
    }

    public void Run()
    {
        var who = (WhoAmIResponse)_service.Execute(new WhoAmIRequest());
        var user = _service.Retrieve("systemuser", who.UserId, new Microsoft.Xrm.Sdk.Query.ColumnSet("fullname", "domainname"));
        Log.Ok($"Signed in as {user.GetAttributeValue<string>("fullname")} ({user.GetAttributeValue<string>("domainname")})");

        EnsureSolution(_service);

        var publisher = _service.FindOne("publisher", new[] { "customizationprefix", "customizationoptionvalueprefix" },
            ("customizationprefix", Conventions.Prefix));
        if (publisher == null)
        {
            Log.Warn($"No publisher with prefix '{Conventions.Prefix}' found.");
        }
        else
        {
            var optionPrefix = publisher.GetAttributeValue<int>("customizationoptionvalueprefix");
            Log.Ok($"Publisher prefix '{Conventions.Prefix}', choice value prefix {optionPrefix}");
            if (optionPrefix * 10000 != Conventions.ChoiceValueBase)
            {
                Log.Warn($"Expected choice value prefix {Conventions.ChoiceValueBase / 10000}. Choice values in the model assume it.");
            }
        }

        var existing = _model.Tables.Count(t => _service.TryExecute(
            new Microsoft.Xrm.Sdk.Messages.RetrieveEntityRequest { LogicalName = t.LogicalName, EntityFilters = Microsoft.Xrm.Sdk.Metadata.EntityFilters.Entity }, out _));
        Log.Info($"Model tables present in Dataverse: {existing} of {_model.Tables.Count}");
    }

    public static void EnsureSolution(IOrganizationService service)
    {
        var solution = service.FindOne("solution", new[] { "friendlyname", "version", "publisherid" },
            ("uniquename", Conventions.SolutionUniqueName));
        if (solution == null)
        {
            throw new InvalidOperationException($"Solution '{Conventions.SolutionUniqueName}' was not found in this environment. Check --url.");
        }

        var publisher = service.Retrieve("publisher", solution.GetAttributeValue<EntityReference>("publisherid").Id,
            new Microsoft.Xrm.Sdk.Query.ColumnSet("customizationprefix"));
        var prefix = publisher.GetAttributeValue<string>("customizationprefix");
        if (!string.Equals(prefix, Conventions.Prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Solution '{Conventions.SolutionUniqueName}' uses publisher prefix '{prefix}', expected '{Conventions.Prefix}'.");
        }

        Log.Ok($"Solution {Conventions.SolutionUniqueName} ({solution.GetAttributeValue<string>("friendlyname")} {solution.GetAttributeValue<string>("version")}) found");
    }
}
