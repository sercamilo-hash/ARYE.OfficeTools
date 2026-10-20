# ARYE Region Area

One Revit 2026 button — **ARYE › Energy › Region Area → Param** — that writes the computed area of
Filled Regions into a writable parameter (for example `Region Area` on the energy elevations), with a
before/after preview that must be confirmed before anything is written. Building and Side can be
filled in the same run.

Extracted from [BauTools](https://github.com/sercamilo-hash/BauTools-v2) (`Region Area → Param`) so the
office can install this single tool on its own. BauTools keeps its own copy of the button; the two
add-ins can be installed side by side.

## Install (office)

1. Download `ARYE.RegionArea-<version>-Revit2026.zip` from the latest
   [release](https://github.com/sercamilo-hash/ARYE.RegionArea/releases), or take it from the office
   share.
2. Close Revit.
3. Extract the whole zip (right-click › *Extract All*). Do not run anything from inside the zip.
4. Double-click **Install.bat** and wait for `[OK] Installed`. If Windows shows *"Windows protected your
   PC"*, click *More info › Run anyway*.
5. Open Revit 2026. The button is on the **ARYE** tab, **Energy** panel. If Revit asks whether to load
   the add-in, choose **Always Load**.

No administrator rights are needed: the add-in is installed for the current Windows user only, in
`%APPDATA%\Autodesk\Revit\Addins\2026\`. To update, run `Install.bat` from the new zip. To remove it,
run `Uninstall.bat`. Logs: `%LOCALAPPDATA%\ARYE\RegionArea\logs\`.

## Build and release (maintainer)

Requirements: .NET 8 SDK. The Revit API comes from the reference-only
[Nice3point](https://github.com/Nice3point/RevitApi) NuGet packages, so Revit does not need to be
installed to build.

```powershell
.\build\package.ps1      # Release build + dist\ARYE.RegionArea-<version>-Revit2026.zip
```

To publish a new version: raise `<Version>` in `src/ARYE.RegionArea/ARYE.RegionArea.csproj`, run
`package.ps1`, commit, and attach the zip to a GitHub release tagged `v<version>`.

## Layout

| Path | Contents |
|---|---|
| `src/ARYE.RegionArea/` | The add-in: `App.cs` (ribbon), `Commands/`, `Views/` (WPF in C#, no XAML), `Services/`, `Models/` |
| `installer/` | `.addin` manifest, `Install.bat` / `Uninstall.bat` and the PowerShell scripts they run, zip `README.txt` |
| `build/package.ps1` | Builds and packs the release zip |

The `.addin` manifest points to the DLL with a path **relative** to itself. Never point it at a network
share: deploy binaries locally on each machine. Never change its `ClientId`.

The "last run" stamp uses the same ExtensibleStorage schema as BauTools
(`7C1E3A20-5B44-4C6E-9F2A-8D5E1B0C7A31`), so a model shows the same last run whichever add-in wrote it.
Schemas cannot be altered once published: do not change its fields.
