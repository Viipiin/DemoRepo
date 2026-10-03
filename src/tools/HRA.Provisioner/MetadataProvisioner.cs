using HRA.Provisioner.Model;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using static HRA.Provisioner.ServiceExtensions;

namespace HRA.Provisioner;

/// <summary>Creates global choices, tables, columns, relationships and alternate keys. Idempotent.</summary>
public sealed class MetadataProvisioner
{
    private readonly IOrganizationService _service;
    private const string Solution = Conventions.SolutionUniqueName;

    private readonly HrModel _model;

    public MetadataProvisioner(IOrganizationService service, HrModel model)
    {
        _service = service;
        _model = model;
    }

    public void Run()
    {
        Checker.EnsureSolution(_service);
        CreateGlobalChoices(_model.Choices);

        Log.Step("Tables");
        foreach (var table in _model.Tables) EnsureTable(table);

        Log.Step("Columns");
        foreach (var table in _model.Tables) EnsureColumns(table, lookups: false);

        Log.Step("Relationships (lookups)");
        foreach (var table in _model.Tables) EnsureColumns(table, lookups: true);

        Log.Step("Alternate keys");
        foreach (var table in _model.Tables) EnsureKeys(table);
    }

    public void PublishAll()
    {
        Log.Step("Publishing all customizations (can take a minute)");
        _service.Execute(new PublishAllXmlRequest());
        Log.Ok("Published");
    }

    private void CreateGlobalChoices(IEnumerable<ChoiceDef> choices)
    {
        Log.Step("Global choices");
        foreach (var choice in choices)
        {
            if (_service.TryExecute(new RetrieveOptionSetRequest { Name = choice.Name }, out var response))
            {
                var existing = (OptionSetMetadata)((RetrieveOptionSetResponse)response).OptionSetMetadata;
                var values = existing.Options.Select(o => o.Value).ToHashSet();
                for (var i = 0; i < choice.Options.Length; i++)
                {
                    if (values.Contains(ChoiceDef.ValueOf(i))) continue;
                    _service.Execute(new InsertOptionValueRequest
                    {
                        OptionSetName = choice.Name,
                        Value = ChoiceDef.ValueOf(i),
                        Label = Label(choice.Options[i]),
                        SolutionUniqueName = Solution,
                    });
                    Log.Ok($"{choice.Name}: added option '{choice.Options[i]}'");
                }
                Log.Skip($"{choice.Name} exists");
                continue;
            }

            var optionSet = new OptionSetMetadata
            {
                Name = choice.Name,
                DisplayName = Label(choice.Label),
                IsGlobal = true,
                OptionSetType = OptionSetType.Picklist,
            };
            for (var i = 0; i < choice.Options.Length; i++)
            {
                optionSet.Options.Add(new OptionMetadata(Label(choice.Options[i]), ChoiceDef.ValueOf(i)));
            }
            _service.Execute(new CreateOptionSetRequest { OptionSet = optionSet, SolutionUniqueName = Solution });
            Log.Ok($"{choice.Name} created ({choice.Options.Length} options)");
        }
    }

    private void EnsureTable(TableDef table)
    {
        if (TryGetEntity(table.LogicalName, EntityFilters.Entity) != null)
        {
            Log.Skip($"{table.SchemaName} exists");
            return;
        }

        var primary = table.PrimaryName;
        _service.Execute(new CreateEntityRequest
        {
            Entity = new EntityMetadata
            {
                SchemaName = table.SchemaName,
                DisplayName = Label(table.Label),
                DisplayCollectionName = Label(table.PluralLabel),
                Description = Label(table.Description ?? table.Label),
                OwnershipType = table.Ownership,
                IsActivity = false,
                IsAuditEnabled = new BooleanManagedProperty(table.Audited),
            },
            PrimaryAttribute = new StringAttributeMetadata
            {
                SchemaName = primary.SchemaName,
                DisplayName = Label(primary.Label),
                Description = Label(primary.Description ?? primary.Label),
                RequiredLevel = RequiredLevel(primary.Requirement),
                MaxLength = primary.MaxLength ?? 100,
                FormatName = StringFormatName.Text,
                AutoNumberFormat = primary.AutoNumberFormat,
            },
            HasNotes = table.HasNotes,
            HasActivities = false,
            SolutionUniqueName = Solution,
        });
        Log.Ok($"{table.SchemaName} created");
    }

    private void EnsureColumns(TableDef table, bool lookups)
    {
        var entity = TryGetEntity(table.LogicalName, EntityFilters.Attributes)
            ?? throw new InvalidOperationException($"Table {table.LogicalName} was not found after creation.");
        var existing = entity.Attributes.Select(a => a.LogicalName).ToHashSet();

        foreach (var column in table.Columns.Where(c => (c.Type == ColumnType.Lookup) == lookups))
        {
            if (existing.Contains(column.LogicalName))
            {
                if (column.Type == ColumnType.Choice) AddMissingOptions(table, column, entity);
                Log.Skip($"{table.LogicalName}.{column.LogicalName} exists");
                continue;
            }

            if (column.Type == ColumnType.Lookup)
            {
                CreateLookup(table, column);
            }
            else
            {
                _service.Execute(new CreateAttributeRequest
                {
                    EntityName = table.LogicalName,
                    Attribute = BuildAttribute(column),
                    SolutionUniqueName = Solution,
                });
            }
            Log.Ok($"{table.LogicalName}.{column.LogicalName} created");
        }
    }

    /// <summary>Options appended to a local choice in the JSON are added to the existing column.</summary>
    private void AddMissingOptions(TableDef table, ColumnDef column, EntityMetadata entity)
    {
        var attribute = entity.Attributes.OfType<PicklistAttributeMetadata>().FirstOrDefault(a => a.LogicalName == column.LogicalName);
        if (attribute?.OptionSet == null || attribute.OptionSet.IsGlobal == true) return;
        var values = attribute.OptionSet.Options.Select(o => o.Value).ToHashSet();
        for (var i = 0; i < column.Options.Length; i++)
        {
            if (values.Contains(ChoiceDef.ValueOf(i))) continue;
            _service.Execute(new InsertOptionValueRequest
            {
                EntityLogicalName = table.LogicalName,
                AttributeLogicalName = column.LogicalName,
                Value = ChoiceDef.ValueOf(i),
                Label = Label(column.Options[i]),
                SolutionUniqueName = Solution,
            });
            Log.Ok($"{table.LogicalName}.{column.LogicalName}: added option '{column.Options[i]}'");
        }
    }

    private void CreateLookup(TableDef table, ColumnDef column)
    {
        var relationship = $"hra_{Strip(column.Target)}_{Strip(table.LogicalName)}_{Strip(column.LogicalName)}";
        var cascade = column.Behaviour switch
        {
            LookupBehaviour.Parental => new CascadeConfiguration
            {
                Assign = CascadeType.Cascade, Delete = CascadeType.Cascade, Merge = CascadeType.NoCascade,
                Reparent = CascadeType.Cascade, Share = CascadeType.Cascade, Unshare = CascadeType.Cascade,
                RollupView = CascadeType.NoCascade,
            },
            _ => new CascadeConfiguration
            {
                Assign = CascadeType.NoCascade,
                Delete = column.Behaviour == LookupBehaviour.Restrict ? CascadeType.Restrict : CascadeType.RemoveLink,
                Merge = CascadeType.NoCascade, Reparent = CascadeType.NoCascade, Share = CascadeType.NoCascade,
                Unshare = CascadeType.NoCascade, RollupView = CascadeType.NoCascade,
            },
        };

        _service.Execute(new CreateOneToManyRequest
        {
            Lookup = new LookupAttributeMetadata
            {
                SchemaName = column.SchemaName,
                DisplayName = Label(column.Label),
                Description = Label(column.Description ?? column.Label),
                RequiredLevel = RequiredLevel(column.Requirement),
            },
            OneToManyRelationship = new OneToManyRelationshipMetadata
            {
                SchemaName = relationship,
                ReferencedEntity = column.Target,
                ReferencingEntity = table.LogicalName,
                CascadeConfiguration = cascade,
                AssociatedMenuConfiguration = new AssociatedMenuConfiguration
                {
                    Behavior = AssociatedMenuBehavior.UseLabel,
                    Group = AssociatedMenuGroup.Details,
                    Label = Label($"{table.PluralLabel} ({column.Label})"),
                    Order = 10000,
                },
            },
            SolutionUniqueName = Solution,
        });
    }

    private void EnsureKeys(TableDef table)
    {
        foreach (var key in table.Keys)
        {
            var logicalName = key.SchemaName.ToLowerInvariant();
            if (_service.TryExecute(new RetrieveEntityKeyRequest { EntityLogicalName = table.LogicalName, LogicalName = logicalName }, out _))
            {
                Log.Skip($"{table.LogicalName} key {logicalName} exists");
                continue;
            }
            _service.Execute(new CreateEntityKeyRequest
            {
                EntityName = table.LogicalName,
                EntityKey = new EntityKeyMetadata
                {
                    SchemaName = key.SchemaName,
                    DisplayName = Label(key.Label),
                    KeyAttributes = key.Columns,
                },
                SolutionUniqueName = Solution,
            });
            Log.Ok($"{table.LogicalName} key {logicalName} created (Dataverse builds the index in the background)");
        }
    }

    private static AttributeMetadata BuildAttribute(ColumnDef c)
    {
        AttributeMetadata attribute = c.Type switch
        {
            ColumnType.Text or ColumnType.Email or ColumnType.Phone or ColumnType.Url => new StringAttributeMetadata
            {
                MaxLength = c.MaxLength ?? 100,
                FormatName = c.Type switch
                {
                    ColumnType.Email => StringFormatName.Email,
                    ColumnType.Phone => StringFormatName.Phone,
                    ColumnType.Url => StringFormatName.Url,
                    _ => StringFormatName.Text,
                },
                AutoNumberFormat = c.AutoNumberFormat,
            },
            ColumnType.Memo => new MemoAttributeMetadata { MaxLength = c.MaxLength ?? 2000, Format = StringFormat.TextArea },
            ColumnType.WholeNumber => new IntegerAttributeMetadata
            {
                Format = IntegerFormat.None, MinValue = c.MinValue ?? 0, MaxValue = c.MaxValue ?? int.MaxValue,
            },
            ColumnType.Decimal => new DecimalAttributeMetadata
            {
                Precision = c.Precision, MinValue = c.MinValue ?? 0, MaxValue = c.MaxValue ?? 1000,
            },
            ColumnType.Currency => new MoneyAttributeMetadata
            {
                Precision = 2, PrecisionSource = 2, MinValue = 0, MaxValue = 1_000_000_000, ImeMode = ImeMode.Disabled,
            },
            ColumnType.DateOnly => new DateTimeAttributeMetadata
            {
                Format = DateTimeFormat.DateOnly, DateTimeBehavior = DateTimeBehavior.DateOnly,
            },
            ColumnType.DateTime => new DateTimeAttributeMetadata
            {
                Format = DateTimeFormat.DateAndTime, DateTimeBehavior = DateTimeBehavior.UserLocal,
            },
            ColumnType.YesNo => new BooleanAttributeMetadata
            {
                OptionSet = new BooleanOptionSetMetadata(new OptionMetadata(Label("Yes"), 1), new OptionMetadata(Label("No"), 0)),
                DefaultValue = false,
            },
            ColumnType.Choice => LocalChoice(c),
            ColumnType.GlobalChoice => new PicklistAttributeMetadata
            {
                OptionSet = new OptionSetMetadata { IsGlobal = true, Name = c.GlobalChoice },
                DefaultFormValue = c.DefaultOption.HasValue ? ChoiceDef.ValueOf(c.DefaultOption.Value) : null,
            },
            ColumnType.File => new FileAttributeMetadata { MaxSizeInKB = c.MaxSizeKb },
            _ => throw new NotSupportedException($"Column type {c.Type} for {c.SchemaName}"),
        };

        attribute.SchemaName = c.SchemaName;
        attribute.DisplayName = Label(c.Label);
        attribute.Description = Label(c.Description ?? c.Label);
        attribute.RequiredLevel = RequiredLevel(c.Requirement);
        if (c.Secured) attribute.IsSecured = true;
        if (c.Audited || c.Secured) attribute.IsAuditEnabled = new BooleanManagedProperty(true);
        return attribute;
    }

    private static PicklistAttributeMetadata LocalChoice(ColumnDef c)
    {
        var optionSet = new OptionSetMetadata { IsGlobal = false, OptionSetType = OptionSetType.Picklist };
        for (var i = 0; i < c.Options.Length; i++)
        {
            optionSet.Options.Add(new OptionMetadata(Label(c.Options[i]), ChoiceDef.ValueOf(i)));
        }
        return new PicklistAttributeMetadata
        {
            OptionSet = optionSet,
            DefaultFormValue = c.DefaultOption.HasValue ? ChoiceDef.ValueOf(c.DefaultOption.Value) : null,
        };
    }

    private static AttributeRequiredLevelManagedProperty RequiredLevel(Requirement requirement) => new(requirement switch
    {
        Requirement.Required => AttributeRequiredLevel.ApplicationRequired,
        Requirement.Recommended => AttributeRequiredLevel.Recommended,
        _ => AttributeRequiredLevel.None,
    });

    private static string Strip(string logicalName) =>
        logicalName.StartsWith(Conventions.Prefix + "_") ? logicalName[(Conventions.Prefix.Length + 1)..] : logicalName;

    private EntityMetadata TryGetEntity(string logicalName, EntityFilters filters) =>
        _service.TryExecute(new RetrieveEntityRequest { LogicalName = logicalName, EntityFilters = filters }, out var response)
            ? ((RetrieveEntityResponse)response).EntityMetadata
            : null;
}
