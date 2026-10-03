# HR Automation Model (JSON)

These files are the **single source of truth** for what the provisioner creates in Dataverse. To change the data model, edit the JSON, validate it, and run `provision`. Don't add columns by hand in the maker portal, because the next `provision` run won't know about them.

```
model/
  choices.json                  global choices (option sets) shared by many columns
  tables/NN-<table>.json        one file per table: columns, lookups, keys, form, views
  security.json                 owner teams, security roles, column security profiles
  environment-variables.json    configurable settings
```

## The workflow

```powershell
# 1. Edit the JSON in VS Code
# 2. Check it. This works offline and doesn't touch Dataverse:
./scripts/hra.ps1 validate
# 3. Apply it. New items are created, existing ones are skipped:
./scripts/hra.ps1 provision
# 4. Commit both the JSON and the exported solution
./scripts/export-solution.ps1
```

`validate` lists every problem with its file and column, for example:
```
- 03-hra_location.json: hra_Pincode: type "Txt" is not valid. Use one of: Text, Email, ...
- hra_location form tab general: column hra_zone doesn't exist in this table
```

## Add a column

Add an object to the table's `columns` array. If you want the column on the form and in views, also add its **lowercase** logical name to `form` and `views`.

```json
{ "name": "hra_Pincode", "label": "PIN Code", "type": "Text", "maxLength": 6, "required": "Recommended" }
```

| Property | Used for | Notes |
|---|---|---|
| `name` | all | Schema name, which must start with `hra_`. The logical name is the lowercase form (`hra_pincode`) |
| `label` | all | Display name |
| `type` | all | `Text`, `Email`, `Phone`, `Url`, `Memo`, `WholeNumber`, `Decimal`, `Currency`, `DateOnly`, `DateTime`, `YesNo`, `Choice`, `GlobalChoice`, `Lookup`, `File` |
| `required` | all | `Required`, `Recommended` or `Optional` (the default) |
| `description` | all | Tooltip text |
| `maxLength` | Text, Memo | Defaults: Text 100, Email 100, Phone 20, Url 200, Memo 2000 |
| `autoNumber` | Text | For example `"EMP-{SEQNUM:4}"`. The column is shown read-only on the form |
| `min`, `max` | WholeNumber, Decimal | |
| `precision` | Decimal | Decimal places, default 2 |
| `options` | Choice | A local list, for example `["Low", "Normal", "High"]` |
| `choice` | GlobalChoice | Name of a choice in `choices.json`, for example `"hra_gender"` |
| `default` | Choice, GlobalChoice | The **label** of the default option, for example `"Normal"` |
| `target` | Lookup | Logical name of a model table, or `systemuser`, `team`, `contact`, `account` or `businessunit` |
| `onDelete` | Lookup | `RemoveLink` (the default), `Restrict` or `Cascade` (parental) |
| `maxSizeKb` | File | Default 10240 (10 MB) |
| `secured` | all | Column security. Also add the column to a profile in `security.json` |
| `audited` | all | Audit changes (secured columns are always audited) |
| `readOnly` | all | Read-only on the generated form (for values set by plugins) |
| `_note` | all | A free-text note for developers. It's ignored by the tool |

## Add a choice option

Append the new label to the **end** of the `options` list, either in `choices.json` or in a column's local `options`. `provision` adds it to the existing choice.

> **Never reorder, rename or delete existing options.** Option values come from their position: the first is 817990000, the second 817990001, and so on. Moving an option changes the meaning of data that's already saved, and of plugin code that uses the values. This matters especially for `hra_employmenthistory.hra_changetype`.

## Add a table

Copy an existing file in `tables/`, give it the next number (for example `09-hra_leavetype.json`), and change `name`, `label`, `pluralLabel`, `ownership` (`User` or `Organization`), `primaryName` and `columns`. Each table needs exactly one view with `"default": true`, which becomes the table's Active view.

Then add the table to the roles in `security.json`, for example `"hra_leavetype": "Org:RT"`.

**Role access format:** `"<depth>:<letters>"`.
- **Depth:** `Org`, `BusinessUnit` or `User`.
- **Letters:** **C**reate, **R**ead, **W**rite, **D**elete, **A**ppend, append **T**o, a**S**sign + share.

## What `provision` doesn't change

To avoid damaging data, `provision` only **adds**. These changes need to be done by hand in the maker portal (and then copied into the JSON so the two match), or with Claude's help:
- changing an existing column's type, length, label or required level
- renaming or deleting columns, tables or options
- changing a lookup's delete behaviour after it's created

Forms and views are the exception. They're **rewritten** from the JSON on every run, so make layout changes in the JSON, not in the designer.
