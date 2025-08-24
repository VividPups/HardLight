# Round Persistence Expedition Fix Test

## Problem Description
- **Error**: "Dungeon data keys are missing for gen" occurring on second round
- **Cause**: Round persistence system clears missions but doesn't regenerate them properly
- **Result**: Fallback mission generation creates incomplete missions missing dungeon/biome/faction data

## Fix Applied
1. **Modified `RoundPersistenceSystem.cs`** (line 396):
   - **Before**: `expeditionComp.Missions.Clear(); // Clear invalid missions - system will regenerate`
   - **After**: `_salvageSystem.ForceGenerateMissions(expeditionComp);`

2. **Added `ForceGenerateMissions()` method to `SalvageSystem.Expeditions.cs`**:
   - Public method that calls the proper `GenerateMissions()` method
   - Ensures missions are regenerated using the correct system logic

## Test Procedure
1. Start server
2. Start first round - verify expeditions work
3. Restart round (admin command or natural restart)
4. On second round, check expedition console:
   - Should show missions without errors
   - Should be able to claim and start expeditions
   - Dungeon generation should work without "missing keys" error

## Expected Results
- **Before Fix**: Second round shows "Dungeon data keys are missing for gen" error, FTL fails
- **After Fix**: Second round expeditions work normally, no dungeon errors

## Files Modified
- `Content.Server/_HL/RoundPersistence/Systems/RoundPersistenceSystem.cs`
- `Content.Server/Salvage/SalvageSystem.Expeditions.cs`

## Root Cause Analysis
The issue was that round persistence correctly saved and restored expedition data, but when invalid missions were detected, it only cleared them without properly regenerating new ones. This caused the expedition console to fall back to `GenerateLocalMissions()` which creates incomplete missions that lack the dungeon configuration data needed for proper dungeon generation.

The fix ensures that when missions are cleared due to invalid state, they are immediately regenerated using the proper `GenerateMissions()` method that creates complete mission parameters.
