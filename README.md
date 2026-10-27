# ARYE Office Tools

Three Revit 2026 buttons on the **ARYE** tab:

- **NYC Lot Boundary** (**Site** panel) searches a NYC tax lot by address or BBL, shows every dimension
  source side by side (tax-map polygon, legal tax-map dimensions, assessment record) and draws the lot in
  context: adjacent lots, block, sidewalk, 3D masses and street names. Needs an internet connection. The
  subject lot keeps the `BauTools - Property Line` line style and interop record that TopoGenerator's
  *Align to Lot* looks for: do not rename them.
- **Region Area → Param** (**Zoning** panel) writes the computed area of Filled Regions into a writable parameter (for
  example `Region Area` on the energy elevations), with a before/after preview that must be confirmed
  before anything is written. Building and Side can be filled in the same run.
- **Light & Vent** (**Zoning** panel) adds up the light area and the vent area of each room's exterior windows and writes
  the two totals to the room, after a plan preview with the windows highlighted and a before/after table.

All three come from [BauTools](https://github.com/sercamilo-hash/BauTools-v2), packed on their own so the
office can install just these tools. BauTools keeps its own copy of them; the two add-ins
can be installed side by side. A fix made in one copy has to be carried to the other by hand.

## Install with pyRevit (recommended for the office)

This repository is also a pyRevit extension: pyRevit clones it and the three buttons appear on the
**ARYE** tab (Site and Zoning panels). Requires pyRevit installed for Revit 2026.

**Option A — from pyRevit's Extensions window**

1. Once per computer, make the extension appear in the list. Run `pyRevit-AddSource.bat` (it is in the
   release zip and in `installer/`), or run these two lines in a Command Prompt:

   ```bat
   powershell -Command "iwr -UseBasicParsing https://raw.githubusercontent.com/sercamilo-hash/ARYE.OfficeTools/main/extensions.json -OutFile $env:APPDATA\pyRevit\ARYE.extensions.json"
   pyrevit extensions sources add "%APPDATA%\pyRevit\ARYE.extensions.json"
   ```

   pyRevit lists only extensions whose definition file is on disk, which is why the definition is
   downloaded first.
2. In Revit: **pyRevit** tab › **Extensions**, find **ARYEOfficeTools**, click **Install Extension**,
   and let pyRevit reload.

**Option B — one command**

```bat
pyrevit extend ui ARYEOfficeTools https://github.com/sercamilo-hash/ARYE.OfficeTools.git
```

Then reload pyRevit (pyRevit tab › Reload) or restart Revit.

**Update:** close Revit first (Revit locks the DLL while it is open), then pyRevit tab › Extensions ›
ARYEOfficeTools › Update, or `pyrevit extensions update ARYEOfficeTools`.

Do not install it both with pyRevit and with `Install.bat`: the buttons would appear twice.

## Install without pyRevit (Install.bat)

1. Download `ARYE.OfficeTools-<version>-Revit2026.zip` from the latest
   [release](https://github.com/sercamilo-hash/ARYE.OfficeTools/releases), or take it from the office
   share.
2. Close Revit.
3. Extract the whole zip (right-click › *Extract All*). Do not run anything from inside the zip.
4. Double-click **Install.bat** and wait for `[OK] Installed`. If Windows shows *"Windows protected your
   PC"*, click *More info › Run anyway*.
5. Open Revit 2026 and go to the **ARYE** tab. If Revit asks whether to load the
   add-in, choose **Always Load**.

No administrator rights are needed: the add-in is installed for the current Windows user only, in
`%APPDATA%\Autodesk\Revit\Addins\2026\`. To update, run `Install.bat` from the new zip. To remove it,
run `Uninstall.bat`. Logs: `%LOCALAPPDATA%\ARYE\OfficeTools\logs\`.

## Build and release (maintainer)

Requirements: .NET 8 SDK. The Revit API comes from the reference-only
[Nice3point](https://github.com/Nice3point/RevitApi) NuGet packages, so Revit does not need to be
installed to build.

```powershell
.\build\package.ps1      # Release build + dist\ARYE.OfficeTools-<version>-Revit2026.zip
```

Version 1.1.0 renamed the add-in from `ARYE.RegionArea` to `ARYE.OfficeTools`; `Install.bat` removes a
1.0.0 install it finds.

To publish a new version: raise `<Version>` in `src/ARYE.OfficeTools/ARYE.OfficeTools.csproj`, run
`package.ps1`, commit (including `bin/ARYE.OfficeTools.dll`, which pyRevit users get with every update),
push, and attach the zip to a GitHub release tagged `v<version>`.

## Layout

| Path | Contents |
|---|---|
| `src/ARYE.OfficeTools/` | The add-in: `App.cs` (ribbon), `Commands/`, `Views/` (WPF in C#, no XAML), `Services/`, `Models/` |
| `src/ARYE.OfficeTools/Core/` | Revit-free logic: Light & Vent (which room a window counts for, totals), NYC lot geometry and sources (`Nyc/`), the TopoGenerator interop payloads (`Interop/`) |
| `tests/ARYE.OfficeTools.Tests/` | xUnit tests of `Core/`, compiled without Revit: `dotnet test tests/ARYE.OfficeTools.Tests` |
| `installer/` | `.addin` manifest, `Install.bat` / `Uninstall.bat` and the PowerShell scripts they run, zip `README.txt` |
| `build/package.ps1` | Builds, copies the DLL to `bin/` and packs the release zip |
| `ARYE.tab/`, `bin/`, `extension.json` | The pyRevit extension: three link buttons that run the commands of `bin/ARYE.OfficeTools.dll` |
| `extensions.json` | Extension definition that `pyRevit-AddSource.bat` registers, so pyRevit's Extensions window lists it |

The `.addin` manifest points to the DLL with a path **relative** to itself. Never point it at a network
share: deploy binaries locally on each machine. Never change its `ClientId`.

The "last run" stamp uses the same ExtensibleStorage schema as BauTools
(`7C1E3A20-5B44-4C6E-9F2A-8D5E1B0C7A31`), so a model shows the same last run whichever add-in wrote it.
Schemas cannot be altered once published: do not change its fields.
