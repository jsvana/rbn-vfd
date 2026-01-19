using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RbnVfdDisplay.Models;
using RbnVfdDisplay.Services;

namespace RbnVfdDisplay
{
    /// <summary>
    /// Main window for RBN VFD Spot Display application
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly SpotStore _spotStore;
        private readonly RbnTelnetClient _rbnClient;
        private readonly VfdDisplayService _vfdService;
        private readonly DispatcherTimer _uiUpdateTimer;
        private readonly DispatcherTimer _spotRateTimer;
        
        private int _spotsReceivedLastMinute;
        private DateTime _lastSpotRateReset = DateTime.UtcNow;
        private bool _isClosing;

        public MainWindow()
        {
            InitializeComponent();

            // Initialize services
            _spotStore = new SpotStore();
            _rbnClient = new RbnTelnetClient(_spotStore);
            _vfdService = new VfdDisplayService(_spotStore);

            // Wire up events
            _spotStore.SpotsChanged += SpotStore_SpotsChanged;
            _rbnClient.StatusChanged += RbnClient_StatusChanged;
            _rbnClient.DataReceived += RbnClient_DataReceived;
            _rbnClient.SpotReceived += RbnClient_SpotReceived;
            _vfdService.StatusChanged += VfdService_StatusChanged;
            _vfdService.DisplayUpdated += VfdService_DisplayUpdated;

            // UI update timer - refresh spot list every 2 seconds
            _uiUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _uiUpdateTimer.Tick += UiUpdateTimer_Tick;
            _uiUpdateTimer.Start();

            // Spot rate timer - calculate spots per minute
            _spotRateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            _spotRateTimer.Tick += SpotRateTimer_Tick;
            _spotRateTimer.Start();

            // Initialize COM ports dropdown
            RefreshComPorts();

            Log("Application started");
        }

        #region Event Handlers

        private void SpotStore_SpotsChanged(object? sender, SpotStoreChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                SpotCountText.Text = $"Active spots: {e.SpotCount}";
            });
        }

        private void RbnClient_StatusChanged(object? sender, string status)
        {
            Dispatcher.BeginInvoke(() =>
            {
                RbnStatusText.Text = status;
                StatusBarText.Text = status;
                Log($"RBN: {status}");

                // Update connect button state
                if (_rbnClient.IsConnected)
                {
                    ConnectButton.Content = "Disconnect";
                    RbnStatusText.Foreground = System.Windows.Media.Brushes.Green;
                }
                else
                {
                    ConnectButton.Content = "Connect";
                    RbnStatusText.Foreground = System.Windows.Media.Brushes.Gray;
                }
            });
        }

        private void RbnClient_DataReceived(object? sender, string data)
        {
            Dispatcher.BeginInvoke(() =>
            {
                // Limit raw data text box size
                if (RawDataTextBox.Text.Length > 100000)
                {
                    RawDataTextBox.Text = RawDataTextBox.Text.Substring(50000);
                }

                RawDataTextBox.AppendText(data + Environment.NewLine);

                if (AutoScrollCheckBox.IsChecked == true)
                {
                    RawDataTextBox.ScrollToEnd();
                }
            });
        }

        private void RbnClient_SpotReceived(object? sender, RawSpot spot)
        {
            Interlocked.Increment(ref _spotsReceivedLastMinute);
        }

        private void VfdService_StatusChanged(object? sender, string status)
        {
            Dispatcher.BeginInvoke(() =>
            {
                VfdStatusText.Text = status;
                Log($"VFD: {status}");

                // Update button state
                if (_vfdService.IsOpen)
                {
                    OpenVfdButton.Content = "Close";
                    VfdStatusText.Foreground = System.Windows.Media.Brushes.Green;
                }
                else
                {
                    OpenVfdButton.Content = "Open";
                    VfdStatusText.Foreground = System.Windows.Media.Brushes.Gray;
                }
            });
        }

        private void VfdService_DisplayUpdated(object? sender, string displayText)
        {
            Dispatcher.BeginInvoke(() =>
            {
                // Parse display text and update preview
                var lines = displayText.Split('\n');
                if (lines.Length >= 1 && lines[0].StartsWith("Line 1:"))
                {
                    VfdLine1Preview.Text = lines[0].Replace("Line 1:", "").Trim().PadRight(24).Substring(0, 24);
                }
                if (lines.Length >= 2 && lines[1].StartsWith("Line 2:"))
                {
                    var line2Text = lines[1].Replace("Line 2:", "").Trim();
                    if (line2Text == "(empty)")
                    {
                        line2Text = "";
                    }
                    VfdLine2Preview.Text = line2Text.PadRight(24).Substring(0, 24);
                }
            });
        }

        private void UiUpdateTimer_Tick(object? sender, EventArgs e)
        {
            RefreshSpotsList();
        }

        private void SpotRateTimer_Tick(object? sender, EventArgs e)
        {
            var elapsed = (DateTime.UtcNow - _lastSpotRateReset).TotalMinutes;
            if (elapsed > 0)
            {
                var rate = _spotsReceivedLastMinute / elapsed;
                SpotRateText.Text = $"Spots/min: {rate:F0}";
            }

            // Reset counter every minute
            if (elapsed >= 1)
            {
                _spotsReceivedLastMinute = 0;
                _lastSpotRateReset = DateTime.UtcNow;
            }
        }

        #endregion

        #region Button Click Handlers

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_rbnClient.IsConnected)
            {
                _rbnClient.Disconnect();
            }
            else
            {
                var callsign = CallsignTextBox.Text.Trim().ToUpperInvariant();
                if (string.IsNullOrEmpty(callsign))
                {
                    MessageBox.Show("Please enter your callsign.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ConnectButton.IsEnabled = false;
                try
                {
                    await _rbnClient.ConnectAsync(callsign);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to connect: {ex.Message}", "Connection Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    ConnectButton.IsEnabled = true;
                }
            }
        }

        private void OpenVfdButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vfdService.IsOpen)
            {
                _vfdService.Close();
            }
            else
            {
                var portName = ComPortComboBox.SelectedItem as string;
                if (string.IsNullOrEmpty(portName))
                {
                    MessageBox.Show("Please select a COM port.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    _vfdService.Open(portName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to open VFD: {ex.Message}", "VFD Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void RefreshPortsButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshComPorts();
        }

        private void ClearSpotsButton_Click(object sender, RoutedEventArgs e)
        {
            _spotStore.Clear();
            RefreshSpotsList();
            Log("Spots cleared");
        }

        private void ClearRawDataButton_Click(object sender, RoutedEventArgs e)
        {
            RawDataTextBox.Clear();
        }

        #endregion

        #region Radio Button Handlers

        private void SnrRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tagStr && int.TryParse(tagStr, out int snr))
            {
                _spotStore.MinimumSnr = snr;
                Log($"Minimum SNR set to {snr} dB");
            }
        }

        private void AgeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tagStr && int.TryParse(tagStr, out int age))
            {
                _spotStore.MaxAgeMinutes = age;
                Log($"Spot expiry set to {age} minutes");
            }
        }

        private void ScrollRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tagStr && int.TryParse(tagStr, out int interval))
            {
                _vfdService.ScrollIntervalMs = interval;
                Log($"Scroll interval set to {interval / 1000.0:F1} seconds");
            }
        }

        #endregion

        #region Helper Methods

        private void RefreshComPorts()
        {
            var currentSelection = ComPortComboBox.SelectedItem as string;
            ComPortComboBox.Items.Clear();

            var ports = VfdDisplayService.GetAvailablePorts();
            foreach (var port in ports.OrderBy(p => p))
            {
                ComPortComboBox.Items.Add(port);
            }

            // Restore selection if still available
            if (currentSelection != null && ComPortComboBox.Items.Contains(currentSelection))
            {
                ComPortComboBox.SelectedItem = currentSelection;
            }
            else if (ComPortComboBox.Items.Count > 0)
            {
                ComPortComboBox.SelectedIndex = 0;
            }

            Log($"Found {ports.Length} COM ports");
        }

        private void RefreshSpotsList()
        {
            if (_isClosing) return;

            var spots = _spotStore.GetSpotsByRecency();
            
            // Create display items with the formatted display string
            var displayItems = spots.Select(s => new SpotDisplayItem
            {
                Callsign = s.Callsign,
                FrequencyKhz = s.FrequencyKhz,
                HighestSnr = s.HighestSnr,
                AverageSpeed = s.AverageSpeed,
                SpotCount = s.SpotCount,
                LastSpottedUtc = s.LastSpottedUtc,
                DisplayString = s.ToDisplayString()
            }).ToList();

            SpotsListView.ItemsSource = displayItems;
        }

        private void Log(string message)
        {
            Dispatcher.BeginInvoke(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                DebugLogTextBox.AppendText($"[{timestamp}] {message}{Environment.NewLine}");
                DebugLogTextBox.ScrollToEnd();
            });
        }

        #endregion

        #region Window Events

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            _isClosing = true;
            
            _uiUpdateTimer.Stop();
            _spotRateTimer.Stop();

            _rbnClient.Disconnect();
            _vfdService.Close();

            _rbnClient.Dispose();
            _vfdService.Dispose();
            _spotStore.Dispose();
        }

        #endregion
    }

    /// <summary>
    /// Display item for the spots ListView
    /// </summary>
    public class SpotDisplayItem
    {
        public string Callsign { get; set; } = string.Empty;
        public double FrequencyKhz { get; set; }
        public int HighestSnr { get; set; }
        public double AverageSpeed { get; set; }
        public int SpotCount { get; set; }
        public DateTime LastSpottedUtc { get; set; }
        public string DisplayString { get; set; } = string.Empty;
    }
}
