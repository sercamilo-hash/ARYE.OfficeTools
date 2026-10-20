# Builds ARYE Region Area in Release and packs the zip the office installs from:
#   dist\ARYE.RegionArea-<version>-Revit2026.zip
# Layout inside the zip:
#   Install.bat, Uninstall.bat, README.txt
#   installer\install.ps1, installer\uninstall.ps1
#   payload\ARYE.RegionArea.addin, payload\ARYE.RegionArea\ (dll + Resources)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repo 'src\ARYE.RegionArea\ARYE.RegionArea.csproj'

dotnet build $proj -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

$bin = Join-Path $repo 'src\ARYE.RegionArea\bin\Release'
$dll = Join-Path $bin 'ARYE.RegionArea.dll'
$version = (Get-Item $dll).VersionInfo.ProductVersion -replace '\+.*$', ''

$name  = "ARYE.RegionArea-$version-Revit2026"
$dist  = Join-Path $repo 'dist'
$stage = Join-Path $dist $name
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force -Path "$stage\installer", "$stage\payload\ARYE.RegionArea" | Out-Null

Copy-Item $dll, (Join-Path $bin 'ARYE.RegionArea.deps.json') "$stage\payload\ARYE.RegionArea"
Copy-Item -Recurse (Join-Path $bin 'Resources') "$stage\payload\ARYE.RegionArea\Resources"
Copy-Item (Join-Path $repo 'installer\ARYE.RegionArea.addin') "$stage\payload"
Copy-Item (Join-Path $repo 'installer\install.ps1'), (Join-Path $repo 'installer\uninstall.ps1') "$stage\installer"

# cmd.exe expects CRLF in .bat files; normalise whatever line endings git checked out.
foreach ($bat in 'Install.bat', 'Uninstall.bat') {
    $text = (Get-Content -Raw (Join-Path $repo "installer\$bat")) -replace "`r?`n", "`r`n"
    [System.IO.File]::WriteAllText((Join-Path $stage $bat), $text, (New-Object System.Text.ASCIIEncoding))
}
$readme = (Get-Content -Raw (Join-Path $repo 'installer\README.txt')) -replace "`r?`n", "`r`n"
[System.IO.File]::WriteAllText((Join-Path $stage 'README.txt'), $readme.Replace('{VERSION}', $version), (New-Object System.Text.UTF8Encoding $false))

$zip = "$stage.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
# Compress-Archive in Windows PowerShell 5.1 writes entry names with backslashes, which
# some unzip tools turn into odd file names. Write the entries with '/' explicitly.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, 'Create')
try {
    foreach ($file in Get-ChildItem -Recurse -File $stage) {
        $entry = $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry) | Out-Null
    }
} finally {
    $archive.Dispose()
}
Write-Host "[OK] $zip" -ForegroundColor Green
