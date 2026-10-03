using System.Security;
using System.Text;
using HRA.Provisioner.Model;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace HRA.Provisioner;

/// <summary>
/// Rewrites each table's main form and default views from the model, and adds extra named views.
/// Safe to re-run: the generated layout replaces the previous one. Manual form tweaks made in the
/// designer are overwritten on the next run, so make lasting changes in the model instead.
/// </summary>
public sealed class FormAndViewProvisioner
{
    private const int MainFormType = 2;
    private const int PublicViewType = 0;
    private const int QuickFindViewType = 4;

    private readonly IOrganizationService _service;
    private readonly HrModel _model;

    public FormAndViewProvisioner(IOrganizationService service, HrModel model)
    {
        _service = service;
        _model = model;
    }

    public void Run()
    {
        Log.Step("Forms and views");
        foreach (var table in _model.Tables)
        {
            var metadata = ((RetrieveEntityResponse)_service.Execute(new RetrieveEntityRequest
            {
                LogicalName = table.LogicalName,
                EntityFilters = EntityFilters.Attributes,
            })).EntityMetadata;

            UpdateMainForm(table, metadata);
            foreach (var view in table.Views) UpsertView(table, metadata, view);
            UpdateQuickFind(table, metadata);
        }
    }

    private void UpdateMainForm(TableDef table, EntityMetadata metadata)
    {
        var query = new QueryExpression("systemform") { ColumnSet = new ColumnSet("name", "isdefault") };
        query.Criteria.AddCondition("objecttypecode", ConditionOperator.Equal, table.LogicalName);
        query.Criteria.AddCondition("type", ConditionOperator.Equal, MainFormType);
        // systemform has no createdon column, so prefer the table's default main form ("Information").
        var forms = _service.RetrieveMultiple(query).Entities;
        var form = forms.FirstOrDefault(f => f.GetAttributeValue<bool>("isdefault")) ?? forms.FirstOrDefault();
        if (form == null)
        {
            Log.Warn($"{table.LogicalName}: no main form found, skipped");
            return;
        }

        var xml = BuildFormXml(table, metadata);
        _service.Update(new Entity("systemform", form.Id) { ["formxml"] = xml });
        Log.Ok($"{table.LogicalName}: main form '{form.GetAttributeValue<string>("name")}' laid out ({table.Form.Count} tabs)");
    }

    private static string BuildFormXml(TableDef table, EntityMetadata metadata)
    {
        var sb = new StringBuilder("<form><tabs>");
        foreach (var tab in table.Form)
        {
            sb.Append($"<tab name=\"tab_{tab.Name}\" id=\"{NewId()}\" IsUserDefined=\"0\" locklevel=\"0\" showlabel=\"true\" expanded=\"true\">");
            sb.Append(Labels(tab.Label));
            sb.Append("<columns>");
            AppendColumn(sb, table, metadata, tab.Name + "_left", tab.Label, tab.Left, tab.Right.Length > 0 ? "50%" : "100%");
            if (tab.Right.Length > 0) AppendColumn(sb, table, metadata, tab.Name + "_right", tab.Label, tab.Right, "50%");
            sb.Append("</columns></tab>");
        }
        sb.Append("</tabs></form>");
        return sb.ToString();
    }

    private static void AppendColumn(StringBuilder sb, TableDef table, EntityMetadata metadata, string sectionName, string label, string[] fields, string width)
    {
        sb.Append($"<column width=\"{width}\"><sections>");
        sb.Append($"<section name=\"sec_{sectionName}\" showlabel=\"false\" showbar=\"false\" locklevel=\"0\" id=\"{NewId()}\" IsUserDefined=\"0\" layout=\"varwidth\" columns=\"1\" labelwidth=\"140\" celllabelalignment=\"Left\" celllabelposition=\"Left\">");
        sb.Append(Labels(label));
        sb.Append("<rows>");
        foreach (var field in fields)
        {
            var attribute = metadata.Attributes.FirstOrDefault(a => a.LogicalName == field);
            if (attribute == null)
            {
                Log.Warn($"{table.LogicalName}: column {field} not found, left off the form");
                continue;
            }
            var classId = ControlClassId(attribute);
            if (classId == null)
            {
                Log.Warn($"{table.LogicalName}: column {field} has no standard control, left off the form");
                continue;
            }
            var readOnly = table.Column(field)?.ReadOnlyOnForm == true;
            var fieldLabel = attribute.DisplayName?.UserLocalizedLabel?.Label ?? field;
            sb.Append($"<row><cell id=\"{NewId()}\" showlabel=\"true\" locklevel=\"0\">");
            sb.Append(Labels(fieldLabel));
            sb.Append($"<control id=\"{field}\" classid=\"{classId}\" datafieldname=\"{field}\" disabled=\"{(readOnly ? "true" : "false")}\" />");
            sb.Append("</cell></row>");
        }
        sb.Append("</rows></section></sections></column>");
    }

    /// <summary>Standard form control class ids by column type.</summary>
    private static string ControlClassId(AttributeMetadata attribute) => attribute switch
    {
        StringAttributeMetadata s when s.FormatName == StringFormatName.Email => "{ADA2203E-B4CD-49be-9DDF-234642B43B52}",
        StringAttributeMetadata s when s.FormatName == StringFormatName.Phone => "{8C10015A-B339-4982-9474-A95FE05631A5}",
        StringAttributeMetadata s when s.FormatName == StringFormatName.Url => "{71716B6C-711E-476c-8AB8-5D11542BFB47}",
        StringAttributeMetadata => "{4273EDBD-AC1D-40d3-9FB2-095C621B552D}",
        MemoAttributeMetadata => "{E0DECE4B-6FC8-4a8f-A065-082708572369}",
        IntegerAttributeMetadata => "{C6D124CA-7EDA-4a60-AEA9-7FB8D318B68F}",
        DecimalAttributeMetadata => "{C3EFE0C3-0EC6-42be-8349-CBD9079DFD8E}",
        MoneyAttributeMetadata => "{533B9E00-756B-4312-95A0-DC888637AC78}",
        DateTimeAttributeMetadata => "{5B773807-9FB2-42db-97C3-7A91EFF8ADFF}",
        BooleanAttributeMetadata => "{67FAC785-CD58-4f9f-ABB3-4B7DDC6ED5ED}",
        PicklistAttributeMetadata => "{3EF39988-22BB-4f0b-BBBE-64B5A3748AEE}",
        LookupAttributeMetadata => "{270BD3DB-D9AF-4782-9025-509E298DEC0A}",
        _ => null,
    };

    private void UpsertView(TableDef table, EntityMetadata metadata, ViewDef view)
    {
        var fetchXml = BuildFetchXml(table, metadata, view);
        var layoutXml = BuildLayoutXml(table, metadata, view.Columns);

        var query = new QueryExpression("savedquery") { ColumnSet = new ColumnSet("name"), TopCount = 1 };
        query.Criteria.AddCondition("returnedtypecode", ConditionOperator.Equal, table.LogicalName);
        query.Criteria.AddCondition("querytype", ConditionOperator.Equal, PublicViewType);
        if (view.Name == null) query.Criteria.AddCondition("isdefault", ConditionOperator.Equal, true);
        else query.Criteria.AddCondition("name", ConditionOperator.Equal, view.Name);
        var existing = _service.RetrieveMultiple(query).Entities.FirstOrDefault();

        if (existing != null)
        {
            _service.Update(new Entity("savedquery", existing.Id) { ["fetchxml"] = fetchXml, ["layoutxml"] = layoutXml });
            Log.Ok($"{table.LogicalName}: view '{existing.GetAttributeValue<string>("name")}' updated");
            return;
        }
        if (view.Name == null)
        {
            Log.Warn($"{table.LogicalName}: no default public view found, skipped");
            return;
        }

        var create = new CreateRequest
        {
            Target = new Entity("savedquery")
            {
                ["name"] = view.Name,
                ["description"] = view.Description ?? view.Name,
                ["returnedtypecode"] = table.LogicalName,
                ["querytype"] = PublicViewType,
                ["fetchxml"] = fetchXml,
                ["layoutxml"] = layoutXml,
            },
        };
        create.Parameters["SolutionUniqueName"] = Conventions.SolutionUniqueName;
        _service.Execute(create);
        Log.Ok($"{table.LogicalName}: view '{view.Name}' created");
    }

    private void UpdateQuickFind(TableDef table, EntityMetadata metadata)
    {
        var query = new QueryExpression("savedquery") { ColumnSet = new ColumnSet("name"), TopCount = 1 };
        query.Criteria.AddCondition("returnedtypecode", ConditionOperator.Equal, table.LogicalName);
        query.Criteria.AddCondition("querytype", ConditionOperator.Equal, QuickFindViewType);
        var quickFind = _service.RetrieveMultiple(query).Entities.FirstOrDefault();
        if (quickFind == null) return;

        var defaultView = table.Views.First(v => v.Name == null);
        var findColumns = new[] { metadata.PrimaryNameAttribute }.Concat(table.QuickFind).Distinct().ToArray();
        var sb = new StringBuilder($"<fetch version=\"1.0\" mapping=\"logical\"><entity name=\"{table.LogicalName}\">");
        foreach (var column in ViewAttributes(metadata, defaultView.Columns)) sb.Append($"<attribute name=\"{column}\" />");
        sb.Append($"<order attribute=\"{metadata.PrimaryNameAttribute}\" descending=\"false\" />");
        sb.Append("<filter type=\"and\"><condition attribute=\"statecode\" operator=\"eq\" value=\"0\" /></filter>");
        sb.Append("<filter type=\"or\" isquickfindfields=\"1\">");
        foreach (var column in findColumns) sb.Append($"<condition attribute=\"{column}\" operator=\"like\" value=\"{{0}}\" />");
        sb.Append("</filter></entity></fetch>");

        _service.Update(new Entity("savedquery", quickFind.Id)
        {
            ["fetchxml"] = sb.ToString(),
            ["layoutxml"] = BuildLayoutXml(table, metadata, defaultView.Columns),
        });
        Log.Ok($"{table.LogicalName}: quick find searches {string.Join(", ", findColumns)}");
    }

    private static string BuildFetchXml(TableDef table, EntityMetadata metadata, ViewDef view)
    {
        var sb = new StringBuilder($"<fetch version=\"1.0\" mapping=\"logical\"><entity name=\"{table.LogicalName}\">");
        foreach (var column in ViewAttributes(metadata, view.Columns)) sb.Append($"<attribute name=\"{column}\" />");
        sb.Append($"<order attribute=\"{view.SortColumn ?? metadata.PrimaryNameAttribute}\" descending=\"false\" />");
        sb.Append("<filter type=\"and\"><condition attribute=\"statecode\" operator=\"eq\" value=\"0\" />");
        sb.Append(view.ExtraConditions);
        sb.Append("</filter></entity></fetch>");
        return sb.ToString();
    }

    private static IEnumerable<string> ViewAttributes(EntityMetadata metadata, string[] columns) =>
        new[] { metadata.PrimaryIdAttribute, metadata.PrimaryNameAttribute }.Concat(columns).Distinct();

    private static string BuildLayoutXml(TableDef table, EntityMetadata metadata, string[] columns)
    {
        var sb = new StringBuilder(
            $"<grid name=\"resultset\" object=\"{metadata.ObjectTypeCode}\" jump=\"{metadata.PrimaryNameAttribute}\" select=\"1\" icon=\"1\" preview=\"1\">" +
            $"<row name=\"result\" id=\"{metadata.PrimaryIdAttribute}\">");
        var cells = new[] { metadata.PrimaryNameAttribute }.Concat(columns).Distinct();
        foreach (var column in cells)
        {
            var width = column == metadata.PrimaryNameAttribute ? 200 : 150;
            sb.Append($"<cell name=\"{column}\" width=\"{width}\" />");
        }
        sb.Append("</row></grid>");
        return sb.ToString();
    }

    private static string Labels(string text) =>
        $"<labels><label description=\"{SecurityElement.Escape(text)}\" languagecode=\"{Conventions.LanguageCode}\" /></labels>";

    private static string NewId() => "{" + Guid.NewGuid() + "}";
}
