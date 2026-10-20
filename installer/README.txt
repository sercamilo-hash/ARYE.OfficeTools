ARYE Office Tools {VERSION} - for Revit 2026
============================================

Two buttons on the ARYE tab, Zoning panel:

- Region Area -> Param: writes the area of Filled Regions into a parameter
  (for example "Region Area" on the energy elevations).
- Light & Vent: adds up the light area and the vent area of each room's
  exterior windows and writes the two totals to the room.

INSTALL
1. Close Revit.
2. Extract this whole zip to a folder (right-click > Extract All).
   Do not run Install.bat from inside the zip.
3. Double-click Install.bat. Wait for "[OK] Installed".
   If Windows shows "Windows protected your PC", click
   "More info" > "Run anyway".
4. Open Revit 2026 and go to the ARYE tab, Zoning panel.
   If Revit asks whether to load the add-in, choose "Always Load".

UPDATE
Close Revit and run Install.bat from the new version. It replaces the old
one, including ARYE Region Area 1.0.0.

UNINSTALL
Close Revit and double-click Uninstall.bat.

No administrator rights are needed: everything is installed for your
Windows user only, in %APPDATA%\Autodesk\Revit\Addins\2026\.

Problems? Send the log in %LOCALAPPDATA%\ARYE\OfficeTools\logs\
