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
                             teams and column security. Also turns on auditing and adds INR.
          register-plugins   Upload the built plugin assembly and register its steps.
                             Build first: dotnet build src/plugins/HRAutomation.Plugins -c Release
          seed               Load synthetic sample data (departments, a Bengaluru office, 50 employees).

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
       $"{model.Tables.Sum(t => t.Columns.Count + 1)} columns, {model.Roles.Count} roles");
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
            metadata.PublishAll();
            if (formFailures.Count > 0)
            {
                Log.Error($"{formFailures.Count} form/view item(s) failed; everything else was applied:");
                foreach (var failure in formFailures) Log.Error("  " + failure);
                return 1;
            }
            break;
        case "register-plugins":
            var assemblyPath = Option("assembly", Path.Combine(repoRoot, "src", "plugins", "HRAutomation.Plugins", "bin", "Release", "net462", "HRAutomation.Plugins.dll"));
            new PluginRegistrar(client).Run(assemblyPath);
            break;
        case "seed":
            new Seeder(client, model).Run();
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
