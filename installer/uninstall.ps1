# ARYE Region Area - uninstaller for Revit 2026 (per user, no administrator rights).
# Run it through Uninstall.bat (double-click).

$ErrorActionPreference = 'Stop'
$RevitVersion = '2026'
$Name = 'ARYE.RegionArea'
$addins = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"

Write-Host ''
Write-Host "ARYE Region Area - uninstall from Revit $RevitVersion" -ForegroundColor Cyan
Write-Host ''

if (Get-Process -Name 'Revit' -ErrorAction SilentlyContinue) {
    Write-Host '[X] Revit is open. Close every Revit window and run Uninstall.bat again.' -ForegroundColor Red
    exit 1
}

$removed = 0
$manifest = Join-Path $addins "$Name.addin"
$folder = Join-Path $addins $Name
if (Test-Path $manifest) { Remove-Item -Force $manifest; $removed++ }
if (Test-Path $folder) { Remove-Item -Recurse -Force $folder; $removed++ }

if ($removed -eq 0) {
    Write-Host '[!] Nothing to remove: ARYE Region Area was not installed for this user.' -ForegroundColor Yellow
} else {
    Write-Host "[OK] Removed from $addins" -ForegroundColor Green
}
exit 0
