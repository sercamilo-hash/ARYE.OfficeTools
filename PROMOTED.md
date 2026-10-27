# Promoted buttons

Written by `build/promote.py`. Every file under `src/` and `tests/` except `App.cs`, `Commands/AboutCommand.cs` and the project files is a copy from BauTools: change it there.

- **BauTools commit:** `5cac160425b9d8b719a4f11e9c61f0360325e94b` (origin/main)
- **Promoted on:** 2026-10-27
- **Files:** 145 · **test files:** 24 · Passed!  - Failed:     0, Passed:   201, Skipped:     0, Total:   201, Duration: 60 ms - ARYE.OfficeTools.Tests.dll (net8.0)

| Panel | Button | Command |
|---|---|---|
| Site | NYC Lot Boundary | `CommandNycLot.cs` |
| Levels | Generate Levels | `GenerateLevelsCommand.cs` |
| Levels | Bubble Heads | `CommandBubbleHeads.cs` |
| Zoning | Zoning Floor Area | `ZfaCommand.cs` |
| Zoning | Region Area → Param | `CommandRegionAreaToParameter.cs` |
| Zoning | Light & Vent | `CommandRoomLightVent.cs` |

Deferred: Import Survey and Import from CSV (TopoGenerator): until TopoGenerator has an approved release on GitHub
