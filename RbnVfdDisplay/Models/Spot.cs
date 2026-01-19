using System;

namespace RbnVfdDisplay.Models
{
    /// <summary>
    /// Represents an aggregated RBN spot for a station on a specific frequency band
    /// </summary>
    public class AggregatedSpot
    {
        /// <summary>
        /// The spotted station's callsign
        /// </summary>
        public string Callsign { get; set; } = string.Empty;

        /// <summary>
        /// Average frequency in kHz (aggregated from spots within 1 kHz of each other)
        /// </summary>
        public double FrequencyKhz { get; set; }

        /// <summary>
        /// The center frequency used to group spots (rounded to nearest kHz)
        /// </summary>
        public double CenterFrequencyKhz { get; set; }

        /// <summary>
        /// Highest SNR observed for this station/frequency combination
        /// </summary>
        public int HighestSnr { get; set; }

        /// <summary>
        /// Average speed in WPM
        /// </summary>
        public double AverageSpeed { get; set; }

        /// <summary>
        /// Total of all reported speeds (for averaging)
        /// </summary>
        public double TotalSpeed { get; set; }

        /// <summary>
        /// Total of all reported frequencies (for averaging)
        /// </summary>
        public double TotalFrequency { get; set; }

        /// <summary>
        /// Count of spots aggregated into this entry
        /// </summary>
        public int SpotCount { get; set; }

        /// <summary>
        /// Timestamp of the most recent spot for this entry
        /// </summary>
        public DateTime LastSpottedUtc { get; set; }

        /// <summary>
        /// Unique key for this aggregated spot (callsign + center frequency)
        /// </summary>
        public string Key => $"{Callsign}|{CenterFrequencyKhz:F0}";

        /// <summary>
        /// Format the spot for display on the VFD (max 24 characters)
        /// Format: "14033.0 WO6W 24"
        /// </summary>
        public string ToDisplayString()
        {
            // Frequency to 100 Hz resolution (e.g., 14033.0)
            string freqStr = $"{FrequencyKhz:F1}";
            
            // Speed as integer
            string speedStr = $"{Math.Round(AverageSpeed)}";
            
            // Calculate available space for callsign
            // Format: "FREQ.F CALL SPD" where FREQ.F is up to 8 chars, SPD is up to 3 chars
            int usedChars = freqStr.Length + 1 + speedStr.Length + 1; // freq + space + speed + space
            int callMaxLen = Math.Max(1, 24 - usedChars);
            
            string callStr = Callsign.Length <= callMaxLen 
                ? Callsign 
                : Callsign.Substring(0, callMaxLen);
            
            return $"{freqStr} {callStr} {speedStr}";
        }

        public override string ToString()
        {
            return $"{Callsign} {FrequencyKhz:F1} kHz SNR:{HighestSnr} SPD:{AverageSpeed:F0} WPM ({SpotCount} spots)";
        }
    }

    /// <summary>
    /// Represents a raw RBN spot as received from telnet
    /// </summary>
    public class RawSpot
    {
        public string SpotterCallsign { get; set; } = string.Empty;
        public string SpottedCallsign { get; set; } = string.Empty;
        public double FrequencyKhz { get; set; }
        public int Snr { get; set; }
        public int SpeedWpm { get; set; }
        public string Mode { get; set; } = string.Empty;
        public DateTime TimestampUtc { get; set; }

        public override string ToString()
        {
            return $"{SpotterCallsign} spotted {SpottedCallsign} on {FrequencyKhz:F1} kHz, SNR:{Snr} dB, {SpeedWpm} WPM";
        }
    }
}
