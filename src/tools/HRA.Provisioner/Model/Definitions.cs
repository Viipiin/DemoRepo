using Microsoft.Xrm.Sdk.Metadata;

namespace HRA.Provisioner.Model;

public enum ColumnType
{
    Text,
    Email,
    Phone,
    Url,
    Memo,
    WholeNumber,
    Decimal,
    Currency,
    DateOnly,
    DateTime,
    YesNo,
    Choice,
    GlobalChoice,
    Lookup,
    File,
}

public enum Requirement
{
    Optional,
    Recommended,
    Required,
}

/// <summary>How a lookup behaves when its parent record is deleted or reassigned.</summary>
public enum LookupBehaviour
{
    /// <summary>Delete: remove link. Nothing else cascades.</summary>
    RemoveLink,
    /// <summary>Delete: restrict (parent can't be deleted while children exist). Nothing else cascades.</summary>
    Restrict,
    /// <summary>Parental: everything cascades, including delete.</summary>
    Parental,
}

public sealed class ChoiceDef
{
    public ChoiceDef(string name, string label, params string[] options)
    {
        Name = name;
        Label = label;
        Options = options;
    }

    /// <summary>Global choice name, e.g. hra_employmenttype. Ignored for local choices.</summary>
    public string Name { get; }
    public string Label { get; }
    public string[] Options { get; }

    /// <summary>Option values start at the publisher's choice value prefix: 817990000, 817990001, ...</summary>
    public static int ValueOf(int index) => Conventions.ChoiceValueBase + index;
}

public sealed record ColumnDef
{
    public ColumnDef(string schemaName, string label, ColumnType type)
    {
        SchemaName = schemaName;
        Label = label;
        Type = type;
    }

    public string SchemaName { get; }
    public string LogicalName => SchemaName.ToLowerInvariant();
    public string Label { get; }
    public ColumnType Type { get; }
    public string Description { get; init; }
    public Requirement Requirement { get; init; } = Requirement.Optional;
    public int? MaxLength { get; init; }
    public int? MinValue { get; init; }
    public int? MaxValue { get; init; }
    public int Precision { get; init; } = 2;
    public string AutoNumberFormat { get; init; }
    public bool Secured { get; init; }
    public bool Audited { get; init; }
    /// <summary>Global choice name for GlobalChoice columns.</summary>
    public string GlobalChoice { get; init; }
    /// <summary>Options for local Choice columns.</summary>
    public string[] Options { get; init; }
    /// <summary>Index into Options (or the global choice) used as the default value.</summary>
    public int? DefaultOption { get; init; }
    /// <summary>Logical name of the referenced table for Lookup columns.</summary>
    public string Target { get; init; }
    public LookupBehaviour Behaviour { get; init; } = LookupBehaviour.RemoveLink;
    public int MaxSizeKb { get; init; } = 10240;
    /// <summary>Shown read-only on generated forms (set by plugins).</summary>
    public bool ReadOnlyOnForm { get; init; }
}

public sealed class KeyDef
{
    public KeyDef(string schemaName, string label, params string[] columns)
    {
        SchemaName = schemaName;
        Label = label;
        Columns = columns;
    }

    public string SchemaName { get; }
    public string Label { get; }
    public string[] Columns { get; }
}

public sealed class FormTab
{
    public FormTab(string name, string label, string[] left, string[] right = null)
    {
        Name = name;
        Label = label;
        Left = left;
        Right = right ?? Array.Empty<string>();
    }

    public string Name { get; }
    public string Label { get; }
    public string[] Left { get; }
    public string[] Right { get; }
}

public sealed class ViewDef
{
    public ViewDef(string name, string[] columns)
    {
        Name = name;
        Columns = columns;
    }

    /// <summary>Null means "update the table's default Active view".</summary>
    public string Name { get; }
    public string[] Columns { get; }
    public string Description { get; init; }
    /// <summary>Extra FetchXML condition elements added to the statecode = Active filter.</summary>
    public string ExtraConditions { get; init; } = "";
    public string SortColumn { get; init; }
}

public sealed class TableDef
{
    public TableDef(string schemaName, string label, string pluralLabel)
    {
        SchemaName = schemaName;
        Label = label;
        PluralLabel = pluralLabel;
    }

    public string SchemaName { get; }
    public string LogicalName => SchemaName.ToLowerInvariant();
    public string Label { get; }
    public string PluralLabel { get; }
    /// <summary>Build phase the table belongs to (informational).</summary>
    public int Phase { get; init; }
    public string Description { get; init; }
    public bool OrganizationOwned { get; init; }
    public bool HasNotes { get; init; }
    public bool Audited { get; init; }
    public ColumnDef PrimaryName { get; init; }
    public List<ColumnDef> Columns { get; init; } = new();
    public List<KeyDef> Keys { get; init; } = new();
    public List<FormTab> Form { get; init; } = new();
    public List<ViewDef> Views { get; init; } = new();
    /// <summary>Columns searched by Quick Find, in addition to the primary name.</summary>
    public string[] QuickFind { get; init; } = Array.Empty<string>();

    public IEnumerable<ColumnDef> AllColumns => new[] { PrimaryName }.Concat(Columns);

    public ColumnDef Column(string logicalName) =>
        AllColumns.FirstOrDefault(c => c.LogicalName == logicalName);

    public OwnershipTypes Ownership => OrganizationOwned ? OwnershipTypes.OrganizationOwned : OwnershipTypes.UserOwned;
}

/// <summary>Privilege depth per role, for one table.</summary>
public enum Depth
{
    None,
    User,
    BusinessUnit,
    Organization,
}

/// <summary>Privileges a role has on a table. Letters: C R W D A(ppend) T (append To) S (assign + share).</summary>
public sealed class Access
{
    public Access(string privileges, Depth depth)
    {
        Privileges = privileges;
        Depth = depth;
    }

    public string Privileges { get; }
    public Depth Depth { get; }

    public static Access Org(string privileges) => new(privileges, Depth.Organization);
    public static Access User(string privileges) => new(privileges, Depth.User);
}

public sealed class RoleDef
{
    public RoleDef(string name, string description)
    {
        Name = name;
        Description = description;
    }

    public string Name { get; }
    public string Description { get; }
    /// <summary>Table logical name -> access.</summary>
    public Dictionary<string, Access> Tables { get; } = new();
}

public sealed class FieldSecurityProfileDef
{
    public FieldSecurityProfileDef(string name, string description, string teamName)
    {
        Name = name;
        Description = description;
        TeamName = teamName;
    }

    public string Name { get; }
    public string Description { get; }
    /// <summary>Owner team whose members get full access to the secured columns.</summary>
    public string TeamName { get; }
    /// <summary>(table logical name, column logical name).</summary>
    public List<(string Table, string Column)> Columns { get; } = new();
}

public sealed class EnvironmentVariableDef
{
    public EnvironmentVariableDef(string schemaName, string label, string defaultValue, string description)
    {
        SchemaName = schemaName;
        Label = label;
        DefaultValue = defaultValue;
        Description = description;
    }

    public string SchemaName { get; }
    public string Label { get; }
    public string DefaultValue { get; }
    public string Description { get; }
}

public static class Conventions
{
    public const string SolutionUniqueName = "HRAutomation";
    public const string Prefix = "hra";
    public const int ChoiceValueBase = 817990000;
    public const int LanguageCode = 1033;
}
