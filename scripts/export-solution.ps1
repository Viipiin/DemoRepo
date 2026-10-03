# Exports the HRAutomation solution from the environment you're connected to with pac,
# and unpacks it into src/solution so the changes can be committed to Git.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$zip = Join-Path $root "out/HRAutomation.zip"
$folder = Join-Path $root "src/solution"

New-Item -ItemType Directory -Force -Path (Join-Path $root "out") | Out-Null

pac solution export --name HRAutomation --path $zip --overwrite
if ($LASTEXITCODE -ne 0) { throw "pac solution export failed." }

if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }
pac solution unpack --zipfile $zip --folder $folder --packagetype Unmanaged
if ($LASTEXITCODE -ne 0) { throw "pac solution unpack failed." }

Write-Host "`nSolution unpacked to src/solution. Review with 'git status', then commit." -ForegroundColor Green
