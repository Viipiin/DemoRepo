# Builds the plugin assembly (Release) and runs its unit tests.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

dotnet build "$root/src/plugins/HRAutomation.Plugins" -c Release
if ($LASTEXITCODE -ne 0) { throw "Plugin build failed." }

dotnet test "$root/src/plugins/HRAutomation.Plugins.Tests"
if ($LASTEXITCODE -ne 0) { throw "Plugin tests failed." }

Write-Host "`nPlugins built: src/plugins/HRAutomation.Plugins/bin/Release/net462/HRAutomation.Plugins.dll" -ForegroundColor Green
