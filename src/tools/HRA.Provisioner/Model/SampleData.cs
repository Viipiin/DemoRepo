using System.Globalization;
using System.Text.Json;
using Microsoft.Xrm.Sdk;

namespace HRA.Provisioner.Model;

/// <summary>
/// Converts and checks values in model/sample-data.json using the column types from the model:
/// choices are given by label, lookups by the key of a record in an earlier group, dates as yyyy-MM-dd,
/// and "@me" in a systemuser lookup means the signed-in user.
/// </summary>
public static class SampleValues
{
    public const string CurrentUser = "@me";

    /// <summary>Returns an error message, or null when the value suits the column.</summary>
    public static string Check(HrModel model, TableDef table, ColumnDef column, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        switch (column.Type)
        {
            case ColumnType.Text or ColumnType.Email or ColumnType.Phone or ColumnType.Url or ColumnType.Memo:
                if (value.ValueKind != JsonValueKind.String) return "must be text";
                return value.GetString()!.Length > (column.MaxLength ?? 100) ? $"is longer than {column.MaxLength} characters" : null;
            case ColumnType.WholeNumber:
                return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _) ? null : "must be a whole number";
            case ColumnType.Decimal or ColumnType.Currency:
                return value.ValueKind == JsonValueKind.Number ? null : "must be a number";
            case ColumnType.DateOnly or ColumnType.DateTime:
                return value.ValueKind == JsonValueKind.String && TryDate(value.GetString(), out _) ? null : "must be a date like 2026-04-01";
            case ColumnType.YesNo:
                return value.ValueKind is JsonValueKind.True or JsonValueKind.False ? null : "must be true or false";
            case ColumnType.Choice or ColumnType.GlobalChoice:
                if (value.ValueKind != JsonValueKind.String) return "must be the option's label";
                try
                {
                    model.Option(table.LogicalName, column.LogicalName, value.GetString());
                    return null;
                }
                catch (ArgumentException ex)
                {
                    return ex.Message.TrimEnd('.');
                }
            case ColumnType.Lookup:
                if (value.ValueKind != JsonValueKind.String) return "must be the key of the related record";
                if (column.Target == "systemuser" && value.GetString() != CurrentUser) return $"only \"{CurrentUser}\" (you) is supported for user lookups";
                return null;
            default:
                return $"{column.Type} columns can't be loaded from sample data";
        }
    }

    public static object Convert(HrModel model, TableDef table, ColumnDef column, JsonElement value, Func<string, string, EntityReference> resolveLookup)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        return column.Type switch
        {
            ColumnType.WholeNumber => value.GetInt32(),
            ColumnType.Decimal => value.GetDecimal(),
            ColumnType.Currency => new Money(value.GetDecimal()),
            ColumnType.DateOnly or ColumnType.DateTime => TryDate(value.GetString(), out var date) ? date : throw new FormatException(value.GetString()),
            ColumnType.YesNo => value.GetBoolean(),
            ColumnType.Choice or ColumnType.GlobalChoice => new OptionSetValue(model.Option(table.LogicalName, column.LogicalName, value.GetString())),
            ColumnType.Lookup => resolveLookup(column.Target, value.GetString()),
            _ => value.GetString(),
        };
    }

    private static bool TryDate(string text, out DateTime date) =>
        DateTime.TryParseExact(text, new[] { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}

/// <summary>Offline checks for model/sample-data.json, run by every command including 'validate'.</summary>
public static class SampleDataValidator
{
    public static void Validate(HrModel model, List<string> errors)
    {
        var keyColumns = new Dictionary<string, string[]>();
        var knownKeys = new Dictionary<string, HashSet<string>>();

        for (var g = 0; g < model.SampleData.Count; g++)
        {
            var group = model.SampleData[g];
            var where = $"sample-data.json group {g + 1} ({group.Table})";
            var table = model.Table(group.Table ?? "");
            if (table == null)
            {
                errors.Add($"{where}: table is not in the model.");
                continue;
            }
            if (group.Key.Length == 0 || group.Key.Any(k => table.Column(k) == null))
            {
                errors.Add($"{where}: \"key\" must list columns of the table.");
                continue;
            }
            if (keyColumns.TryGetValue(table.LogicalName, out var firstKey))
            {
                if (!firstKey.SequenceEqual(group.Key)) errors.Add($"{where}: use the same key as the first group for this table ({string.Join(", ", firstKey)}).");
            }
            else
            {
                keyColumns[table.LogicalName] = group.Key;
                knownKeys[table.LogicalName] = new HashSet<string>();
            }

            for (var r = 0; r < group.Rows.Count; r++)
            {
                var row = group.Rows[r];
                var rowWhere = $"{where} row {r + 1}";
                foreach (var key in group.Key.Where(k => !row.ContainsKey(k)))
                {
                    errors.Add($"{rowWhere}: key column {key} is missing.");
                }

                foreach (var (name, value) in row)
                {
                    var column = table.Column(name);
                    if (column == null)
                    {
                        errors.Add($"{rowWhere}: column {name} is not in {table.LogicalName}.");
                        continue;
                    }
                    var problem = SampleValues.Check(model, table, column, value);
                    if (problem != null)
                    {
                        errors.Add($"{rowWhere}: {name} {problem}.");
                        continue;
                    }
                    if (column.Type == ColumnType.Lookup && column.Target != "systemuser" && value.ValueKind == JsonValueKind.String)
                    {
                        if (!keyColumns.TryGetValue(column.Target, out var targetKey) || targetKey.Length != 1)
                        {
                            errors.Add($"{rowWhere}: {name} points to {column.Target}, which needs an earlier group with a single-column key.");
                        }
                        else if (!knownKeys[column.Target].Contains(value.GetString()!))
                        {
                            errors.Add($"{rowWhere}: {name} \"{value.GetString()}\" isn't a {column.Target} defined earlier in the file.");
                        }
                    }
                }

                if (group.Key.Length == 1 && row.TryGetValue(group.Key[0], out var keyValue) && keyValue.ValueKind == JsonValueKind.String)
                {
                    knownKeys[table.LogicalName].Add(keyValue.GetString()!);
                }
            }
        }
    }
}
