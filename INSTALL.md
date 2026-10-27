# Installing ARYE Office Tools (Revit 2026)

ARYE Office Tools adds an **ARYE** tab to Revit 2026 with the tools approved for the office:

| Panel | Buttons |
|---|---|
| **Site** | NYC Lot Boundary (needs an internet connection) |
| **Levels** | Generate Levels, Bubble Heads |
| **Zoning** | Zoning Floor Area, Region Area → Param, Light & Vent |
| **Help** | About |

> **Zoning Floor Area:** the ZR calculation is not yet validated against an approved filing.
> Check every number before using it in a filing.

**Choose one method only.** If you install it both ways, every button appears twice.

| | Method A — pyRevit (recommended) | Method B — Installer |
|---|---|---|
| Needs | pyRevit installed | Nothing else |
| Updates | One command or one click in pyRevit | Download and run the new zip |

---

## Method A — with pyRevit

You need [pyRevit](https://github.com/pyrevitlabs/pyRevit/releases) installed for Revit 2026. If you already have a
**pyRevit** tab in Revit, you have it.

1. **Close Revit.**
2. Open the Windows **Start** menu, type `cmd`, and open **Command Prompt**.
3. Copy this line, paste it into Command Prompt, and press **Enter**:

   ```bat
   pyrevit extend ui ARYEOfficeTools https://github.com/sercamilo-hash/ARYE.OfficeTools.git
   ```

   Wait until the prompt returns. It downloads the tools from GitHub; no account is needed.
4. **Open Revit 2026.** The **ARYE** tab appears with the Site, Levels, Zoning and Help panels.
5. Check the install: **ARYE › Help › About** shows the version.

### Update (Method A)

When a new version is announced:

1. **Close Revit.**
2. In **Command Prompt**, run:

   ```bat
   pyrevit extensions update ARYEOfficeTools
   ```

3. Open Revit.

You can also update from Revit: **pyRevit** tab › **Extensions**, select **ARYEOfficeTools**, click **Update**,
then close and reopen Revit.

### Remove (Method A)

Close Revit and run:

```bat
pyrevit extensions delete ARYEOfficeTools
```

---

## Method B — Installer (without pyRevit)

1. Download the zip from the latest release:
   **https://github.com/sercamilo-hash/ARYE.OfficeTools/releases/latest**
   (the file is called `ARYE.OfficeTools-<version>-Revit2026.zip`).
2. **Close Revit.**
3. Right-click the zip › **Extract All…** › **Extract**. Open the folder that was created.
   Do not run anything from inside the zip.
4. Double-click **Install.bat** and wait for `[OK] Installed`, then press any key.
   If Windows shows **"Windows protected your PC"**, click **More info** › **Run anyway**.
5. **Open Revit 2026.** If Revit asks whether to load the add-in, choose **Always Load**.
   The **ARYE** tab appears.

No administrator rights are needed: everything is installed for your Windows user only.

### Update (Method B)

Download the new zip and repeat steps 2–5. The installer replaces the old version.

### Remove (Method B)

Close Revit, then double-click **Uninstall.bat** in the extracted folder.

---

## Troubleshooting

| Problem | What to do |
|---|---|
| `'pyrevit' is not recognized as an internal or external command` | pyRevit's command line is not installed. Use Method B, or ask for pyRevit to be reinstalled. |
| No **ARYE** tab after installing | Make sure you opened **Revit 2026**. Restart Revit once. With pyRevit, also try **pyRevit › Reload**. |
| The **ARYE** tab shows panels but no buttons (pyRevit) | Update: `pyrevit extensions update ARYEOfficeTools`, then **pyRevit › Reload**. |
| The update fails with "file in use" | Revit is still open. Close every Revit window and run the update again. |
| `[X] Revit is open` from Install.bat | Close every Revit window and run Install.bat again. |
| A button shows an error | Send Sergio the log of that day from `%LOCALAPPDATA%\ARYE\OfficeTools\logs\` (paste that path into the File Explorer address bar), and the version shown in **ARYE › Help › About**. |
| Every button appears twice | It was installed with both methods. Remove one (see *Remove* above). |

## Links

- Latest release (zip and release notes): https://github.com/sercamilo-hash/ARYE.OfficeTools/releases/latest
- Repository: https://github.com/sercamilo-hash/ARYE.OfficeTools
