# RBN VFD Spot Display

A Windows application that displays amateur radio spots from the Reverse Beacon Network (RBN) on an ELO 24x2 VFD customer-facing display.

## Features

- **Real-time RBN Connection**: Connects to the Reverse Beacon Network via telnet to receive CW spots
- **Intelligent Spot Aggregation**:
  - Retains highest SNR for each spotted station
  - Averages reported speeds for a given station
  - Averages frequencies within 1 kHz of each other
  - Spots more than 1 kHz apart are treated as distinct
- **Automatic Purging**: Old spots are automatically removed based on configurable age
- **ELO VFD Display Support**: Outputs to ELO E122426 / ESYxxE2x series 24x2 character VFD displays
- **Configurable Settings**:
  - Minimum SNR filter (0, 10, 20, 30, 40 dB)
  - Spot expiry time (5, 10, 15, 30, 60 minutes)
  - Display scroll interval (1, 2, 3, 5, 10 seconds)

## Hardware Requirements

- **ELO VFD Display**: Model E122426 or ESYxxE2x series
  - 24 character x 2 line display
  - USB connection (appears as COM port)
  - Settings: 9600 baud, 8 data bits, no parity, 1 stop bit

## Software Requirements

- Windows 10 or later
- .NET 8.0 Runtime (or SDK for building)

## Building

### Prerequisites

1. Install [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Build Commands

```bash
# Navigate to solution directory
cd RbnVfdDisplay

# Restore packages and build
dotnet restore
dotnet build -c Release

# Or publish as a self-contained executable
dotnet publish -c Release -r win-x64 --self-contained true -o ./publish
```

The executable will be in `RbnVfdDisplay/bin/Release/net8.0-windows/` or `./publish/` for self-contained builds.

## Usage

1. **Launch the application**

2. **Configure RBN Connection**:
   - Enter your amateur radio callsign
   - Click "Connect" to establish telnet connection to RBN

3. **Configure VFD Display**:
   - Select the COM port for your ELO VFD
   - Click "Open" to start sending data to the display

4. **Adjust Settings**:
   - **Minimum SNR**: Filter out weak spots below this threshold
   - **Spot Expiry**: How long spots remain in the list after last being received
   - **Scroll Interval**: How fast the display rotates through spots (when >2 spots)

## Display Format

Each line on the VFD shows one spot in the format:
```
14033.0 WO6W 24
```
- Frequency in kHz to 100 Hz resolution
- Callsign
- Speed in WPM

## Spot Aggregation Logic

When multiple RBN stations report the same call:

1. **Same frequency (within 1 kHz)**:
   - Highest SNR is retained
   - Speed is averaged
   - Frequency is averaged
   - Last spotted time is updated

2. **Different frequencies (>1 kHz apart)**:
   - Treated as separate spots (e.g., different bands)

## Display Scrolling

- **0-1 spots**: Display shows "Waiting for spots..." or the single spot
- **2 spots**: Both spots displayed, no scrolling
- **3+ spots**: Display scrolls through spots at the configured interval

## Tabs

- **Active Spots**: Shows all current spots in a sortable list
- **RBN Raw Data**: Shows raw telnet data from RBN
- **Debug Log**: Application log messages

## Troubleshooting

### VFD Not Displaying

1. Check COM port is correct (use Device Manager)
2. Verify display is powered on
3. Try refreshing COM ports list
4. Check USB connection

### RBN Connection Issues

1. Verify internet connectivity
2. Check firewall allows outbound port 7000
3. Ensure callsign is valid

### No Spots Appearing

1. Lower the minimum SNR threshold
2. Check RBN Raw Data tab for incoming data
3. Verify RBN connection status

## License

MIT License - Free for amateur radio use.

## Author

Created for W6JSV by Claude AI

## References

- [Reverse Beacon Network](https://reversebeacon.net/)
- [ELO Touch Solutions](https://www.elotouch.com/)
