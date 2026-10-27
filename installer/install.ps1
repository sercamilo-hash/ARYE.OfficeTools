# ARYE Office Tools - installer for Revit 2026.
# Per-user install, no administrator rights: copies the add-in to
#   %APPDATA%\Autodesk\Revit\Addins\2026\
# Run it through Install.bat (double-click).

$ErrorActionPreference = 'Stop'
$RevitVersion = '2026'
$Name = 'ARYE.OfficeTools'

$root    = Split-Path -Parent $PSScriptRoot
$payload = Join-Path $root 'payload'
$addins  = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"

Write-Host ''
Write-Host "ARYE Office Tools - install for Revit $RevitVersion" -ForegroundColor Cyan
Write-Host ''

if (-not (Test-Path (Join-Path $payload "$Name.addin"))) {
    Write-Host "[X] The 'payload' folder is missing next to Install.bat." -ForegroundColor Red
    Write-Host '    Extract the WHOLE zip to a folder first, then run Install.bat from there.'
    exit 1
}

$revitExe = Join-Path $env:ProgramFiles "Autodesk\Revit $RevitVersion\Revit.exe"
if (-not (Test-Path $revitExe)) {
    Write-Host "[!] Revit $RevitVersion was not found at $revitExe." -ForegroundColor Yellow
    Write-Host '    Installing anyway; the button appears once Revit 2026 is installed.'
}

if (Get-Process -Name 'Revit' -ErrorAction SilentlyContinue) {
    Write-Host '[X] Revit is open. Close every Revit window and run Install.bat again.' -ForegroundColor Red
    exit 1
}

New-Item -ItemType Directory -Force -Path $addins | Out-Null

# 1.0.0 shipped as "ARYE.RegionArea" with the same ClientId. Two manifests with one ClientId
# make Revit refuse the second add-in, so the old install is removed and reported.
$old = 'ARYE.RegionArea'
if ((Test-Path (Join-Path $addins "$old.addin")) -or (Test-Path (Join-Path $addins $old))) {
    Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $addins "$old.addin")
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue (Join-Path $addins $old)
    Write-Host '[OK] Removed the previous version (ARYE Region Area 1.0.0).' -ForegroundColor Green
}

$target = Join-Path $addins $Name
if (Test-Path $target) { Remove-Item -Recurse -Force $target }

Copy-Item -Recurse -Force (Join-Path $payload $Name) $target
Copy-Item -Force (Join-Path $payload "$Name.addin") (Join-Path $addins "$Name.addin")

# Files extracted from a downloaded zip are marked as coming from the internet;
# Revit refuses to load such a DLL. Remove the mark from what was just copied.
Get-ChildItem -Recurse $target | Unblock-File
Unblock-File (Join-Path $addins "$Name.addin")

$dll = Join-Path $target "$Name.dll"
if (-not (Test-Path $dll)) {
    Write-Host "[X] Copy failed: $dll is missing." -ForegroundColor Red
    exit 1
}

$version = (Get-Item $dll).VersionInfo.ProductVersion -replace '\+.*$', ''
Write-Host "[OK] Installed version $version in $addins" -ForegroundColor Green
Write-Host ''
Write-Host 'Open Revit 2026 and go to the ARYE tab: Site, Levels, Zoning and Help panels.'
Write-Host 'The first time, Revit may ask whether to load the add-in: choose "Always Load".'
exit 0
