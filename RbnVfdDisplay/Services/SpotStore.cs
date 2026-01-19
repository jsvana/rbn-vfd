using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using RbnVfdDisplay.Models;

namespace RbnVfdDisplay.Services
{
    /// <summary>
    /// Thread-safe store for aggregated RBN spots with automatic purging
    /// </summary>
    public class SpotStore : IDisposable
    {
        private readonly ConcurrentDictionary<string, AggregatedSpot> _spots = new();
        private readonly object _updateLock = new();
        private Timer? _purgeTimer;
        private int _maxAgeMinutes = 10;
        private int _minimumSnr = 0;
        private bool _disposed;

        public event EventHandler<SpotStoreChangedEventArgs>? SpotsChanged;

        public int MaxAgeMinutes
        {
            get => _maxAgeMinutes;
            set => _maxAgeMinutes = Math.Max(1, value);
        }

        public int MinimumSnr
        {
            get => _minimumSnr;
            set => _minimumSnr = value;
        }

        public int SpotCount => _spots.Count;

        public SpotStore()
        {
            // Purge old spots every 10 seconds
            _purgeTimer = new Timer(PurgeOldSpots, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
        }

        /// <summary>
        /// Add or update a spot in the store
        /// </summary>
        public void AddSpot(RawSpot rawSpot)
        {
            if (rawSpot.Snr < _minimumSnr)
            {
                return; // Discard spots below minimum SNR
            }

            // Calculate the center frequency (rounded to nearest kHz) for grouping
            double centerFreq = Math.Round(rawSpot.FrequencyKhz);

            string key = $"{rawSpot.SpottedCallsign}|{centerFreq:F0}";

            lock (_updateLock)
            {
                if (_spots.TryGetValue(key, out var existing))
                {
                    // Update existing spot
                    existing.TotalSpeed += rawSpot.SpeedWpm;
                    existing.TotalFrequency += rawSpot.FrequencyKhz;
                    existing.SpotCount++;
                    existing.AverageSpeed = existing.TotalSpeed / existing.SpotCount;
                    existing.FrequencyKhz = existing.TotalFrequency / existing.SpotCount;
                    
                    // Keep highest SNR
                    if (rawSpot.Snr > existing.HighestSnr)
                    {
                        existing.HighestSnr = rawSpot.Snr;
                    }

                    // Update last spotted time
                    existing.LastSpottedUtc = DateTime.UtcNow;
                }
                else
                {
                    // Create new aggregated spot
                    var newSpot = new AggregatedSpot
                    {
                        Callsign = rawSpot.SpottedCallsign,
                        FrequencyKhz = rawSpot.FrequencyKhz,
                        CenterFrequencyKhz = centerFreq,
                        HighestSnr = rawSpot.Snr,
                        AverageSpeed = rawSpot.SpeedWpm,
                        TotalSpeed = rawSpot.SpeedWpm,
                        TotalFrequency = rawSpot.FrequencyKhz,
                        SpotCount = 1,
                        LastSpottedUtc = DateTime.UtcNow
                    };

                    _spots[key] = newSpot;
                }
            }

            OnSpotsChanged();
        }

        /// <summary>
        /// Get all current spots sorted by frequency
        /// </summary>
        public List<AggregatedSpot> GetAllSpots()
        {
            return _spots.Values
                .OrderBy(s => s.FrequencyKhz)
                .ToList();
        }

        /// <summary>
        /// Get spots sorted by most recently spotted
        /// </summary>
        public List<AggregatedSpot> GetSpotsByRecency()
        {
            return _spots.Values
                .OrderByDescending(s => s.LastSpottedUtc)
                .ToList();
        }

        /// <summary>
        /// Clear all spots
        /// </summary>
        public void Clear()
        {
            _spots.Clear();
            OnSpotsChanged();
        }

        private void PurgeOldSpots(object? state)
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-_maxAgeMinutes);
            var keysToRemove = new List<string>();

            foreach (var kvp in _spots)
            {
                if (kvp.Value.LastSpottedUtc < cutoff)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            if (keysToRemove.Count > 0)
            {
                foreach (var key in keysToRemove)
                {
                    _spots.TryRemove(key, out _);
                }
                OnSpotsChanged();
            }
        }

        private void OnSpotsChanged()
        {
            SpotsChanged?.Invoke(this, new SpotStoreChangedEventArgs(SpotCount));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _purgeTimer?.Dispose();
                _purgeTimer = null;
                _disposed = true;
            }
        }
    }

    public class SpotStoreChangedEventArgs : EventArgs
    {
        public int SpotCount { get; }

        public SpotStoreChangedEventArgs(int spotCount)
        {
            SpotCount = spotCount;
        }
    }
}
