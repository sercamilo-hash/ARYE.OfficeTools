ARYE Office Tools {VERSION} - for Revit 2026
============================================

Buttons on the ARYE tab:

- Site: NYC Lot Boundary - finds a NYC tax lot by address or BBL and draws
  it in context. Needs an internet connection.
- Levels: Generate Levels, Bubble Heads.
- Zoning: Zoning Floor Area (the ZR calculation is not yet validated for
  filing), Region Area -> Param, Light & Vent.
- Help: About (version and log folder).

INSTALL
1. Close Revit.
2. Extract this whole zip to a folder (right-click > Extract All).
   Do not run Install.bat from inside the zip.
3. Double-click Install.bat. Wait for "[OK] Installed".
   If Windows shows "Windows protected your PC", click
   "More info" > "Run anyway".
4. Open Revit 2026 and go to the ARYE tab.
   If Revit asks whether to load the add-in, choose "Always Load".

UPDATE
Close Revit and run Install.bat from the new version. It replaces the old
one, including ARYE Region Area 1.0.0.

UNINSTALL
Close Revit and double-click Uninstall.bat.

No administrator rights are needed: everything is installed for your
Windows user only, in %APPDATA%\Autodesk\Revit\Addins\2026\.

Problems? Send the log in %LOCALAPPDATA%\ARYE\OfficeTools\logs\

WITH pyRevit INSTEAD
If you use pyRevit, you can install the same buttons as a pyRevit
extension and update them from pyRevit. Do not use both methods.
1. Run pyRevit-AddSource.bat once.
2. In Revit: pyRevit tab > Extensions > ARYEOfficeTools > Install Extension.
