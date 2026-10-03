using System.Text.Json;
using System.Text.Json.Serialization;

namespace HRA.Provisioner.Model;

/// <summary>
/// Reads the JSON model in the repo's model/ folder and checks it before anything touches Dataverse.
/// See model/README.md for the file format.
/// </summary>
public static class ModelLoader
{
    /// <summary>Tables outside the model that lookups may point to.</summary>
    private static readonly HashSet<string> SystemTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "systemuser", "team", "contact", "account", "businessunit",
    };

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static HrModel Load(string modelFolder)
    {
        if (!Directory.Exists(modelFolder))
        {
            throw new DirectoryNotFoundException($"Model folder not found: {modelFolder}");
        }

        var errors = new List<string>();

        var choicesFile = Read<ChoicesFile>(Path.Combine(modelFolder, "choices.json"), errors);
        var choices = (choicesFile?.Choices ?? new()).Select(c => new ChoiceDef(c.Name, c.Label, c.Options ?? Array.Empty<string>())).ToList();

        var tables = new List<TableDef>();
        var tableFolder = Path.Combine(modelFolder, "tables");
        var tableFiles = Directory.Exists(tableFolder)
            ? Directory.GetFiles(tableFolder, "*.json").OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();
        foreach (var file in tableFiles)
        {
            var json = Read<TableJson>(file, errors);
            if (json != null) tables.Add(ToTable(json, Path.GetFileName(file), choices, errors));
        }

        var security = Read<SecurityFile>(Path.Combine(modelFolder, "security.json"), errors) ?? new SecurityFile();
        var variables = Read<EnvironmentVariablesFile>(Path.Combine(modelFolder, "environment-variables.json"), errors) ?? new EnvironmentVariablesFile();

        var roles = security.Roles.Select(r => ToRole(r, errors)).ToList();
        var profiles = security.ColumnSecurityProfiles.Select(p => ToProfile(p, errors)).ToList();
        var environmentVariables = variables.EnvironmentVariables
            .Select(v => new EnvironmentVariableDef(v.Name, v.Label, v.Default, v.Description)).ToList();

        var ownerTeams = security.OwnerTeams.Select(t => new OwnerTeamDef(t.Name, t.Roles ?? Array.Empty<string>())).ToList();

        var plugins = Read<PluginsFile>(Path.Combine(modelFolder, "plugins.json"), errors) ?? new PluginsFile();
        var steps = plugins.Steps.Select(step => ToPluginStep(step, errors)).Where(step => step != null).ToList();

        // Sample data is optional: without the file, 'seed' has nothing to load.
        var samplePath = Path.Combine(modelFolder, "sample-data.json");
        var sample = File.Exists(samplePath) ? Read<SampleFile>(samplePath, errors) ?? new SampleFile() : new SampleFile();
        var sampleGroups = sample.Groups.Select(g => new SampleGroup
        {
            Table = g.Table?.ToLowerInvariant(),
            Key = g.Key ?? Array.Empty<string>(),
            Upsert = g.Upsert,
            Rows = g.Rows ?? new(),
        }).ToList();

        var apisPath = Path.Combine(modelFolder, "custom-apis.json");
        var apisFile = File.Exists(apisPath) ? Read<CustomApisFile>(apisPath, errors) ?? new CustomApisFile() : new CustomApisFile();
        var customApis = apisFile.CustomApis.Select(a => new CustomApiDef(
            a.UniqueName, a.DisplayName, a.Description, a.Plugin, a.IsFunction,
            (a.Parameters ?? new()).Select(p => new CustomApiFieldDef(p.Name, p.Type, p.Optional, p.Description)).ToList(),
            (a.Responses ?? new()).Select(p => new CustomApiFieldDef(p.Name, p.Type, false, p.Description)).ToList())).ToList();

        var model = new HrModel(choices, tables, ownerTeams, roles, profiles, environmentVariables, plugins.Assembly, steps, sampleGroups, customApis);
        Validate(model, errors);
        ValidatePluginSteps(model, errors);
        ValidateCustomApis(model, errors);
        SampleDataValidator.Validate(model, errors);

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"The model in {modelFolder} has {errors.Count} problem(s):{Environment.NewLine}  - " +
                string.Join(Environment.NewLine + "  - ", errors));
        }
        return model;
    }

    private static T Read<T>(string path, List<string> errors) where T : class
    {
        if (!File.Exists(path))
        {
            errors.Add($"{Path.GetFileName(path)}: file is missing.");
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        }
        catch (JsonException ex)
        {
            errors.Add($"{Path.GetFileName(path)}: invalid JSON at line {ex.LineNumber + 1}: {ex.Message}");
            return null;
        }
    }

    private static TableDef ToTable(TableJson json, string file, List<ChoiceDef> choices, List<string> errors)
    {
        string Where(string column) => $"{file}: {column}";

        ColumnDef ToColumn(ColumnJson c, bool primary)
        {
            if (string.IsNullOrWhiteSpace(c.Name) || string.IsNullOrWhiteSpace(c.Label))
            {
                errors.Add($"{file}: every column needs a name and a label.");
                return null;
            }
            if (!Enum.TryParse<ColumnType>(c.Type, ignoreCase: false, out var type))
            {
                errors.Add($"{Where(c.Name)}: type \"{c.Type}\" is not valid. Use one of: {string.Join(", ", Enum.GetNames<ColumnType>())}.");
                return null;
            }
            var requirement = Requirement.Optional;
            if (c.Required != null && !Enum.TryParse(c.Required, ignoreCase: false, out requirement))
            {
                errors.Add($"{Where(c.Name)}: required must be Required, Recommended or Optional.");
            }
            if (primary && type != ColumnType.Text)
            {
                errors.Add($"{Where(c.Name)}: the primary name column must be type Text.");
            }

            var options = c.Options;
            if (type == ColumnType.Choice && (options == null || options.Length == 0))
            {
                errors.Add($"{Where(c.Name)}: a Choice column needs an \"options\" list.");
            }
            if (type == ColumnType.GlobalChoice)
            {
                var global = choices.FirstOrDefault(g => g.Name == c.Choice);
                if (global == null) errors.Add($"{Where(c.Name)}: global choice \"{c.Choice}\" is not in choices.json.");
                options = global?.Options;
            }

            int? defaultOption = null;
            if (c.Default != null)
            {
                var index = options == null ? -1 : Array.IndexOf(options, c.Default);
                if (index < 0) errors.Add($"{Where(c.Name)}: default \"{c.Default}\" is not one of its options.");
                else defaultOption = index;
            }

            if (type == ColumnType.Lookup && string.IsNullOrWhiteSpace(c.Target))
            {
                errors.Add($"{Where(c.Name)}: a Lookup column needs a \"target\" table.");
            }

            return new ColumnDef(c.Name, c.Label, type)
            {
                Description = c.Description,
                Requirement = requirement,
                MaxLength = c.MaxLength ?? type switch
                {
                    ColumnType.Email => 100,
                    ColumnType.Phone => 20,
                    ColumnType.Url => 200,
                    ColumnType.Memo => 2000,
                    ColumnType.Text => 100,
                    _ => null,
                },
                MinValue = c.Min,
                MaxValue = c.Max,
                Precision = c.Precision ?? 2,
                AutoNumberFormat = c.AutoNumber,
                Secured = c.Secured,
                Audited = c.Audited || c.Secured,
                GlobalChoice = c.Choice,
                Options = type == ColumnType.Choice ? options : null,
                DefaultOption = defaultOption,
                Target = c.Target?.ToLowerInvariant(),
                Behaviour = c.OnDelete switch
                {
                    "Restrict" => LookupBehaviour.Restrict,
                    "Cascade" => LookupBehaviour.Parental,
                    null or "RemoveLink" => LookupBehaviour.RemoveLink,
                    _ => Invalid(errors, $"{Where(c.Name)}: onDelete must be RemoveLink, Restrict or Cascade.", LookupBehaviour.RemoveLink),
                },
                MaxSizeKb = c.MaxSizeKb ?? 10240,
                ReadOnlyOnForm = c.ReadOnly || c.AutoNumber != null,
            };
        }

        if (json.PrimaryName == null) errors.Add($"{file}: \"primaryName\" is missing.");

        var table = new TableDef(json.Name, json.Label, json.PluralLabel)
        {
            Phase = json.Phase,
            Description = json.Description,
            OrganizationOwned = json.Ownership switch
            {
                "Organization" => true,
                "User" or null => false,
                _ => Invalid(errors, $"{file}: ownership must be User or Organization.", false),
            },
            HasNotes = json.HasNotes,
            Audited = json.Audited,
            PrimaryName = json.PrimaryName == null ? null : ToColumn(json.PrimaryName, primary: true),
            Columns = json.Columns.Select(c => ToColumn(c, primary: false)).Where(c => c != null).ToList(),
            Keys = json.Keys.Select(k => new KeyDef(k.Name, k.Label, k.Columns ?? Array.Empty<string>())).ToList(),
            Form = json.Form.Select(f => new FormTab(f.Tab, f.Label, f.Left ?? Array.Empty<string>(), f.Right)).ToList(),
            Views = json.Views.Select(v => new ViewDef(v.Default ? null : v.Name, v.Columns ?? Array.Empty<string>())
            {
                Description = v.Description,
                ExtraConditions = v.Filter ?? "",
                Join = v.Join ?? "",
                SortColumn = v.Sort,
            }).ToList(),
            QuickFind = json.QuickFind ?? Array.Empty<string>(),
        };
        return table;
    }

    private static RoleDef ToRole(RoleJson json, List<string> errors)
    {
        var role = new RoleDef(json.Name, json.Description);
        foreach (var (table, value) in json.Tables)
        {
            var parts = value.Split(':');
            var depth = parts.Length == 2 ? parts[0] switch
            {
                "Org" => Depth.Organization,
                "BusinessUnit" => Depth.BusinessUnit,
                "User" => Depth.User,
                _ => (Depth?)null,
            } : null;
            if (depth == null || parts[1].Any(ch => !"CRWDATS".Contains(ch)))
            {
                errors.Add($"security.json: role \"{json.Name}\", table {table}: use \"Org:CRW...\", \"BusinessUnit:...\" or \"User:...\" with letters from CRWDATS.");
                continue;
            }
            role.Tables[table.ToLowerInvariant()] = new Access(parts[1], depth.Value);
        }
        return role;
    }

    private static FieldSecurityProfileDef ToProfile(ProfileJson json, List<string> errors)
    {
        var profile = new FieldSecurityProfileDef(json.Name, json.Description, json.Team);
        foreach (var column in json.Columns)
        {
            var parts = column.ToLowerInvariant().Split('.');
            if (parts.Length != 2)
            {
                errors.Add($"security.json: profile \"{json.Name}\": \"{column}\" must be table.column.");
                continue;
            }
            profile.Columns.Add((parts[0], parts[1]));
        }
        return profile;
    }

    private static void Validate(HrModel model, List<string> errors)
    {
        foreach (var duplicate in model.Choices.GroupBy(c => c.Name).Where(g => g.Count() > 1))
        {
            errors.Add($"choices.json: choice {duplicate.Key} is defined twice.");
        }
        foreach (var choice in model.Choices)
        {
            if (!choice.Name.StartsWith(Conventions.Prefix + "_")) errors.Add($"choices.json: {choice.Name} must start with {Conventions.Prefix}_.");
            if (choice.Options.Length == 0) errors.Add($"choices.json: {choice.Name} has no options.");
        }

        var tableNames = model.Tables.Select(t => t.LogicalName).ToHashSet();
        foreach (var duplicate in model.Tables.GroupBy(t => t.LogicalName).Where(g => g.Count() > 1))
        {
            errors.Add($"tables: {duplicate.Key} is defined in more than one file.");
        }

        foreach (var table in model.Tables)
        {
            var name = table.LogicalName;
            if (!name.StartsWith(Conventions.Prefix + "_")) errors.Add($"{name}: table name must start with {Conventions.Prefix}_.");
            if (table.PrimaryName == null) continue;

            var columns = table.AllColumns.Select(c => c.LogicalName).ToList();
            foreach (var duplicate in columns.GroupBy(c => c).Where(g => g.Count() > 1))
            {
                errors.Add($"{name}: column {duplicate.Key} is defined twice.");
            }
            foreach (var column in table.AllColumns)
            {
                if (!column.LogicalName.StartsWith(Conventions.Prefix + "_")) errors.Add($"{name}.{column.LogicalName}: column name must start with {Conventions.Prefix}_.");
                if (column.Type == ColumnType.Lookup && column.Target != null && !tableNames.Contains(column.Target) && !SystemTables.Contains(column.Target))
                {
                    errors.Add($"{name}.{column.LogicalName}: lookup target {column.Target} is not a model table or a supported system table.");
                }
            }

            var known = columns.ToHashSet();
            void CheckColumns(string where, IEnumerable<string> names)
            {
                foreach (var column in names.Where(c => !known.Contains(c)))
                {
                    errors.Add($"{name} {where}: column {column} doesn't exist in this table (use lowercase logical names).");
                }
            }

            foreach (var key in table.Keys) CheckColumns($"key {key.SchemaName}", key.Columns);
            foreach (var tab in table.Form) CheckColumns($"form tab {tab.Name}", tab.Left.Concat(tab.Right));
            foreach (var view in table.Views)
            {
                CheckColumns($"view {view.Name ?? "(default)"}", view.Columns);
                if (view.SortColumn != null) CheckColumns($"view {view.Name ?? "(default)"} sort", new[] { view.SortColumn });
            }
            CheckColumns("quickFind", table.QuickFind);
            foreach (var view in table.Views.Where(v => !string.IsNullOrEmpty(v.Join) || !string.IsNullOrEmpty(v.ExtraConditions)))
            {
                try
                {
                    System.Xml.Linq.XElement.Parse($"<x>{view.ExtraConditions}{view.Join}</x>");
                }
                catch (System.Xml.XmlException ex)
                {
                    errors.Add($"{name} view {view.Name ?? "(default)"}: filter/join isn't valid FetchXML: {ex.Message}");
                }
            }
            if (table.Views.Count(v => v.Name == null) != 1)
            {
                errors.Add($"{name}: needs exactly one view with \"default\": true.");
            }
        }

        foreach (var role in model.Roles)
        foreach (var table in role.Tables.Keys.Where(t => !tableNames.Contains(t)))
        {
            errors.Add($"security.json: role \"{role.Name}\" refers to unknown table {table}.");
        }

        foreach (var team in model.OwnerTeams)
        foreach (var role in team.Roles.Where(r => model.Roles.All(x => x.Name != r)))
        {
            errors.Add($"security.json: team \"{team.Name}\" refers to unknown role \"{role}\".");
        }

        foreach (var profile in model.FieldSecurityProfiles)
        {
            if (model.OwnerTeams.All(t => t.Name != profile.TeamName)) errors.Add($"security.json: profile \"{profile.Name}\" team {profile.TeamName} is not in ownerTeams.");
            foreach (var (table, column) in profile.Columns)
            {
                var def = model.Table(table)?.Column(column);
                if (def == null) errors.Add($"security.json: profile \"{profile.Name}\": {table}.{column} doesn't exist.");
                else if (!def.Secured) errors.Add($"security.json: profile \"{profile.Name}\": {table}.{column} must have \"secured\": true.");
            }
        }

        foreach (var variable in model.EnvironmentVariables.Where(v => !v.SchemaName.StartsWith(Conventions.Prefix + "_")))
        {
            errors.Add($"environment-variables.json: {variable.SchemaName} must start with {Conventions.Prefix}_.");
        }
    }

    private static readonly Dictionary<string, int> Stages = new()
    {
        ["PreValidation"] = 10,
        ["PreOperation"] = 20,
        ["PostOperation"] = 40,
    };

    private static PluginStepDef ToPluginStep(PluginStepJson json, List<string> errors)
    {
        if (!Stages.TryGetValue(json.Stage ?? "", out var stage))
        {
            errors.Add($"plugins.json: {json.Plugin} {json.Message}: stage must be PreValidation, PreOperation or PostOperation.");
            return null;
        }
        return new PluginStepDef(
            json.Plugin,
            json.Message,
            json.Table?.ToLowerInvariant(),
            stage,
            json.FilteringAttributes is { Length: > 0 } ? string.Join(",", json.FilteringAttributes) : null,
            json.PreImage is { Length: > 0 } ? string.Join(",", json.PreImage) : null);
    }

    /// <summary>System columns plugin steps may filter on or include in images.</summary>
    private static readonly HashSet<string> SystemColumns = new() { "ownerid", "statecode", "statuscode", "createdon", "modifiedon" };

    private static void ValidatePluginSteps(HrModel model, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(model.PluginAssembly)) errors.Add("plugins.json: \"assembly\" is missing.");
        foreach (var step in model.PluginSteps)
        {
            var where = $"plugins.json: {step.Plugin} {step.Message}";
            if (string.IsNullOrWhiteSpace(step.Plugin) || !step.Plugin.StartsWith(model.PluginAssembly + "."))
            {
                errors.Add($"{where}: plugin must be a full class name in {model.PluginAssembly}.");
            }
            if (step.Message is not ("Create" or "Update" or "Delete"))
            {
                errors.Add($"{where}: message must be Create, Update or Delete.");
            }
            var table = model.Table(step.Table ?? "");
            if (table == null)
            {
                errors.Add($"{where}: table {step.Table} is not in the model.");
                continue;
            }
            var columns = table.AllColumns.Select(c => c.LogicalName).ToHashSet();
            foreach (var column in (step.FilteringAttributes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                         .Concat((step.PreImageAttributes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)))
            {
                if (!columns.Contains(column) && !SystemColumns.Contains(column)) errors.Add($"{where}: column {column} is not in {step.Table}.");
            }
            if (step.FilteringAttributes != null && step.Message != "Update") errors.Add($"{where}: filteringAttributes only apply to Update.");
            if (step.PreImageAttributes != null && step.Message == "Create") errors.Add($"{where}: Create steps can't have a preImage.");
        }
        foreach (var duplicate in model.PluginSteps.GroupBy(s => (s.Plugin, s.Message, s.Table, s.Stage)).Where(g => g.Count() > 1))
        {
            errors.Add($"plugins.json: {duplicate.Key.Plugin} {duplicate.Key.Message} on {duplicate.Key.Table} is listed twice.");
        }
    }

    /// <summary>Custom API parameter types and their Dataverse option values.</summary>
    public static readonly Dictionary<string, int> CustomApiTypes = new()
    {
        ["Boolean"] = 0, ["DateTime"] = 1, ["Decimal"] = 2, ["Integer"] = 7, ["Money"] = 8,
        ["String"] = 10, ["StringArray"] = 11, ["Guid"] = 12,
    };

    private static void ValidateCustomApis(HrModel model, List<string> errors)
    {
        foreach (var api in model.CustomApis)
        {
            var where = $"custom-apis.json: {api.UniqueName}";
            if (string.IsNullOrWhiteSpace(api.UniqueName) || !api.UniqueName.StartsWith(Conventions.Prefix + "_"))
            {
                errors.Add($"{where}: uniqueName must start with {Conventions.Prefix}_.");
            }
            if (string.IsNullOrWhiteSpace(api.Plugin) || !api.Plugin.StartsWith(model.PluginAssembly + "."))
            {
                errors.Add($"{where}: plugin must be a full class name in {model.PluginAssembly}.");
            }
            foreach (var field in api.Parameters.Concat(api.Responses))
            {
                if (string.IsNullOrWhiteSpace(field.Name)) errors.Add($"{where}: every parameter and response needs a name.");
                if (!CustomApiTypes.ContainsKey(field.Type ?? "")) errors.Add($"{where}.{field.Name}: type must be one of {string.Join(", ", CustomApiTypes.Keys)}.");
            }
        }
        foreach (var duplicate in model.CustomApis.GroupBy(a => a.UniqueName).Where(g => g.Count() > 1))
        {
            errors.Add($"custom-apis.json: {duplicate.Key} is defined twice.");
        }
    }

    private static T Invalid<T>(List<string> errors, string message, T fallback)
    {
        errors.Add(message);
        return fallback;
    }

    // JSON shapes. Property names match the files (case-insensitive).
    private sealed class ChoicesFile { public List<ChoiceJson> Choices { get; set; } = new(); }
    private sealed class ChoiceJson { public string Name { get; set; } public string Label { get; set; } public string[] Options { get; set; } }

    private sealed class TableJson
    {
        public int Phase { get; set; }
        public string Name { get; set; }
        public string Label { get; set; }
        public string PluralLabel { get; set; }
        public string Description { get; set; }
        public string Ownership { get; set; }
        public bool HasNotes { get; set; }
        public bool Audited { get; set; }
        public ColumnJson PrimaryName { get; set; }
        public List<ColumnJson> Columns { get; set; } = new();
        public List<KeyJson> Keys { get; set; } = new();
        public List<TabJson> Form { get; set; } = new();
        public List<ViewJson> Views { get; set; } = new();
        public string[] QuickFind { get; set; }
    }

    private sealed class ColumnJson
    {
        public string Name { get; set; }
        public string Label { get; set; }
        public string Type { get; set; }
        public string Required { get; set; }
        public string Description { get; set; }
        public int? MaxLength { get; set; }
        public int? Min { get; set; }
        public int? Max { get; set; }
        public int? Precision { get; set; }
        public string AutoNumber { get; set; }
        public string Choice { get; set; }
        public string[] Options { get; set; }
        public string Default { get; set; }
        public string Target { get; set; }
        public string OnDelete { get; set; }
        public int? MaxSizeKb { get; set; }
        public bool Secured { get; set; }
        public bool Audited { get; set; }
        public bool ReadOnly { get; set; }
    }

    private sealed class KeyJson { public string Name { get; set; } public string Label { get; set; } public string[] Columns { get; set; } }
    private sealed class TabJson { public string Tab { get; set; } public string Label { get; set; } public string[] Left { get; set; } public string[] Right { get; set; } }

    private sealed class ViewJson
    {
        public bool Default { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string[] Columns { get; set; }
        public string Filter { get; set; }
        public string Join { get; set; }
        public string Sort { get; set; }
    }

    private sealed class SecurityFile
    {
        public List<OwnerTeamJson> OwnerTeams { get; set; } = new();
        public List<RoleJson> Roles { get; set; } = new();
        public List<ProfileJson> ColumnSecurityProfiles { get; set; } = new();
    }

    private sealed class RoleJson { public string Name { get; set; } public string Description { get; set; } public Dictionary<string, string> Tables { get; set; } = new(); }
    private sealed class ProfileJson { public string Name { get; set; } public string Description { get; set; } public string Team { get; set; } public string[] Columns { get; set; } = Array.Empty<string>(); }

    private sealed class OwnerTeamJson { public string Name { get; set; } public string[] Roles { get; set; } }

    private sealed class PluginsFile
    {
        public string Assembly { get; set; }
        public List<PluginStepJson> Steps { get; set; } = new();
    }

    private sealed class PluginStepJson
    {
        public string Plugin { get; set; }
        public string Message { get; set; }
        public string Table { get; set; }
        public string Stage { get; set; }
        public string[] FilteringAttributes { get; set; }
        public string[] PreImage { get; set; }
    }

    private sealed class SampleFile { public List<SampleGroupJson> Groups { get; set; } = new(); }

    private sealed class SampleGroupJson
    {
        public string Table { get; set; }
        public string[] Key { get; set; }
        public bool Upsert { get; set; }
        public List<Dictionary<string, JsonElement>> Rows { get; set; }
    }

    private sealed class CustomApisFile { public List<CustomApiJson> CustomApis { get; set; } = new(); }

    private sealed class CustomApiJson
    {
        public string UniqueName { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string Plugin { get; set; }
        public bool IsFunction { get; set; }
        public List<CustomApiFieldJson> Parameters { get; set; }
        public List<CustomApiFieldJson> Responses { get; set; }
    }

    private sealed class CustomApiFieldJson
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public bool Optional { get; set; }
        public string Description { get; set; }
    }

    private sealed class EnvironmentVariablesFile { public List<VariableJson> EnvironmentVariables { get; set; } = new(); }
    private sealed class VariableJson { public string Name { get; set; } public string Label { get; set; } public string Type { get; set; } public string Default { get; set; } public string Description { get; set; } }
}
