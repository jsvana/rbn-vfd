# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

RBN VFD Spot Display - A Windows WPF application that displays amateur radio spots from the Reverse Beacon Network (RBN) on an ELO 24x2 VFD customer-facing display. Created for W6JSV.

## Build Commands

```bash
# Build
dotnet build -c Release

# Publish self-contained executable
dotnet publish -c Release -r win-x64 --self-contained true -o ./publish
```

Output: `RbnVfdDisplay/bin/Release/net8.0-windows/RbnVfdDisplay.exe`

## Releasing

Push a tag to trigger the GitHub Actions release workflow:

```bash
git tag v1.0.0
git push origin v1.0.0
```

This builds a self-contained Windows x64 binary and creates a GitHub release with `RbnVfdDisplay-win-x64.zip`.

## Architecture

```
RBN Telnet Server → RbnTelnetClient → SpotStore → MainWindow UI → VfdDisplayService → ELO VFD
```

**Models** (`Models/Spot.cs`):
- `RawSpot`: Incoming RBN telnet data (callsign, frequency, SNR, speed, mode)
- `AggregatedSpot`: Aggregated spot data grouped by callsign + center frequency (1 kHz threshold)

**Services**:
- `RbnTelnetClient`: Async telnet connection to telnet.reversebeacon.net:7000, parses spots via regex
- `SpotStore`: Thread-safe `ConcurrentDictionary` storage with automatic purging, SNR filtering, spot aggregation
- `VfdDisplayService`: Serial port output (9600/8/N/1) with ESC/POS commands, timer-based scrolling

**UI** (`MainWindow.xaml`):
- Three tabs: Active Spots, RBN Raw Data, Debug Log
- Controls: RBN connection, VFD COM port, filter radio buttons (SNR, expiry, scroll interval)
- VFD Preview panel (green-on-black monospace)

## Key Patterns

- **Thread safety**: `ConcurrentDictionary` for spot storage, `lock` for serial port access
- **Async/await**: Task-based telnet reading with CancellationToken support
- **Event-driven**: Services expose events (`SpotReceived`, `SpotsChanged`, `StatusChanged`) consumed by UI
- **DispatcherTimer**: UI updates every 2 seconds, spot rate calculation every 10 seconds

## Display Format

24 characters max per line: `"{FrequencyKhz:F1} {Callsign} {HighestSnr}"`

Example: `14033.0 WO6W 24`

## Dependencies

- .NET 8.0 (Windows)
- System.IO.Ports 8.0.0 (NuGet)
- WPF (built-in)
