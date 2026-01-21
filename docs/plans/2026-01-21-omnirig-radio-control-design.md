# Radio Control Feature Design

## Overview

Add radio control support to the RBN VFD application, allowing users to tune their radio to a selected spot's frequency and mode. Supports OmniRig on Windows and rigctld (Hamlib) on macOS/Linux.

## Requirements

- Click a spot row to select it, with a separate Tune button to execute
- Double-click a spot row as a shortcut to tune immediately
- Set both frequency and mode when tuning
- Radio configuration in a separate settings dialog
- Connection status shown in both status line and indicator near Tune button
- Popup dialog for tune failures

## Architecture

```
                         ┌─────────────────────┐
                         │    RbnVfdApp (UI)   │
                         │  - selected_spot    │
                         │  - Tune button      │
                         └──────────┬──────────┘
                                    │
                         ┌──────────▼──────────┐
                         │   RadioController   │
                         │      (trait)        │
                         │  - connect()        │
                         │  - tune(freq, mode) │
                         │  - is_connected()   │
                         └──────────┬──────────┘
                                    │
              ┌─────────────────────┼─────────────────────┐
              │                     │                     │
   ┌──────────▼──────────┐         │          ┌──────────▼──────────┐
   │  OmniRigController  │         │          │  RigctldController  │
   │    (Windows only)   │         │          │  (macOS/Linux)      │
   │   COM interop       │         │          │  TCP client         │
   └─────────────────────┘         │          └─────────────────────┘
                                   │
                        ┌──────────▼──────────┐
                        │   NoOpController    │
                        │  (radio disabled)   │
                        └─────────────────────┘
```

### New Files

- `src/services/radio/mod.rs` - trait definition and factory function
- `src/services/radio/rigctld.rs` - TCP client for rigctld
- `src/services/radio/omnirig.rs` - Windows COM wrapper (conditional compilation)
- `src/services/radio/noop.rs` - placeholder when radio is disabled

### Configuration Additions

Added to `config.rs` and persisted in `settings.ini`:

- `radio_enabled: bool`
- `radio_backend: String` ("omnirig" or "rigctld")
- `rigctld_host: String` (default: "localhost")
- `rigctld_port: u16` (default: 4532)
- `omnirig_rig: u8` (1 or 2)

## UI Changes

### Main Window

1. **Spot selection in Active Spots table** - Each row becomes clickable. Clicking highlights the row with a distinct background color. The selected spot is stored in `app.selected_spot: Option<AggregatedSpot>`.

2. **Tune button and indicator** - Added below the Active Spots section:
   ```
   [●] [Tune]
   ```
   - Green indicator when connected, red when disconnected
   - Button disabled when no spot selected or radio not connected
   - Double-clicking a spot row triggers tune immediately

3. **Radio status in status line** - Extends current status display:
   ```
   VFD: /dev/ttyUSB0 | Radio: rigctld connected
   ```

### Settings Dialog

New modal window accessible via "Radio Settings..." button. Contents:

- **Enable radio control** checkbox
- **Backend selection** (platform-dependent):
  - Windows: "OmniRig" with Rig dropdown (Rig 1 / Rig 2)
  - macOS/Linux: "rigctld" with Host and Port fields
- **Test Connection** button
- **OK / Cancel** buttons

## Mode Mapping

| RBN Mode | Radio Mode | Notes |
|----------|------------|-------|
| CW | CW | Standard CW mode |
| RTTY | RTTY or USB | Some radios have RTTY, others use USB |
| FT8 | USB | Digital mode, upper sideband |
| FT4 | USB | Digital mode, upper sideband |
| PSK31 | USB | Digital mode |
| JT65 | USB | Digital mode |
| JT9 | USB | Digital mode |

**Fallback:** Unrecognized modes default to CW (RBN is primarily CW spots).

**Frequency conversion:** RBN reports kHz, radio expects Hz: `freq_hz = (freq_khz * 1000.0) as u64`

## Error Handling

| Scenario | Behavior |
|----------|----------|
| rigctld not running | Popup: "Cannot connect to rigctld at localhost:4532. Is rigctld running?" |
| OmniRig not installed | Popup: "Cannot connect to OmniRig. Is OmniRig installed and running?" |
| Connection lost mid-session | Indicator turns red, status line updates, next tune attempt shows popup |
| Tune command fails | Popup: "Failed to tune radio: [error details]" |
| Radio busy/timeout | Popup: "Radio not responding. Check connection." |

## Implementation Details

### rigctld (`rigctld.rs`)

- Uses `std::net::TcpStream` (synchronous)
- Connection established on-demand or via Test Connection
- Commands: `F <freq_hz>` for frequency, `M <mode> <width>` for mode
- Timeout: 2-3 seconds for commands

### OmniRig (`omnirig.rs`)

- Uses `windows` crate for COM interop
- Wrapped in `#[cfg(target_os = "windows")]` conditional compilation
- Creates OmniRig.Engine COM object, accesses Rig1 or Rig2
- Sets `Freq` and `Mode` properties

### Build Considerations

- Windows: Both backends compiled, OmniRig is default
- macOS/Linux: Only rigctld backend compiled, OmniRig code excluded via `#[cfg]`
- `NoOpController` always available for disabled state
