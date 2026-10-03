using System.Text.Json;
using HRA.Provisioner.Model;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace HRA.Provisioner;

/// <summary>
/// Loads model/sample-data.json group by group. Records are matched by the group's key columns:
/// new ones are created, existing ones are skipped (or updated when the group has "upsert": true).
/// Run after register-plugins so the plugins set owners and history.
/// </summary>
public sealed class Seeder
{
    private readonly IOrganizationService _service;
    private readonly HrModel _model;
    private readonly Dictionary<(string Table, string Key), Guid> _ids = new();
    private readonly Dictionary<string, string> _lookupKeyColumn = new();
    private Guid? _me;

    public Seeder(IOrganizationService service, HrModel model)
    {
        _service = service;
        _model = model;
    }

    public void Run()
    {
        if (_model.SampleData.Count == 0)
        {
            Log.Warn("model/sample-data.json has no groups; nothing to load.");
            return;
        }

        foreach (var group in _model.SampleData)
        {
            var table = _model.Table(group.Table);
            _lookupKeyColumn.TryAdd(table.LogicalName, group.Key[0]);
            Log.Step($"{table.PluralLabel}: {group.Rows.Count} row(s){(group.Upsert ? " (update existing)" : "")}");

            int created = 0, updated = 0, skipped = 0;
            foreach (var row in group.Rows)
            {
                var record = new Entity(table.LogicalName);
                foreach (var (name, value) in row)
                {
                    record[name] = SampleValues.Convert(_model, table, table.Column(name), value, ResolveLookup);
                }

                var label = Label(table, group, row);
                var existingId = FindExisting(table, group.Key, record);
                if (existingId.HasValue)
                {
                    if (group.Upsert)
                    {
                        record.Id = existingId.Value;
                        _service.Update(record);
                        updated++;
                        Log.Ok($"{label} updated");
                    }
                    else
                    {
                        skipped++;
                        Log.Skip($"{label} exists");
                    }
                }
                else
                {
                    existingId = _service.Create(record);
                    created++;
                    Log.Ok($"{label} created");
                }

                if (group.Key.Length == 1 && row[group.Key[0]].ValueKind == JsonValueKind.String)
                {
                    _ids[(table.LogicalName, row[group.Key[0]].GetString())] = existingId.Value;
                }
            }
            Log.Info($"{created} created, {updated} updated, {skipped} already there");
        }
    }

    private Guid? FindExisting(TableDef table, string[] key, Entity record)
    {
        var query = new QueryExpression(table.LogicalName) { ColumnSet = new ColumnSet(false), TopCount = 1 };
        foreach (var column in key)
        {
            var value = record[column] is EntityReference reference ? reference.Id : record[column];
            query.Criteria.AddCondition(column, ConditionOperator.Equal, value);
        }
        return _service.RetrieveMultiple(query).Entities.FirstOrDefault()?.Id;
    }

    private EntityReference ResolveLookup(string target, string key)
    {
        if (target == "systemuser" && key == SampleValues.CurrentUser)
        {
            _me ??= ((WhoAmIResponse)_service.Execute(new WhoAmIRequest())).UserId;
            return new EntityReference("systemuser", _me.Value);
        }

        if (_ids.TryGetValue((target, key), out var id)) return new EntityReference(target, id);

        // Not loaded in this run (e.g. created earlier): find it by the target table's key column.
        if (_lookupKeyColumn.TryGetValue(target, out var keyColumn))
        {
            var match = _service.FindOne(target, new[] { keyColumn }, (keyColumn, key));
            if (match != null)
            {
                _ids[(target, key)] = match.Id;
                return match.ToEntityReference();
            }
        }
        throw new InvalidOperationException($"Sample data refers to {target} '{key}', which wasn't found.");
    }

    private static string Label(TableDef table, SampleGroup group, Dictionary<string, JsonElement> row)
    {
        string Text(string column) => row.TryGetValue(column, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var name = Text(table.PrimaryName.LogicalName)
            ?? (row.ContainsKey("hra_firstname") ? $"{Text("hra_firstname")} {Text("hra_lastname")}" : null);
        var key = string.Join(" / ", group.Key.Select(Text));
        return name != null && name != key ? $"{name} ({key})" : key;
    }
}
