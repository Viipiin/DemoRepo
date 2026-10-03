namespace HRA.Provisioner.Model;

/// <summary>The whole HR Automation model, loaded from the JSON files in model/.</summary>
public sealed class HrModel
{
    public HrModel(
        IReadOnlyList<ChoiceDef> choices,
        IReadOnlyList<TableDef> tables,
        IReadOnlyList<OwnerTeamDef> ownerTeams,
        IReadOnlyList<RoleDef> roles,
        IReadOnlyList<FieldSecurityProfileDef> fieldSecurityProfiles,
        IReadOnlyList<EnvironmentVariableDef> environmentVariables,
        string pluginAssembly,
        IReadOnlyList<PluginStepDef> pluginSteps,
        IReadOnlyList<SampleGroup> sampleData)
    {
        Choices = choices;
        Tables = tables;
        OwnerTeams = ownerTeams;
        Roles = roles;
        FieldSecurityProfiles = fieldSecurityProfiles;
        EnvironmentVariables = environmentVariables;
        PluginAssembly = pluginAssembly;
        PluginSteps = pluginSteps;
        SampleData = sampleData;
    }

    public IReadOnlyList<ChoiceDef> Choices { get; }
    public IReadOnlyList<TableDef> Tables { get; }
    public IReadOnlyList<OwnerTeamDef> OwnerTeams { get; }
    public IReadOnlyList<RoleDef> Roles { get; }
    public IReadOnlyList<FieldSecurityProfileDef> FieldSecurityProfiles { get; }
    public IReadOnlyList<EnvironmentVariableDef> EnvironmentVariables { get; }
    /// <summary>Assembly name from plugins.json, e.g. HRAutomation.Plugins.</summary>
    public string PluginAssembly { get; }
    public IReadOnlyList<PluginStepDef> PluginSteps { get; }
    public IReadOnlyList<SampleGroup> SampleData { get; }

    public TableDef Table(string logicalName) =>
        Tables.FirstOrDefault(t => t.LogicalName == logicalName.ToLowerInvariant());

    /// <summary>
    /// Option value for a label of a local or global choice column, e.g. Option("hra_employee", "hra_gender", "Female").
    /// </summary>
    public int Option(string table, string column, string label)
    {
        var def = Table(table)?.Column(column)
            ?? throw new ArgumentException($"Column {table}.{column} is not in the model.");
        var options = def.Type == ColumnType.GlobalChoice
            ? Choices.First(c => c.Name == def.GlobalChoice).Options
            : def.Options ?? throw new ArgumentException($"Column {table}.{column} is not a choice column.");
        var index = Array.IndexOf(options, label);
        if (index < 0) throw new ArgumentException($"\"{label}\" is not an option of {table}.{column}.");
        return ChoiceDef.ValueOf(index);
    }
}
