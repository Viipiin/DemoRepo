namespace HRA.Provisioner.Model;

/// <summary>Short helpers to keep the table definitions readable.</summary>
public static class Col
{
    public static ColumnDef Name(string schema, string label, int length = 100, Requirement req = Requirement.Required) =>
        new(schema, label, ColumnType.Text) { MaxLength = length, Requirement = req };

    public static ColumnDef AutoNumber(string schema, string label, string format, int length = 20) =>
        new(schema, label, ColumnType.Text) { MaxLength = length, AutoNumberFormat = format, ReadOnlyOnForm = true };

    public static ColumnDef Text(string schema, string label, int length, Requirement req = Requirement.Optional) =>
        new(schema, label, ColumnType.Text) { MaxLength = length, Requirement = req };

    public static ColumnDef Secured(string schema, string label, int length) =>
        new(schema, label, ColumnType.Text) { MaxLength = length, Secured = true, Audited = true };

    public static ColumnDef Email(string schema, string label, Requirement req = Requirement.Optional) =>
        new(schema, label, ColumnType.Email) { MaxLength = 100, Requirement = req };

    public static ColumnDef Phone(string schema, string label, Requirement req = Requirement.Optional) =>
        new(schema, label, ColumnType.Phone) { MaxLength = 20, Requirement = req };

    public static ColumnDef Memo(string schema, string label, int length, Requirement req = Requirement.Optional) =>
        new(schema, label, ColumnType.Memo) { MaxLength = length, Requirement = req };

    public static ColumnDef Int(string schema, string label, int min, int max, Requirement req = Requirement.Optional) =>
        new(schema, label, ColumnType.WholeNumber) { MinValue = min, MaxValue = max, Requirement = req };

    public static ColumnDef Decimal(string schema, string label, int precision, int min = 0, int max = 1000) =>
        new(schema, label, ColumnType.Decimal) { Precision = precision, MinValue = min, MaxValue = max };

    public static ColumnDef Money(string schema, string label, bool secured = false) =>
        new(schema, label, ColumnType.Currency) { Secured = secured, Audited = secured };

    public static ColumnDef Date(string schema, string label, Requirement req = Requirement.Optional) =>
        new(schema, label, ColumnType.DateOnly) { Requirement = req };

    public static ColumnDef DateTime(string schema, string label, Requirement req = Requirement.Optional) =>
        new(schema, label, ColumnType.DateTime) { Requirement = req };

    public static ColumnDef YesNo(string schema, string label) =>
        new(schema, label, ColumnType.YesNo);

    public static ColumnDef Choice(string schema, string label, Requirement req, int? defaultOption, params string[] options) =>
        new(schema, label, ColumnType.Choice) { Requirement = req, Options = options, DefaultOption = defaultOption };

    public static ColumnDef Global(string schema, string label, string choice, Requirement req = Requirement.Optional, int? defaultOption = null) =>
        new(schema, label, ColumnType.GlobalChoice) { GlobalChoice = choice, Requirement = req, DefaultOption = defaultOption };

    public static ColumnDef Lookup(string schema, string label, string target, LookupBehaviour behaviour = LookupBehaviour.RemoveLink, Requirement req = Requirement.Optional) =>
        new(schema, label, ColumnType.Lookup) { Target = target, Behaviour = behaviour, Requirement = req };

    public static ColumnDef File(string schema, string label, int maxSizeKb = 10240) =>
        new(schema, label, ColumnType.File) { MaxSizeKb = maxSizeKb };
}
