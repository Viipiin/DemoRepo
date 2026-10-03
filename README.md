# DemoRepo: HR Automation on Power Platform

This is for testing purpose.

A model-driven HR app on Dataverse for an Indian company of about 100 employees, built with Claude Code.

- Design: [docs/architecture.md](docs/architecture.md) and [docs/hr-automation/data-model.md](docs/hr-automation/data-model.md)
- Prompt kit: [docs/hr-automation/PROMPT.md](docs/hr-automation/PROMPT.md)
- How to run: [docs/phase-1-runbook.md](docs/phase-1-runbook.md) and [docs/commands.md](docs/commands.md)

| Folder | Contents |
|---|---|
| `src/tools/HRA.Provisioner` | .NET 8 tool that creates tables, forms, views, roles and sample data in Dataverse |
| `src/plugins/HRAutomation.Plugins` | C# Dataverse plugins (.NET Framework 4.6.2) and their unit tests |
| `src/solution` | Unpacked HRAutomation solution (after the first export) |
| `scripts` | PowerShell scripts to build, provision and export |
