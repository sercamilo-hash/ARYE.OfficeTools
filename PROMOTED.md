# Promoted buttons

Written by `build/promote.py`. Every file under `src/` and `tests/` except `App.cs`, `Commands/AboutCommand.cs` and the project files is a copy from BauTools: change it there.

> **Exception in 1.6.0:** the Sheet Diagrammer was promoted from BauTools `b1f4280`, but **NYC Lot Boundary stays at the 1.5.0 state (BauTools `1e79c6e`)**. BauTools `b1f4280` also carries a newer NYC Lot (multi-lot, joint fit, orientation) that has not been approved for the office. Running `promote.py` again would pull it in: do not re-promote until it is approved.

- **BauTools commit:** `b1f4280962cc6780fc5a8f0df5509df5ae8577c4` (origin/main)
- **Promoted on:** 2026-11-18
- **Files:** 190 · **test files:** 41 · Passed!  - Failed:     0, Passed:   481, Skipped:     0, Total:   481, Duration: 78 ms - ARYE.OfficeTools.Tests.dll (net8.0)

| Panel | Button | Command |
|---|---|---|
| Site | NYC Lot Boundary | `CommandNycLot.cs` |
| Levels | Generate Levels | `GenerateLevelsCommand.cs` |
| Levels | Bubble Heads | `CommandBubbleHeads.cs` |
| Zoning | Zoning Floor Area | `ZfaCommand.cs` |
| Zoning | Region Area → Param | `CommandRegionAreaToParameter.cs` |
| Zoning | Light & Vent | `CommandRoomLightVent.cs` |
| Sheets | Sheet Diagrammer | `CommandSheetDiagrammer.cs` |

Deferred: Import Survey and Import from CSV (TopoGenerator): until TopoGenerator has an approved release on GitHub
