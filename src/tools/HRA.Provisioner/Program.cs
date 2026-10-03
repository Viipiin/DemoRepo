using HRA.Provisioner;
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
          check              Connect, show who you are and confirm the HRAutomation solution exists.
          provision          Create or update choices, tables, columns, relationships, keys, forms, views,
                             environment variables, security roles, teams, column security and settings.
          register-plugins   Upload the built plugin assembly and register its steps.
                             Build first: dotnet build src/plugins/HRAutomation.Plugins -c Release
          seed               Load synthetic sample data (departments, a Bengaluru office, 50 employees).

        Default --url is https://viipiin.crm.dynamics.com. Safe to re-run: existing items are skipped or updated.
        """);
    return 0;
}

var url = Option("url", DefaultUrl);
var username = Option("username");
var repoRoot = RepoPaths.FindRoot();

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
            new Checker(client).Run();
            break;
        case "provision":
            new MetadataProvisioner(client).Run();
            new FormAndViewProvisioner(client).Run();
            new SecurityProvisioner(client).Run();
            new EnvironmentSettings(client).Run();
            new MetadataProvisioner(client).PublishAll();
            break;
        case "register-plugins":
            var assemblyPath = Option("assembly", Path.Combine(repoRoot, "src", "plugins", "HRAutomation.Plugins", "bin", "Release", "net462", "HRAutomation.Plugins.dll"));
            new PluginRegistrar(client).Run(assemblyPath);
            break;
        case "seed":
            new Seeder(client).Run();
            break;
        default:
            Log.Error($"Unknown command '{command}'. Run with 'help' to see the commands.");
            return 1;
    }
}
catch (Exception ex)
{
    Log.Error(ex.Message);
    Log.Detail(ex.ToString());
    return 1;
}

Log.Ok("Done.");
return 0;
