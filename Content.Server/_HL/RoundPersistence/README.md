# Round Persistence System

## Overview

The Round Persistence System is a comprehensive solution to maintain critical game state across round restarts in Space Station 14. This system ensures that when the primary station is deleted and recreated during round restarts, essential functionality like expeditions, ship tracking, player records, and ship associations continue to work properly.

## Problem Solved

Previously, when rounds restarted, the following critical systems would break:

1. **Expedition Console**: Lost mission data, cooldowns, and active expedition state
2. **Shuttle Records**: Lost ship ownership records and purchase history
3. **IFF System**: Ships disappeared from radar and navigation systems
4. **Player Manifest**: Lost crew records and job assignments
5. **Ship-Station Associations**: Ships lost their docking capabilities and station relationships
6. **Autopay System**: Lost player payment tracking data

## Architecture

### Components

#### `RoundPersistenceComponent`
- Stores all critical data that needs to persist across rounds
- Contains dictionaries for expedition data, shuttle records, station records, ship data, and player payments
- Applied to a persistent entity that survives round transitions

#### Persistence Data Types
- `PersistedExpeditionData`: Mission data, cooldowns, active expeditions
- `PersistedStationRecords`: Crew manifest, job slots, station records
- `PersistedShipData`: IFF flags, ownership, station associations
- `PersistedPlayerPayment`: Payment tracking, work hours, job history

### Systems

#### `RoundPersistenceSystem`
Main orchestrator that:
- Listens for round restart events
- Saves data before station deletion
- Restores data when new stations are created
- Manages the persistent entity lifecycle

#### `PlayerPaymentPersistenceSystem`
Specialized system for tracking player work sessions and payment data across rounds.

## Configuration

All features can be configured via CVars in `HLCCVars.cs`:

```toml
# Enable/disable the entire system
hardlight.round_persistence.enabled = true

# Individual feature toggles
hardlight.round_persistence.expeditions = true
hardlight.round_persistence.shuttle_records = true
hardlight.round_persistence.station_records = true
hardlight.round_persistence.ship_data = true
hardlight.round_persistence.player_payments = true

# Maintenance settings
hardlight.round_persistence.max_rounds = 10
hardlight.round_persistence.debug_logging = false
```

## Admin Commands

### `save_persistent_data`
Force save all persistent data immediately.

### `persistent_data_status`
Show current status and statistics of the persistence system.

### `clear_persistent_data` (Admin only)
Clear all persistent data. Use with caution!

## How It Works

### Round Restart Process

1. **Pre-Restart (OnRoundRestart)**:
   - `RoundRestartCleanupEvent` is fired by GameTicker
   - Persistence system captures all critical data from stations
   - Data is stored in a persistent entity on a dedicated map

2. **Station Deletion**:
   - GameTicker deletes all station entities as normal
   - Persistent entity and its data survive on the isolated map

3. **New Round Start (OnRoundStarted)**:
   - System waits for new stations to be created
   - When stations initialize, data is automatically restored

4. **Data Restoration**:
   - Expedition data: Missions, cooldowns, active state
   - Shuttle records: Ownership, purchase history
   - Station records: Crew manifest, job assignments
   - Ship data: IFF flags, associations, ownership

### Ship Handling

When ships are spawned or recreated:
1. System detects `ShuttleComponent` initialization
2. Looks up ship in persistent data by NetEntity ID
3. Restores IFF flags, colors, and station associations
4. Updates metadata with correct ship name

## Implementation Details

### Data Integrity

- All data is stored with timestamps and round numbers
- System validates data before restoration
- Graceful fallback if persistence data is corrupted

### Performance

- Persistence entity uses a dedicated map to avoid cleanup
- Data is only saved/restored during round transitions
- Minimal overhead during normal gameplay

### Compatibility

- System is designed to be non-intrusive
- Can be disabled via configuration without affecting base game
- Hooks into existing event system without modifying core files

## Testing

### Validation Steps

1. **Expedition Continuity**:
   - Start an expedition before round restart
   - Verify expedition continues after restart
   - Check mission timers and cooldowns persist

2. **Ship Persistence**:
   - Purchase/load ships before restart
   - Verify ships appear on IFF after restart
   - Test docking capabilities persist

3. **Records Continuity**:
   - Create player records before restart
   - Verify crew manifest persists
   - Check shuttle records console shows previous ships

4. **Payment Tracking**:
   - Work on station before restart
   - Verify accumulated work time persists
   - Test autopay continues functioning

### Debug Commands

Enable debug logging for detailed information:
```
hardlight.round_persistence.debug_logging true
```

## Troubleshooting

### Common Issues

1. **Data Not Persisting**:
   - Check `hardlight.round_persistence.enabled` is true
   - Verify no errors in server logs during round restart
   - Use `persistent_data_status` to check system status

2. **Ships Not Appearing on IFF**:
   - Ensure `hardlight.round_persistence.ship_data` is enabled
   - Check ships have `IFFComponent` before restart
   - Verify ships are properly associated with a station

3. **Expeditions Not Working**:
   - Confirm `hardlight.round_persistence.expeditions` is enabled
   - Check expedition console has data before restart
   - Verify station naming consistency

### Log Analysis

Look for these log entries:
- "Round restart detected, saving persistent data..."
- "Restored expedition data with X missions"
- "Restored IFF data for ship: [name]"

## Future Enhancements

Potential improvements:
1. **Database Persistence**: Store data in SQL database for server restarts
2. **Player Ship Tracking**: More detailed ship state preservation
3. **Economy Persistence**: Bank account and transaction history
4. **Cross-Server Persistence**: Share data between multiple servers
5. **Selective Restoration**: Choose which data to restore per restart

## Contributing

When modifying this system:
1. Update relevant data structures in `RoundPersistenceComponent`
2. Add save/restore logic to appropriate systems
3. Update configuration variables if needed
4. Add appropriate logging for debugging
5. Test thoroughly with round restarts

## Files

- `Content.Server\_HL\RoundPersistence\Components\RoundPersistenceComponent.cs`
- `Content.Server\_HL\RoundPersistence\Systems\RoundPersistenceSystem.cs`
- `Content.Server\_HL\RoundPersistence\Systems\PlayerPaymentPersistenceSystem.cs`
- `Content.Server\_HL\RoundPersistence\Commands\PersistenceCommands.cs`
- `Content.Shared\_HL\CCVar\HLCCVars.cs`
