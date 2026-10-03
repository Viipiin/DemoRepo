using System.ServiceModel;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace HRA.Provisioner;

public static class Connection
{
    // Microsoft's well-known app registration for Dataverse developer tools (interactive sign-in).
    private const string AppId = "51f81489-12ee-4a9e-aaae-a2591f45987d";

    public static ServiceClient Create(string url, string username, string repoRoot)
    {
        var cache = Path.Combine(repoRoot, ".hra-token-cache");
        Directory.CreateDirectory(cache);
        var connectionString =
            $"AuthType=OAuth;Url={url};AppId={AppId};RedirectUri=http://localhost;LoginPrompt=Auto;" +
            $"TokenCacheStorePath={Path.Combine(cache, "token.dat")};" +
            (string.IsNullOrWhiteSpace(username) ? "" : $"Username={username};");
        ServiceClient.MaxConnectionTimeout = TimeSpan.FromMinutes(10);
        return new ServiceClient(connectionString);
    }
}

public static class RepoPaths
{
    /// <summary>Walks up from the current directory to the folder containing the .git directory.</summary>
    public static string FindRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? Directory.GetCurrentDirectory();
    }
}

public static class Log
{
    public static void Info(string message) => Write(ConsoleColor.Gray, "  ", message);
    public static void Ok(string message) => Write(ConsoleColor.Green, "✔ ", message);
    public static void Skip(string message) => Write(ConsoleColor.DarkGray, "· ", message);
    public static void Warn(string message) => Write(ConsoleColor.Yellow, "! ", message);
    public static void Error(string message) => Write(ConsoleColor.Red, "✖ ", message);
    public static void Step(string message) => Write(ConsoleColor.Cyan, "\n▶ ", message);

    public static void Detail(string message)
    {
        if (Environment.GetEnvironmentVariable("HRA_VERBOSE") == "1") Write(ConsoleColor.DarkGray, "  ", message);
    }

    private static void Write(ConsoleColor color, string prefix, string message)
    {
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(prefix + message);
        Console.ForegroundColor = previous;
    }
}

public static class ServiceExtensions
{
    /// <summary>Runs a request, returning false instead of throwing when Dataverse reports the item does not exist.</summary>
    public static bool TryExecute(this IOrganizationService service, OrganizationRequest request, out OrganizationResponse response)
    {
        try
        {
            response = service.Execute(request);
            return true;
        }
        catch (FaultException<OrganizationServiceFault>)
        {
            response = null;
            return false;
        }
    }

    public static Entity FindOne(this IOrganizationService service, string table, string[] columns, params (string Attribute, object Value)[] conditions)
    {
        var query = new QueryExpression(table) { ColumnSet = new ColumnSet(columns), TopCount = 1 };
        foreach (var (attribute, value) in conditions)
        {
            query.Criteria.AddCondition(attribute, ConditionOperator.Equal, value);
        }
        return service.RetrieveMultiple(query).Entities.FirstOrDefault();
    }

    public static Guid RootBusinessUnitId(this IOrganizationService service)
    {
        var query = new QueryExpression("businessunit") { ColumnSet = new ColumnSet("businessunitid"), TopCount = 1 };
        query.Criteria.AddCondition("parentbusinessunitid", ConditionOperator.Null);
        return service.RetrieveMultiple(query).Entities.Single().Id;
    }

    public static Microsoft.Xrm.Sdk.Label Label(string text) => new(text, Model.Conventions.LanguageCode);
}
