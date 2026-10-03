using System.Reflection;
using HRA.Provisioner.Model;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace HRA.Provisioner;

/// <summary>Uploads the plugin assembly and registers the steps listed in model/plugins.json. Re-run after every plugin build.</summary>
public sealed class PluginRegistrar
{
    private readonly IOrganizationService _service;
    private readonly HrModel _model;

    public PluginRegistrar(IOrganizationService service, HrModel model)
    {
        _service = service;
        _model = model;
    }

    public void Run(string assemblyPath)
    {
        if (!File.Exists(assemblyPath))
        {
            throw new FileNotFoundException(
                $"Plugin assembly not found at {assemblyPath}. Build it first: dotnet build src/plugins/HRAutomation.Plugins -c Release");
        }

        Log.Step("Plugin assembly");
        var assemblyId = UpsertAssembly(assemblyPath);

        Log.Step("Plugin types and steps");
        var typeIds = _model.PluginSteps.Select(s => s.Plugin).Concat(_model.CustomApis.Select(a => a.Plugin))
            .Distinct().ToDictionary(t => t, t => EnsurePluginType(assemblyId, t));
        foreach (var step in _model.PluginSteps) EnsureStep(step, typeIds[step.Plugin]);

        if (_model.CustomApis.Count > 0) Log.Step("Custom APIs");
        foreach (var api in _model.CustomApis) EnsureCustomApi(api, typeIds[api.Plugin]);
    }

    private Guid UpsertAssembly(string path)
    {
        var name = AssemblyName.GetAssemblyName(path);
        var token = name.GetPublicKeyToken();
        if (token == null || token.Length == 0) throw new InvalidOperationException("The plugin assembly must be signed.");

        var assembly = new Entity("pluginassembly")
        {
            ["name"] = name.Name,
            ["content"] = Convert.ToBase64String(File.ReadAllBytes(path)),
            ["version"] = name.Version!.ToString(),
            ["culture"] = string.IsNullOrEmpty(name.CultureName) ? "neutral" : name.CultureName,
            ["publickeytoken"] = Convert.ToHexString(token).ToLowerInvariant(),
            ["isolationmode"] = new OptionSetValue(2), // Sandbox
            ["sourcetype"] = new OptionSetValue(0),    // Database
        };

        var existing = _service.FindOne("pluginassembly", new[] { "pluginassemblyid" }, ("name", name.Name));
        if (existing != null)
        {
            assembly.Id = existing.Id;
            _service.Update(assembly);
            Log.Ok($"{name.Name} {name.Version} updated");
            return existing.Id;
        }

        var create = new CreateRequest { Target = assembly };
        create.Parameters["SolutionUniqueName"] = Conventions.SolutionUniqueName;
        var id = ((CreateResponse)_service.Execute(create)).id;
        Log.Ok($"{name.Name} {name.Version} registered");
        return id;
    }

    private Guid EnsurePluginType(Guid assemblyId, string typeName)
    {
        var existing = _service.FindOne("plugintype", new[] { "plugintypeid" }, ("pluginassemblyid", assemblyId), ("typename", typeName));
        if (existing != null)
        {
            Log.Skip($"{typeName} registered");
            return existing.Id;
        }
        var shortName = typeName.Split('.').Last();
        var id = _service.Create(new Entity("plugintype")
        {
            ["pluginassemblyid"] = new EntityReference("pluginassembly", assemblyId),
            ["typename"] = typeName,
            ["name"] = typeName,
            ["friendlyname"] = shortName,
        });
        Log.Ok($"{typeName} registered");
        return id;
    }

    private void EnsureStep(PluginStepDef step, Guid pluginTypeId)
    {
        var shortType = step.Plugin.Split('.').Last();
        var stepName = $"{shortType}: {step.Message} of {step.Table}";

        var messageId = _service.FindOne("sdkmessage", new[] { "sdkmessageid" }, ("name", step.Message))?.Id
            ?? throw new InvalidOperationException($"SDK message {step.Message} not found.");
        var filterId = _service.FindOne("sdkmessagefilter", new[] { "sdkmessagefilterid" },
            ("sdkmessageid", messageId), ("primaryobjecttypecode", step.Table))?.Id
            ?? throw new InvalidOperationException($"{step.Message} is not available for {step.Table}. Run 'provision' first.");

        var values = new Entity("sdkmessageprocessingstep")
        {
            ["name"] = stepName,
            ["eventhandler"] = new EntityReference("plugintype", pluginTypeId),
            ["sdkmessageid"] = new EntityReference("sdkmessage", messageId),
            ["sdkmessagefilterid"] = new EntityReference("sdkmessagefilter", filterId),
            ["stage"] = new OptionSetValue(step.Stage),
            ["mode"] = new OptionSetValue(0), // Synchronous
            ["rank"] = 1,
            ["supporteddeployment"] = new OptionSetValue(0), // Server
            ["filteringattributes"] = step.FilteringAttributes,
        };

        var existing = _service.FindOne("sdkmessageprocessingstep", new[] { "sdkmessageprocessingstepid" },
            ("eventhandler", pluginTypeId), ("sdkmessageid", messageId), ("stage", step.Stage));
        Guid stepId;
        if (existing != null)
        {
            values.Id = stepId = existing.Id;
            _service.Update(values);
            Log.Ok($"Step '{stepName}' updated");
        }
        else
        {
            var create = new CreateRequest { Target = values };
            create.Parameters["SolutionUniqueName"] = Conventions.SolutionUniqueName;
            stepId = ((CreateResponse)_service.Execute(create)).id;
            Log.Ok($"Step '{stepName}' registered");
        }

        if (step.PreImageAttributes != null) EnsurePreImage(stepId, step.PreImageAttributes);
    }

    private void EnsureCustomApi(CustomApiDef api, Guid pluginTypeId)
    {
        var existing = _service.FindOne("customapi", new[] { "customapiid" }, ("uniquename", api.UniqueName));
        Guid apiId;
        if (existing != null)
        {
            apiId = existing.Id;
            _service.Update(new Entity("customapi", apiId)
            {
                ["displayname"] = api.DisplayName,
                ["description"] = api.Description,
                ["plugintypeid"] = new EntityReference("plugintype", pluginTypeId),
            });
            Log.Ok($"{api.UniqueName} updated");
        }
        else
        {
            var create = new CreateRequest
            {
                Target = new Entity("customapi")
                {
                    ["uniquename"] = api.UniqueName,
                    ["name"] = api.UniqueName,
                    ["displayname"] = api.DisplayName,
                    ["description"] = api.Description,
                    ["bindingtype"] = new OptionSetValue(0), // Global
                    ["isfunction"] = api.IsFunction,
                    ["isprivate"] = false,
                    ["allowedcustomprocessingsteptype"] = new OptionSetValue(0), // None
                    ["plugintypeid"] = new EntityReference("plugintype", pluginTypeId),
                },
            };
            create.Parameters["SolutionUniqueName"] = Conventions.SolutionUniqueName;
            apiId = ((CreateResponse)_service.Execute(create)).id;
            Log.Ok($"{api.UniqueName} created");
        }

        foreach (var parameter in api.Parameters) EnsureApiField("customapirequestparameter", apiId, api, parameter, isParameter: true);
        foreach (var response in api.Responses) EnsureApiField("customapiresponseproperty", apiId, api, response, isParameter: false);
    }

    private void EnsureApiField(string table, Guid apiId, CustomApiDef api, CustomApiFieldDef field, bool isParameter)
    {
        if (_service.FindOne(table, new[] { "uniquename" }, ("customapiid", apiId), ("uniquename", field.Name)) != null) return;

        var record = new Entity(table)
        {
            ["customapiid"] = new EntityReference("customapi", apiId),
            ["uniquename"] = field.Name,
            ["name"] = $"{api.UniqueName}.{field.Name}",
            ["displayname"] = field.Name,
            ["description"] = field.Description ?? field.Name,
            ["type"] = new OptionSetValue(ModelLoader.CustomApiTypes[field.Type]),
        };
        if (isParameter) record["isoptional"] = field.Optional;

        var create = new CreateRequest { Target = record };
        create.Parameters["SolutionUniqueName"] = Conventions.SolutionUniqueName;
        _service.Execute(create);
        Log.Ok($"  {(isParameter ? "parameter" : "response")} {field.Name} ({field.Type}{(field.Optional ? ", optional" : "")})");
    }

    private void EnsurePreImage(Guid stepId, string attributes)
    {
        var image = new Entity("sdkmessageprocessingstepimage")
        {
            ["sdkmessageprocessingstepid"] = new EntityReference("sdkmessageprocessingstep", stepId),
            ["imagetype"] = new OptionSetValue(0), // PreImage
            ["name"] = "PreImage",
            ["entityalias"] = "PreImage",
            ["messagepropertyname"] = "Target",
            ["attributes"] = attributes,
        };

        var existing = _service.FindOne("sdkmessageprocessingstepimage", new[] { "sdkmessageprocessingstepimageid" },
            ("sdkmessageprocessingstepid", stepId), ("entityalias", "PreImage"));
        if (existing != null)
        {
            image.Id = existing.Id;
            _service.Update(image);
        }
        else
        {
            _service.Create(image);
        }
        Log.Ok("  PreImage: " + attributes);
    }
}
