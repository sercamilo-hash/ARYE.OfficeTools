# ARYE Office Tools

The BauTools buttons **approved for the office**, as a Revit 2026 add-in on the **ARYE** tab.

| Panel | Button | What it does |
|---|---|---|
| Site | **NYC Lot Boundary** | Finds a NYC tax lot by address or BBL and draws it in context: adjacent lots, block, sidewalk, 3D masses, street names. Needs an internet connection. Keeps the `BauTools - Property Line` style and interop record TopoGenerator's *Align to Lot* looks for. |
| Levels | **Generate Levels** | Creates a run of evenly-spaced levels; never overwrites an existing one. |
| Levels | **Bubble Heads** | Shows or hides grid and level bubbles in the active view. |
| Zoning | **Zoning Floor Area** | Buildings, typical floor ranges, Sync Ranges and the NYC deductions matrix. **The ZR calculation is not yet validated against an approved filing.** |
| Zoning | **Region Area → Param** | Writes the area of Filled Regions into a parameter (energy elevations). |
| Zoning | **Light & Vent** | Adds up the light and vent area of each room's exterior windows and writes the totals to the room. |
| Help | **About** | Version, the BauTools commit the buttons come from, log folder. |

## How this repo relates to BauTools

[BauTools](https://github.com/sercamilo-hash/BauTools-v2) is the development repo and changes all the time.
This repo is **only a copy of the buttons that work and are approved** to share with the office.
**Never edit the copied code here**: change it in BauTools, approve it, merge it into BauTools `main`, then
promote it (see *Promote approved buttons* below). `PROMOTED.md` says which BauTools commit each release
was copied from; the About button shows it too.

## Install with pyRevit (recommended for the office)

This repository is also a pyRevit extension: pyRevit clones it and the buttons appear on the **ARYE**
tab. Requires pyRevit installed for Revit 2026.

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

## Promote approved buttons (maintainer)

Requirements: .NET 8 SDK and Python 3. The Revit API comes from the reference-only
[Nice3point](https://github.com/Nice3point/RevitApi) NuGet packages, so Revit does not need to be
installed to build.

1. Merge the approved change into BauTools `main` and push it.
2. Copy it here:

   ```powershell
   python build\promote.py --bautools "C:\ARYE ai\BauTools" --ref origin/main
   ```

   The files listed in `build/promote.json` are read from git at that commit (never from a working
   tree, so unfinished local edits are not copied), renamed into this assembly, built and tested.
   `PROMOTED.md` and `PromotedFrom.cs` record the commit. Promoted files that are no longer listed are
   deleted.
3. **To add or remove a button**: edit `buttons` in `build/promote.json` (its command file is the seed),
   run the same command with `--discover` to recompute the file list, and add or remove the button in
   `App.cs` and under `ARYE.tab/`.
4. Raise `<Version>` in `src/ARYE.OfficeTools/ARYE.OfficeTools.csproj`, run `.\build\package.ps1`
   (Release build, copies the DLL to `bin/` for pyRevit, writes the zip), commit including
   `bin/ARYE.OfficeTools.dll`, push, and attach the zip to a GitHub release tagged `v<version>`.

Only `App.cs`, `Commands/AboutCommand.cs` and the project files belong to this repo; everything else
under `src/` and `tests/` is overwritten by the next promotion.

Version 1.1.0 renamed the add-in from `ARYE.RegionArea` to `ARYE.OfficeTools`; `Install.bat` removes a
1.0.0 install it finds.

## Layout

| Path | Contents |
|---|---|
| `src/ARYE.OfficeTools/` | The add-in. `App.cs` (ribbon) and `Commands/AboutCommand.cs` are this repo's own; the rest is promoted from BauTools |
| `src/ARYE.OfficeTools/Core/` | Revit-free logic promoted from `BauTools.Core` (ZFA model and matrix, levels, NYC lot geometry, Light & Vent, interop payloads) |
| `tests/ARYE.OfficeTools.Tests/` | xUnit tests of `Core/`, compiled without Revit: `dotnet test tests/ARYE.OfficeTools.Tests` |
| `installer/` | `.addin` manifest, `Install.bat` / `Uninstall.bat` and the PowerShell scripts they run, zip `README.txt` |
| `build/promote.py`, `build/promote.json` | Copies the approved buttons from a BauTools commit; the manifest lists buttons, files and tests |
| `PROMOTED.md` | Which BauTools commit the current code was copied from |
| `build/package.ps1` | Builds, copies the DLL to `bin/` and packs the release zip |
| `ARYE.tab/`, `bin/`, `extension.json` | The pyRevit extension: link buttons that run the commands of `bin/ARYE.OfficeTools.dll` |
| `extensions.json` | Extension definition that `pyRevit-AddSource.bat` registers, so pyRevit's Extensions window lists it |

The `.addin` manifest points to the DLL with a path **relative** to itself. Never point it at a network
share: deploy binaries locally on each machine. Never change its `ClientId`.

The "last run" stamp uses the same ExtensibleStorage schema as BauTools
(`7C1E3A20-5B44-4C6E-9F2A-8D5E1B0C7A31`), so a model shows the same last run whichever add-in wrote it.
Schemas cannot be altered once published: do not change its fields.
