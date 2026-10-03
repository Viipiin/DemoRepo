using HRA.Provisioner;
using HRA.Provisioner.Model;
using Microsoft.PowerPlatform.Dataverse.Client;

const string DefaultUrl = "https://viipiin.crm.dynamics.com";

var command = args.FirstOrDefault(a => !a.StartsWith("--"))?.ToLowerInvariant();
string Option(string name, string fallback = null)
{
    var index = Array.FindIndex(args, a => a.Equals("--" + name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
}

if (command is null or "help")
{
    Console.WriteLine("""
        HR Automation provisioner

        Usage: dotnet run --project src/tools/HRA.Provisioner -- <command> [--url <environment url>] [--username <you@tenant>]

        Commands
          validate           Check the JSON model in model/ for mistakes. Doesn't connect to Dataverse.
          check              Connect, show who you are and confirm the HRAutomation solution exists.
          provision          Create or update everything defined in model/: choices, tables, columns,
                             relationships, keys, forms, views, environment variables, security roles,
                             teams, column security, connection references and cloud flows (created off).
                             Also turns on auditing and adds INR.
          register-plugins   Upload the built plugin assembly and register the steps in model/plugins.json.
                             Build first: dotnet build src/plugins/HRAutomation.Plugins -c Release
          run-api <name>     Run a Custom API from model/custom-apis.json, e.g.
                               run-api hra_RunLeaveAccrual --param Period=2026-10
          init-balances      Create missing leave balances for every active employee (one call per employee).
                             Optional: --param LeaveYear=2026-27
          rollover           Year-end rollover for every active employee (one call per employee): carry forward
                             up to the policy limit, lapse the rest. Optional: --param FromLeaveYear=2026-27
          seed               Load the sample data in model/sample-data.json (synthetic: departments, a Bengaluru
                             office, 50 employees).

        Default --url is https://viipiin.crm.dynamics.com. Safe to re-run: existing items are skipped or updated.
        The model is read from the repo's model/ folder (override with --model <folder>).
        """);
    return 0;
}

var url = Option("url", DefaultUrl);
var username = Option("username");
var repoRoot = RepoPaths.FindRoot();

HrModel model;
try
{
    model = ModelLoader.Load(Option("model", Path.Combine(repoRoot, "model")));
}
catch (Exception ex)
{
    Log.Error(ex.Message);
    return 1;
}
Log.Ok($"Model loaded: {model.Choices.Count} global choices, {model.Tables.Count} tables, " +
       $"{model.Tables.Sum(t => t.Columns.Count + 1)} columns, {model.Roles.Count} roles, " +
       $"{model.PluginSteps.Count} plugin steps, {model.CustomApis.Count} custom APIs, {model.Flows.Count} flows, " +
       $"{model.SampleData.Sum(g => g.Rows.Count)} sample rows");
if (command == "validate")
{
    Log.Ok("Model is valid.");
    return 0;
}

Log.Info($"Connecting to {url} (a browser window may open for sign-in)...");
using var client = Connection.Create(url, username, repoRoot);
if (!client.IsReady)
{
    Log.Error("Could not connect: " + client.LastError);
    return 1;
}
Log.Ok($"Connected to {client.ConnectedOrgFriendlyName} as {client.OAuthUserId}");

try
{
    switch (command)
    {
        case "check":
            new Checker(client, model).Run();
            break;
        case "provision":
            var metadata = new MetadataProvisioner(client, model);
            metadata.Run();
            var formFailures = new FormAndViewProvisioner(client, model).Run();
            new SecurityProvisioner(client, model).Run();
            new EnvironmentSettings(client, model).Run();
            new FlowProvisioner(client, model).Run();
            metadata.PublishAll();
            if (formFailures.Count > 0)
            {
                Log.Error($"{formFailures.Count} form/view item(s) failed; everything else was applied:");
                foreach (var failure in formFailures) Log.Error("  " + failure);
                return 1;
            }
            break;
        case "register-plugins":
            var assemblyPath = Option("assembly", Path.Combine(repoRoot, "src", "plugins", model.PluginAssembly, "bin", "Release", "net462", model.PluginAssembly + ".dll"));
            new PluginRegistrar(client, model).Run(assemblyPath);
            break;
        case "seed":
            new Seeder(client, model).Run();
            break;
        case "run-api":
            var apiName = args.Where(a => !a.StartsWith("--")).Skip(1).FirstOrDefault()
                ?? throw new ArgumentException("Give the Custom API name, e.g. run-api hra_RunLeaveAccrual");
            new ApiRunner(client, model).Run(apiName, ApiParameters());
            break;
        case "init-balances":
            new ApiRunner(client, model).RunForEachEmployee("hra_InitializeLeaveBalances", "Created", "balance(s) created", ApiParameters());
            break;
        case "rollover":
            new ApiRunner(client, model).RunForEachEmployee("hra_RunYearEndRollover", "RolledOver", "balance(s) rolled over", ApiParameters());
            break;
        default:
            Log.Error($"Unknown command '{command}'. Run with 'help' to see the commands.");
            return 1;
    }
}
catch (System.ServiceModel.FaultException<Microsoft.Xrm.Sdk.OrganizationServiceFault> ex)
{
    Log.Error(Errors.Describe(ex));
    Log.Detail(ex.Detail.TraceText ?? "");
    Log.Detail(ex.ToString());
    return 1;
}
catch (Exception ex)
{
    Log.Error(ex.Message);
    Log.Detail(ex.ToString());
    return 1;
}

Log.Ok("Done.");
return 0;

// --param Name=Value pairs for run-api.
Dictionary<string, string> ApiParameters()
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (!args[i].Equals("--param", StringComparison.OrdinalIgnoreCase)) continue;
        var pair = args[i + 1].Split('=', 2);
        if (pair.Length == 2) result[pair[0]] = pair[1];
    }
    return result;
}
