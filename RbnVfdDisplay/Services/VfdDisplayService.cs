using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using RbnVfdDisplay.Models;

namespace RbnVfdDisplay.Services
{
    /// <summary>
    /// Controls the ELO 24x2 VFD Rear Facing Customer Display via serial port
    /// ELO E122426 / ESYxxE2x series
    /// </summary>
    public class VfdDisplayService : IDisposable
    {
        // VFD display dimensions
        private const int DisplayWidth = 24;
        private const int DisplayLines = 2;

        // ESC/POS style commands for ELO VFD
        private static readonly byte[] ClearDisplay = { 0x0C };              // Form feed - clear display
        private static readonly byte[] CursorHome = { 0x1B, 0x5B, 0x48 };   // ESC [ H - cursor home
        private static readonly byte[] MoveLine1 = { 0x1B, 0x5B, 0x31, 0x3B, 0x31, 0x48 };  // ESC [ 1;1 H
        private static readonly byte[] MoveLine2 = { 0x1B, 0x5B, 0x32, 0x3B, 0x31, 0x48 };  // ESC [ 2;1 H

        private SerialPort? _serialPort;
        private Timer? _scrollTimer;
        private int _scrollIndex;
        private int _scrollIntervalMs = 3000;
        private bool _disposed;
        private bool _isOpen;

        private readonly SpotStore _spotStore;
        private readonly object _displayLock = new();

        public event EventHandler<string>? StatusChanged;
        public event EventHandler<string>? DisplayUpdated;

        public bool IsOpen => _isOpen;
        
        public int ScrollIntervalMs
        {
            get => _scrollIntervalMs;
            set
            {
                _scrollIntervalMs = Math.Max(500, value);
                // Restart timer with new interval if running
                if (_scrollTimer != null && _isOpen)
                {
                    _scrollTimer.Change(_scrollIntervalMs, _scrollIntervalMs);
                }
            }
        }

        public VfdDisplayService(SpotStore spotStore)
        {
            _spotStore = spotStore;
        }

        /// <summary>
        /// Get list of available COM ports
        /// </summary>
        public static string[] GetAvailablePorts()
        {
            return SerialPort.GetPortNames();
        }

        /// <summary>
        /// Open the serial port and start display updates
        /// </summary>
        public void Open(string portName)
        {
            if (_isOpen)
            {
                Close();
            }

            try
            {
                _serialPort = new SerialPort(portName)
                {
                    BaudRate = 9600,
                    DataBits = 8,
                    Parity = Parity.None,
                    StopBits = StopBits.One,
                    Handshake = Handshake.None,
                    WriteTimeout = 1000,
                    ReadTimeout = 1000
                };

                _serialPort.Open();
                _isOpen = true;

                // Clear the display
                ClearVfd();

                // Start scroll timer
                _scrollIndex = 0;
                _scrollTimer = new Timer(ScrollTimerCallback, null, 0, _scrollIntervalMs);

                OnStatusChanged($"VFD opened on {portName}");
            }
            catch (Exception ex)
            {
                OnStatusChanged($"Failed to open {portName}: {ex.Message}");
                _isOpen = false;
                throw;
            }
        }

        /// <summary>
        /// Close the serial port
        /// </summary>
        public void Close()
        {
            _scrollTimer?.Dispose();
            _scrollTimer = null;

            if (_serialPort != null)
            {
                try
                {
                    if (_serialPort.IsOpen)
                    {
                        ClearVfd();
                        _serialPort.Close();
                    }
                }
                catch
                {
                    // Ignore close errors
                }
                _serialPort.Dispose();
                _serialPort = null;
            }

            _isOpen = false;
            OnStatusChanged("VFD closed");
        }

        /// <summary>
        /// Clear the VFD display
        /// </summary>
        private void ClearVfd()
        {
            if (_serialPort?.IsOpen == true)
            {
                try
                {
                    _serialPort.Write(ClearDisplay, 0, ClearDisplay.Length);
                }
                catch
                {
                    // Ignore write errors
                }
            }
        }

        /// <summary>
        /// Write text to a specific line (1 or 2)
        /// </summary>
        private void WriteToLine(int line, string text)
        {
            if (_serialPort?.IsOpen != true) return;

            try
            {
                lock (_displayLock)
                {
                    // Move cursor to line
                    byte[] moveCmd = line == 1 ? MoveLine1 : MoveLine2;
                    _serialPort.Write(moveCmd, 0, moveCmd.Length);

                    // Pad or truncate text to exactly DisplayWidth characters
                    string paddedText = text.PadRight(DisplayWidth);
                    if (paddedText.Length > DisplayWidth)
                    {
                        paddedText = paddedText.Substring(0, DisplayWidth);
                    }

                    // Write the text
                    _serialPort.Write(paddedText);
                }
            }
            catch (Exception ex)
            {
                OnStatusChanged($"VFD write error: {ex.Message}");
            }
        }

        /// <summary>
        /// Timer callback for scrolling display
        /// </summary>
        private void ScrollTimerCallback(object? state)
        {
            UpdateDisplay();
        }

        /// <summary>
        /// Update the display with current spots
        /// </summary>
        public void UpdateDisplay()
        {
            if (!_isOpen || _serialPort?.IsOpen != true) return;

            var spots = _spotStore.GetSpotsByRecency();
            
            if (spots.Count == 0)
            {
                // No spots - show waiting message
                WriteToLine(1, "Waiting for spots...");
                WriteToLine(2, "");
                OnDisplayUpdated("Line 1: Waiting for spots...\nLine 2: (empty)");
                return;
            }

            if (spots.Count == 1)
            {
                // Single spot - show on line 1 only
                string line1 = spots[0].ToDisplayString();
                WriteToLine(1, line1);
                WriteToLine(2, "");
                OnDisplayUpdated($"Line 1: {line1}\nLine 2: (empty)");
                return;
            }

            if (spots.Count == 2)
            {
                // Two spots - show both, no scrolling needed
                string line1 = spots[0].ToDisplayString();
                string line2 = spots[1].ToDisplayString();
                WriteToLine(1, line1);
                WriteToLine(2, line2);
                OnDisplayUpdated($"Line 1: {line1}\nLine 2: {line2}");
                return;
            }

            // More than 2 spots - scroll through them
            // Show spots at _scrollIndex and _scrollIndex + 1
            int idx1 = _scrollIndex % spots.Count;
            int idx2 = (_scrollIndex + 1) % spots.Count;

            string displayLine1 = spots[idx1].ToDisplayString();
            string displayLine2 = spots[idx2].ToDisplayString();

            WriteToLine(1, displayLine1);
            WriteToLine(2, displayLine2);

            OnDisplayUpdated($"Line 1: {displayLine1}\nLine 2: {displayLine2}\n[{idx1 + 1}/{spots.Count}] Showing spots {idx1 + 1} and {idx2 + 1}");

            // Advance scroll index for next update
            _scrollIndex++;
            if (_scrollIndex >= spots.Count)
            {
                _scrollIndex = 0;
            }
        }

        /// <summary>
        /// Force immediate display refresh
        /// </summary>
        public void RefreshDisplay()
        {
            UpdateDisplay();
        }

        private void OnStatusChanged(string status)
        {
            StatusChanged?.Invoke(this, status);
        }

        private void OnDisplayUpdated(string displayText)
        {
            DisplayUpdated?.Invoke(this, displayText);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Close();
                _disposed = true;
            }
        }
    }
}
