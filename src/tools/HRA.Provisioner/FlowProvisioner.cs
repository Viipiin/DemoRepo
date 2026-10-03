using System.Text.Json;
using System.Text.Json.Nodes;
using HRA.Provisioner.Model;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;

namespace HRA.Provisioner;

/// <summary>
/// Creates the connection references and cloud flows defined in model/connection-references.json and
/// model/flows/*.json. Flows are created turned off: connections need your sign-in, so you link the
/// connection references and turn the flows on in the maker portal (docs/phase-2b-runbook.md).
/// </summary>
public sealed class FlowProvisioner
{
    private const int ModernFlowCategory = 5;
    private const int DefinitionType = 1;
    private const int ActivatedState = 1;

    private readonly IOrganizationService _service;
    private readonly HrModel _model;

    public FlowProvisioner(IOrganizationService service, HrModel model)
    {
        _service = service;
        _model = model;
    }

    public void Run()
    {
        if (_model.ConnectionReferences.Count == 0 && _model.Flows.Count == 0) return;

        Log.Step("Connection references");
        foreach (var reference in _model.ConnectionReferences) EnsureConnectionReference(reference);

        Log.Step("Cloud flows");
        foreach (var flow in _model.Flows) EnsureFlow(flow);
    }

    private void EnsureConnectionReference(ConnectionReferenceDef reference)
    {
        var existing = _service.FindOne("connectionreference", new[] { "connectionid" },
            ("connectionreferencelogicalname", reference.Name));
        if (existing != null)
        {
            var linked = !string.IsNullOrEmpty(existing.GetAttributeValue<string>("connectionid"));
            Log.Skip($"{reference.Name} exists{(linked ? " (connection linked)" : " (no connection linked yet)")}");
            return;
        }

        var create = new CreateRequest
        {
            Target = new Entity("connectionreference")
            {
                ["connectionreferencelogicalname"] = reference.Name,
                ["connectionreferencedisplayname"] = reference.Label,
                ["connectorid"] = reference.ConnectorId,
                ["description"] = reference.Description,
            },
        };
        create.Parameters["SolutionUniqueName"] = Conventions.SolutionUniqueName;
        _service.Execute(create);
        Log.Ok($"{reference.Name} created ({reference.Connector})");
    }

    private void EnsureFlow(FlowDef flow)
    {
        var clientData = BuildClientData(flow);
        var existing = _service.FindOne("workflow", new[] { "statecode" }, ("name", flow.Name), ("category", ModernFlowCategory));
        if (existing != null)
        {
            if (existing.GetAttributeValue<OptionSetValue>("statecode")?.Value == ActivatedState)
            {
                Log.Warn($"'{flow.Name}' is turned on, so it wasn't updated. Turn it off, run provision again, then turn it back on.");
                return;
            }
            _service.Update(new Entity("workflow", existing.Id)
            {
                ["clientdata"] = clientData,
                ["description"] = flow.Description,
            });
            Log.Ok($"'{flow.Name}' updated (off)");
            return;
        }

        var create = new CreateRequest
        {
            Target = new Entity("workflow")
            {
                ["name"] = flow.Name,
                ["description"] = flow.Description,
                ["category"] = new OptionSetValue(ModernFlowCategory),
                ["type"] = new OptionSetValue(DefinitionType),
                ["primaryentity"] = "none",
                ["clientdata"] = clientData,
            },
        };
        create.Parameters["SolutionUniqueName"] = Conventions.SolutionUniqueName;
        _service.Execute(create);
        Log.Ok($"'{flow.Name}' created (off)");
    }

    /// <summary>
    /// Wraps the definition the way solution-aware flows store it: connection references by logical name,
    /// plus the $connections/$authentication parameters and one parameter per environment variable.
    /// </summary>
    private string BuildClientData(FlowDef flow)
    {
        var definition = JsonNode.Parse(flow.DefinitionJson)!.AsObject();
        var parameters = definition["parameters"] as JsonObject ?? new JsonObject();
        parameters["$connections"] = new JsonObject { ["defaultValue"] = new JsonObject(), ["type"] = "Object" };
        parameters["$authentication"] = new JsonObject { ["defaultValue"] = new JsonObject(), ["type"] = "SecureObject" };
        foreach (var schema in flow.EnvironmentVariables)
        {
            var variable = _model.EnvironmentVariables.First(v => v.SchemaName == schema);
            parameters[variable.FlowParameterName] = new JsonObject
            {
                ["defaultValue"] = variable.DefaultValue ?? "",
                ["type"] = variable.Type == "Number" ? "Int" : variable.Type == "Boolean" ? "Bool" : "String",
                ["metadata"] = new JsonObject { ["schemaName"] = variable.SchemaName, ["description"] = variable.Description },
            };
        }
        definition["parameters"] = parameters;
        definition["$schema"] ??= "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#";
        definition["contentVersion"] ??= "1.0.0.0";

        var references = new JsonObject();
        foreach (var (connector, referenceName) in flow.ConnectionReferences)
        {
            references[connector] = new JsonObject
            {
                ["runtimeSource"] = "embedded",
                ["connection"] = new JsonObject { ["connectionReferenceLogicalName"] = referenceName },
                ["api"] = new JsonObject { ["name"] = connector },
            };
        }

        var clientData = new JsonObject
        {
            ["properties"] = new JsonObject
            {
                ["connectionReferences"] = references,
                ["definition"] = definition,
            },
            ["schemaVersion"] = "1.0.0.0",
        };
        return clientData.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }
}
