@echo off
rem Makes "ARYEOfficeTools" appear in pyRevit's Extensions window (pyRevit tab > Extensions).
rem pyRevit lists only extensions defined in a file on disk, so this downloads the definition
rem to %APPDATA%\pyRevit\ARYE.extensions.json and registers it. Run it once per computer.
set "DEST=%APPDATA%\pyRevit\ARYE.extensions.json"
powershell -NoProfile -ExecutionPolicy Bypass -Command "New-Item -ItemType Directory -Force -Path (Split-Path '%DEST%') | Out-Null; Invoke-WebRequest -UseBasicParsing 'https://raw.githubusercontent.com/sercamilo-hash/ARYE.OfficeTools/main/extensions.json' -OutFile '%DEST%'"
if errorlevel 1 (
  echo [X] Could not download the extension definition. Check the internet connection.
  goto end
)
where pyrevit >nul 2>nul
if errorlevel 1 (
  echo [X] pyRevit is not installed, or its command line is not on PATH.
  goto end
)
pyrevit extensions sources add "%DEST%"
echo.
echo [OK] Open Revit, go to pyRevit tab ^> Extensions, find ARYEOfficeTools and click Install Extension.
:end
echo.
pause
