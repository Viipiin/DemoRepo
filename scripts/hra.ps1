# Runs the HR Automation provisioner. Examples:
#   ./scripts/hra.ps1 check
#   ./scripts/hra.ps1 provision
#   ./scripts/hra.ps1 register-plugins
#   ./scripts/hra.ps1 seed
# Add --url https://other.crm.dynamics.com to target another environment.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

dotnet run --project "$root/src/tools/HRA.Provisioner" -- @args
if ($LASTEXITCODE -ne 0) { throw "Provisioner command failed: $args" }
