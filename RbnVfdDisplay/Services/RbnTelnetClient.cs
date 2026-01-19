using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RbnVfdDisplay.Models;

namespace RbnVfdDisplay.Services
{
    /// <summary>
    /// Telnet client for connecting to the Reverse Beacon Network
    /// </summary>
    public class RbnTelnetClient : IDisposable
    {
        // RBN Telnet servers
        private const string RbnHost = "telnet.reversebeacon.net";
        private const int RbnPort = 7000;

        private TcpClient? _tcpClient;
        private NetworkStream? _networkStream;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private CancellationTokenSource? _cts;
        private Task? _readTask;
        private bool _disposed;
        private bool _isConnected;

        private readonly SpotStore _spotStore;
        private string _callsign = string.Empty;

        // Regex to parse RBN spot lines
        // Example: "DX de K3LR-#:    14023.0  WB2JAL         CW    14 dB  25 WPM  CQ      0920Z"
        private static readonly Regex SpotRegex = new(
            @"DX de (\S+):\s+(\d+\.?\d*)\s+(\S+)\s+(\w+)\s+(\d+)\s+dB\s+(\d+)\s+WPM",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public event EventHandler<string>? StatusChanged;
        public event EventHandler<string>? DataReceived;
        public event EventHandler<RawSpot>? SpotReceived;

        public bool IsConnected => _isConnected;

        public RbnTelnetClient(SpotStore spotStore)
        {
            _spotStore = spotStore;
        }

        /// <summary>
        /// Connect to RBN and start receiving spots
        /// </summary>
        public async Task ConnectAsync(string callsign, CancellationToken cancellationToken = default)
        {
            if (_isConnected)
            {
                throw new InvalidOperationException("Already connected");
            }

            _callsign = callsign.ToUpperInvariant();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                OnStatusChanged($"Connecting to {RbnHost}:{RbnPort}...");

                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(RbnHost, RbnPort);
                
                _networkStream = _tcpClient.GetStream();
                _reader = new StreamReader(_networkStream, Encoding.ASCII);
                _writer = new StreamWriter(_networkStream, Encoding.ASCII) { AutoFlush = true };

                _isConnected = true;
                OnStatusChanged("Connected, waiting for login prompt...");

                // Start reading task
                _readTask = Task.Run(() => ReadLoopAsync(_cts.Token), _cts.Token);
            }
            catch (Exception ex)
            {
                OnStatusChanged($"Connection failed: {ex.Message}");
                Disconnect();
                throw;
            }
        }

        /// <summary>
        /// Disconnect from RBN
        /// </summary>
        public void Disconnect()
        {
            _isConnected = false;
            _cts?.Cancel();

            try
            {
                _writer?.Dispose();
                _reader?.Dispose();
                _networkStream?.Dispose();
                _tcpClient?.Dispose();
            }
            catch
            {
                // Ignore cleanup errors
            }

            _writer = null;
            _reader = null;
            _networkStream = null;
            _tcpClient = null;

            OnStatusChanged("Disconnected");
        }

        private async Task ReadLoopAsync(CancellationToken cancellationToken)
        {
            bool loggedIn = false;

            try
            {
                while (!cancellationToken.IsCancellationRequested && _reader != null)
                {
                    var line = await _reader.ReadLineAsync();
                    
                    if (line == null)
                    {
                        break; // Connection closed
                    }

                    OnDataReceived(line);

                    // Handle login
                    if (!loggedIn && line.Contains("Please enter your call", StringComparison.OrdinalIgnoreCase))
                    {
                        if (_writer != null)
                        {
                            await _writer.WriteLineAsync(_callsign);
                            OnStatusChanged($"Logged in as {_callsign}");
                            loggedIn = true;
                        }
                        continue;
                    }

                    // Parse spot lines
                    if (line.StartsWith("DX de", StringComparison.OrdinalIgnoreCase))
                    {
                        var spot = ParseSpotLine(line);
                        if (spot != null)
                        {
                            _spotStore.AddSpot(spot);
                            OnSpotReceived(spot);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation
            }
            catch (Exception ex)
            {
                OnStatusChanged($"Read error: {ex.Message}");
            }
            finally
            {
                _isConnected = false;
                OnStatusChanged("Connection closed");
            }
        }

        /// <summary>
        /// Parse an RBN spot line into a RawSpot object
        /// </summary>
        private RawSpot? ParseSpotLine(string line)
        {
            try
            {
                var match = SpotRegex.Match(line);
                if (!match.Success)
                {
                    return null;
                }

                var spot = new RawSpot
                {
                    SpotterCallsign = match.Groups[1].Value.TrimEnd('-', '#', ':'),
                    FrequencyKhz = double.Parse(match.Groups[2].Value),
                    SpottedCallsign = match.Groups[3].Value,
                    Mode = match.Groups[4].Value,
                    Snr = int.Parse(match.Groups[5].Value),
                    SpeedWpm = int.Parse(match.Groups[6].Value),
                    TimestampUtc = DateTime.UtcNow
                };

                return spot;
            }
            catch
            {
                return null;
            }
        }

        private void OnStatusChanged(string status)
        {
            StatusChanged?.Invoke(this, status);
        }

        private void OnDataReceived(string data)
        {
            DataReceived?.Invoke(this, data);
        }

        private void OnSpotReceived(RawSpot spot)
        {
            SpotReceived?.Invoke(this, spot);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Disconnect();
                _cts?.Dispose();
                _disposed = true;
            }
        }
    }
}
