# RBN VFD Display Rust/Linux Port Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Port the RBN VFD Display from Windows/WPF/C# to Linux/Rust/egui.

**Architecture:** Async telnet client in tokio task communicates via mpsc channels with eframe UI. SpotStore aggregates spots with thread-safe access. VfdDisplay handles serial output with scrolling and random character mode.

**Tech Stack:** Rust, eframe/egui, tokio, serialport, configparser, directories, rand

---

## Task 1: Initialize Rust Project

**Files:**
- Create: `rbn-vfd-linux/Cargo.toml`
- Create: `rbn-vfd-linux/src/main.rs`

**Step 1: Create project directory**

```bash
mkdir -p rbn-vfd-linux/src
```

**Step 2: Create Cargo.toml**

```toml
[package]
name = "rbn-vfd-linux"
version = "0.1.0"
edition = "2021"
description = "RBN VFD Spot Display for Linux"
license = "MIT"

[dependencies]
eframe = "0.31"
tokio = { version = "1", features = ["full"] }
serialport = "4.7"
configparser = "3"
directories = "5"
rand = "0.8"
regex = "1"
```

**Step 3: Create minimal main.rs**

```rust
fn main() {
    println!("RBN VFD Display");
}
```

**Step 4: Verify build**

Run: `cd rbn-vfd-linux && cargo build`
Expected: Compiles successfully

**Step 5: Commit**

```bash
git add rbn-vfd-linux/
git commit -m "feat: initialize Rust project with dependencies"
```

---

## Task 2: Create Data Models

**Files:**
- Create: `rbn-vfd-linux/src/models/mod.rs`
- Create: `rbn-vfd-linux/src/models/spot.rs`
- Modify: `rbn-vfd-linux/src/main.rs`

**Step 1: Create models directory**

```bash
mkdir -p rbn-vfd-linux/src/models
```

**Step 2: Create spot.rs with RawSpot**

```rust
use std::time::Instant;

/// Raw spot data as received from RBN telnet
#[derive(Debug, Clone)]
pub struct RawSpot {
    pub spotter_callsign: String,
    pub spotted_callsign: String,
    pub frequency_khz: f64,
    pub snr: i32,
    pub speed_wpm: i32,
    pub mode: String,
    pub timestamp: Instant,
}

impl RawSpot {
    pub fn new(
        spotter_callsign: String,
        spotted_callsign: String,
        frequency_khz: f64,
        snr: i32,
        speed_wpm: i32,
        mode: String,
    ) -> Self {
        Self {
            spotter_callsign,
            spotted_callsign,
            frequency_khz,
            snr,
            speed_wpm,
            mode,
            timestamp: Instant::now(),
        }
    }
}
```

**Step 3: Add AggregatedSpot to spot.rs**

```rust
/// Aggregated spot data for display
#[derive(Debug, Clone)]
pub struct AggregatedSpot {
    pub callsign: String,
    pub frequency_khz: f64,
    pub center_frequency_khz: f64,
    pub highest_snr: i32,
    pub average_speed: f64,
    pub spot_count: u32,
    pub last_spotted: Instant,
}

impl AggregatedSpot {
    /// Create a new aggregated spot from a raw spot
    pub fn from_raw(raw: &RawSpot) -> Self {
        Self {
            callsign: raw.spotted_callsign.clone(),
            frequency_khz: raw.frequency_khz,
            center_frequency_khz: raw.frequency_khz.round(),
            highest_snr: raw.snr,
            average_speed: raw.speed_wpm as f64,
            spot_count: 1,
            last_spotted: Instant::now(),
        }
    }

    /// Update this spot with new data using incremental averaging
    pub fn update(&mut self, raw: &RawSpot) {
        self.spot_count += 1;
        self.average_speed += (raw.speed_wpm as f64 - self.average_speed) / self.spot_count as f64;
        self.frequency_khz += (raw.frequency_khz - self.frequency_khz) / self.spot_count as f64;
        if raw.snr > self.highest_snr {
            self.highest_snr = raw.snr;
        }
        self.last_spotted = Instant::now();
    }

    /// Generate the unique key for this spot (callsign + center frequency)
    pub fn key(&self) -> String {
        format!("{}|{:.0}", self.callsign, self.center_frequency_khz)
    }

    /// Format for VFD display (max 20 characters)
    /// Format: "14033.0 WO6W 24"
    pub fn to_display_string(&self) -> String {
        let freq_str = format!("{:.1}", self.frequency_khz);
        let speed_str = format!("{}", self.average_speed.round() as i32);

        // Calculate available space for callsign
        // Format: "FREQ.F CALL SPD"
        let used_chars = freq_str.len() + 1 + speed_str.len() + 1;
        let call_max_len = 20_usize.saturating_sub(used_chars);

        let call_str = if self.callsign.len() <= call_max_len {
            &self.callsign
        } else {
            &self.callsign[..call_max_len]
        };

        format!("{} {} {}", freq_str, call_str, speed_str)
    }
}
```

**Step 4: Create models/mod.rs**

```rust
mod spot;

pub use spot::{AggregatedSpot, RawSpot};
```

**Step 5: Update main.rs to include models**

```rust
mod models;

fn main() {
    println!("RBN VFD Display");
}
```

**Step 6: Verify build**

Run: `cd rbn-vfd-linux && cargo build`
Expected: Compiles successfully

**Step 7: Commit**

```bash
git add rbn-vfd-linux/src/models/
git commit -m "feat: add RawSpot and AggregatedSpot data models"
```

---

## Task 3: Create Configuration Module

**Files:**
- Create: `rbn-vfd-linux/src/config.rs`
- Modify: `rbn-vfd-linux/src/main.rs`

**Step 1: Create config.rs**

```rust
use configparser::ini::Ini;
use directories::ProjectDirs;
use std::path::PathBuf;

/// Application settings
#[derive(Debug, Clone)]
pub struct Config {
    pub callsign: String,
    pub serial_port: String,
    pub min_snr: i32,
    pub max_age_minutes: u32,
    pub scroll_interval_seconds: u32,
}

impl Default for Config {
    fn default() -> Self {
        Self {
            callsign: String::new(),
            serial_port: String::new(),
            min_snr: 10,
            max_age_minutes: 10,
            scroll_interval_seconds: 3,
        }
    }
}

impl Config {
    /// Get the config file path
    fn config_path() -> Option<PathBuf> {
        ProjectDirs::from("com", "w6jsv", "rbn-vfd-display")
            .map(|dirs| dirs.config_dir().join("settings.ini"))
    }

    /// Load config from file, or return defaults if file doesn't exist
    pub fn load() -> Self {
        let Some(path) = Self::config_path() else {
            return Self::default();
        };

        if !path.exists() {
            return Self::default();
        }

        let mut ini = Ini::new();
        if ini.load(&path).is_err() {
            return Self::default();
        }

        Self {
            callsign: ini.get("connection", "callsign").unwrap_or_default(),
            serial_port: ini.get("display", "serial_port").unwrap_or_default(),
            min_snr: ini
                .getint("filters", "min_snr")
                .ok()
                .flatten()
                .unwrap_or(10) as i32,
            max_age_minutes: ini
                .getint("filters", "max_age_minutes")
                .ok()
                .flatten()
                .unwrap_or(10) as u32,
            scroll_interval_seconds: ini
                .getint("filters", "scroll_interval_seconds")
                .ok()
                .flatten()
                .unwrap_or(3) as u32,
        }
    }

    /// Save config to file
    pub fn save(&self) -> Result<(), String> {
        let Some(path) = Self::config_path() else {
            return Err("Could not determine config path".to_string());
        };

        // Create config directory if it doesn't exist
        if let Some(parent) = path.parent() {
            std::fs::create_dir_all(parent)
                .map_err(|e| format!("Failed to create config directory: {}", e))?;
        }

        let mut ini = Ini::new();
        ini.set("connection", "callsign", Some(self.callsign.clone()));
        ini.set("display", "serial_port", Some(self.serial_port.clone()));
        ini.set("filters", "min_snr", Some(self.min_snr.to_string()));
        ini.set(
            "filters",
            "max_age_minutes",
            Some(self.max_age_minutes.to_string()),
        );
        ini.set(
            "filters",
            "scroll_interval_seconds",
            Some(self.scroll_interval_seconds.to_string()),
        );

        ini.write(&path)
            .map_err(|e| format!("Failed to write config: {}", e))
    }

    /// Reset to defaults
    pub fn reset_to_defaults(&mut self) {
        let defaults = Self::default();
        self.min_snr = defaults.min_snr;
        self.max_age_minutes = defaults.max_age_minutes;
        self.scroll_interval_seconds = defaults.scroll_interval_seconds;
        // Keep callsign and serial_port as-is
    }
}
```

**Step 2: Update main.rs**

```rust
mod config;
mod models;

fn main() {
    let config = config::Config::load();
    println!("Loaded config: {:?}", config);
}
```

**Step 3: Verify build**

Run: `cd rbn-vfd-linux && cargo build`
Expected: Compiles successfully

**Step 4: Commit**

```bash
git add rbn-vfd-linux/src/config.rs rbn-vfd-linux/src/main.rs
git commit -m "feat: add configuration module with XDG paths"
```

---

## Task 4: Create SpotStore Service

**Files:**
- Create: `rbn-vfd-linux/src/services/mod.rs`
- Create: `rbn-vfd-linux/src/services/spot_store.rs`
- Modify: `rbn-vfd-linux/src/main.rs`

**Step 1: Create services directory**

```bash
mkdir -p rbn-vfd-linux/src/services
```

**Step 2: Create spot_store.rs**

```rust
use crate::models::{AggregatedSpot, RawSpot};
use std::collections::HashMap;
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};

/// Thread-safe store for aggregated spots
#[derive(Clone)]
pub struct SpotStore {
    spots: Arc<Mutex<HashMap<String, AggregatedSpot>>>,
    min_snr: Arc<Mutex<i32>>,
    max_age: Arc<Mutex<Duration>>,
}

impl SpotStore {
    pub fn new(min_snr: i32, max_age_minutes: u32) -> Self {
        Self {
            spots: Arc::new(Mutex::new(HashMap::new())),
            min_snr: Arc::new(Mutex::new(min_snr)),
            max_age: Arc::new(Mutex::new(Duration::from_secs(max_age_minutes as u64 * 60))),
        }
    }

    /// Set minimum SNR filter
    pub fn set_min_snr(&self, snr: i32) {
        if let Ok(mut min) = self.min_snr.lock() {
            *min = snr;
        }
    }

    /// Set maximum age for spots
    pub fn set_max_age_minutes(&self, minutes: u32) {
        if let Ok(mut age) = self.max_age.lock() {
            *age = Duration::from_secs(minutes as u64 * 60);
        }
    }

    /// Add or update a spot
    pub fn add_spot(&self, raw: RawSpot) {
        // Check SNR filter
        let min_snr = self.min_snr.lock().map(|m| *m).unwrap_or(0);
        if raw.snr < min_snr {
            return;
        }

        let center_freq = raw.frequency_khz.round();
        let key = format!("{}|{:.0}", raw.spotted_callsign, center_freq);

        if let Ok(mut spots) = self.spots.lock() {
            if let Some(existing) = spots.get_mut(&key) {
                existing.update(&raw);
            } else {
                let spot = AggregatedSpot::from_raw(&raw);
                spots.insert(key, spot);
            }
        }
    }

    /// Remove spots older than max age
    pub fn purge_old_spots(&self) {
        let max_age = self.max_age.lock().map(|m| *m).unwrap_or(Duration::from_secs(600));
        let cutoff = Instant::now() - max_age;

        if let Ok(mut spots) = self.spots.lock() {
            spots.retain(|_, spot| spot.last_spotted >= cutoff);
        }
    }

    /// Get all spots sorted by frequency
    pub fn get_spots_by_frequency(&self) -> Vec<AggregatedSpot> {
        if let Ok(spots) = self.spots.lock() {
            let mut result: Vec<_> = spots.values().cloned().collect();
            result.sort_by(|a, b| a.frequency_khz.partial_cmp(&b.frequency_khz).unwrap());
            result
        } else {
            Vec::new()
        }
    }

    /// Get all spots sorted by recency
    pub fn get_spots_by_recency(&self) -> Vec<AggregatedSpot> {
        if let Ok(spots) = self.spots.lock() {
            let mut result: Vec<_> = spots.values().cloned().collect();
            result.sort_by(|a, b| b.last_spotted.cmp(&a.last_spotted));
            result
        } else {
            Vec::new()
        }
    }

    /// Get spot count
    pub fn count(&self) -> usize {
        self.spots.lock().map(|s| s.len()).unwrap_or(0)
    }

    /// Clear all spots
    pub fn clear(&self) {
        if let Ok(mut spots) = self.spots.lock() {
            spots.clear();
        }
    }
}
```

**Step 3: Create services/mod.rs**

```rust
mod spot_store;

pub use spot_store::SpotStore;
```

**Step 4: Update main.rs**

```rust
mod config;
mod models;
mod services;

fn main() {
    let config = config::Config::load();
    let store = services::SpotStore::new(config.min_snr, config.max_age_minutes);
    println!("SpotStore created with {} spots", store.count());
}
```

**Step 5: Verify build**

Run: `cd rbn-vfd-linux && cargo build`
Expected: Compiles successfully

**Step 6: Commit**

```bash
git add rbn-vfd-linux/src/services/
git commit -m "feat: add SpotStore service with filtering and purging"
```

---

## Task 5: Create RBN Telnet Client

**Files:**
- Create: `rbn-vfd-linux/src/services/rbn_client.rs`
- Modify: `rbn-vfd-linux/src/services/mod.rs`

**Step 1: Create rbn_client.rs**

```rust
use crate::models::RawSpot;
use regex::Regex;
use std::sync::Arc;
use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
use tokio::net::TcpStream;
use tokio::sync::mpsc;

const RBN_HOST: &str = "rbn.telegraphy.de";
const RBN_PORT: u16 = 7000;

/// Messages sent from the RBN client to the main app
#[derive(Debug, Clone)]
pub enum RbnMessage {
    Status(String),
    Spot(RawSpot),
    Disconnected,
}

/// Commands sent to the RBN client
#[derive(Debug)]
pub enum RbnCommand {
    Connect(String), // callsign
    Disconnect,
}

/// RBN client that runs in a tokio task
pub struct RbnClient {
    cmd_tx: mpsc::Sender<RbnCommand>,
    msg_rx: mpsc::Receiver<RbnMessage>,
}

impl RbnClient {
    /// Create a new RBN client and spawn the background task
    pub fn new() -> Self {
        let (cmd_tx, cmd_rx) = mpsc::channel(16);
        let (msg_tx, msg_rx) = mpsc::channel(256);

        tokio::spawn(rbn_task(cmd_rx, msg_tx));

        Self { cmd_tx, msg_rx }
    }

    /// Send a connect command
    pub async fn connect(&self, callsign: String) -> Result<(), String> {
        self.cmd_tx
            .send(RbnCommand::Connect(callsign))
            .await
            .map_err(|e| format!("Failed to send connect command: {}", e))
    }

    /// Send a disconnect command
    pub async fn disconnect(&self) -> Result<(), String> {
        self.cmd_tx
            .send(RbnCommand::Disconnect)
            .await
            .map_err(|e| format!("Failed to send disconnect command: {}", e))
    }

    /// Try to receive a message (non-blocking)
    pub fn try_recv(&mut self) -> Option<RbnMessage> {
        self.msg_rx.try_recv().ok()
    }
}

async fn rbn_task(mut cmd_rx: mpsc::Receiver<RbnCommand>, msg_tx: mpsc::Sender<RbnMessage>) {
    let spot_regex = Regex::new(
        r"DX de (\S+):\s+(\d+\.?\d*)\s+(\S+)\s+(\w+)\s+(\d+)\s+dB\s+(\d+)\s+WPM",
    )
    .unwrap();

    let mut stream: Option<TcpStream> = None;

    loop {
        tokio::select! {
            Some(cmd) = cmd_rx.recv() => {
                match cmd {
                    RbnCommand::Connect(callsign) => {
                        // Disconnect existing connection first
                        stream = None;

                        let _ = msg_tx.send(RbnMessage::Status(
                            format!("Connecting to {}:{}...", RBN_HOST, RBN_PORT)
                        )).await;

                        match TcpStream::connect((RBN_HOST, RBN_PORT)).await {
                            Ok(s) => {
                                let _ = msg_tx.send(RbnMessage::Status(
                                    "Connected, waiting for login prompt...".to_string()
                                )).await;
                                stream = Some(s);

                                // Handle login in a separate block
                                if let Some(ref mut s) = stream {
                                    if let Err(e) = handle_login(s, &callsign, &msg_tx).await {
                                        let _ = msg_tx.send(RbnMessage::Status(
                                            format!("Login failed: {}", e)
                                        )).await;
                                        stream = None;
                                    }
                                }
                            }
                            Err(e) => {
                                let _ = msg_tx.send(RbnMessage::Status(
                                    format!("Connection failed: {}", e)
                                )).await;
                            }
                        }
                    }
                    RbnCommand::Disconnect => {
                        stream = None;
                        let _ = msg_tx.send(RbnMessage::Status("Disconnected".to_string())).await;
                        let _ = msg_tx.send(RbnMessage::Disconnected).await;
                    }
                }
            }
            _ = async {
                if let Some(ref mut s) = stream {
                    let mut reader = BufReader::new(s);
                    let mut line = String::new();
                    match reader.read_line(&mut line).await {
                        Ok(0) => {
                            // Connection closed
                            let _ = msg_tx.send(RbnMessage::Status("Connection closed".to_string())).await;
                            let _ = msg_tx.send(RbnMessage::Disconnected).await;
                            return true; // Signal to clear stream
                        }
                        Ok(_) => {
                            if let Some(spot) = parse_spot_line(&line, &spot_regex) {
                                let _ = msg_tx.send(RbnMessage::Spot(spot)).await;
                            }
                        }
                        Err(e) => {
                            let _ = msg_tx.send(RbnMessage::Status(format!("Read error: {}", e))).await;
                            return true; // Signal to clear stream
                        }
                    }
                }
                false
            }, if stream.is_some() => {
                // Handle result - stream needs clearing handled above
            }
            else => {
                // No stream, just wait for commands
                tokio::time::sleep(tokio::time::Duration::from_millis(100)).await;
            }
        }
    }
}

async fn handle_login(
    stream: &mut TcpStream,
    callsign: &str,
    msg_tx: &mpsc::Sender<RbnMessage>,
) -> Result<(), String> {
    let mut reader = BufReader::new(&mut *stream);
    let mut line = String::new();

    // Read until we get the login prompt
    loop {
        line.clear();
        match reader.read_line(&mut line).await {
            Ok(0) => return Err("Connection closed".to_string()),
            Ok(_) => {
                if line.to_lowercase().contains("please enter your call") {
                    // Send callsign
                    stream
                        .write_all(format!("{}\r\n", callsign).as_bytes())
                        .await
                        .map_err(|e| format!("Failed to send callsign: {}", e))?;

                    let _ = msg_tx
                        .send(RbnMessage::Status(format!("Logged in as {}", callsign)))
                        .await;
                    return Ok(());
                }
            }
            Err(e) => return Err(format!("Read error: {}", e)),
        }
    }
}

fn parse_spot_line(line: &str, regex: &Regex) -> Option<RawSpot> {
    if !line.starts_with("DX de") {
        return None;
    }

    let caps = regex.captures(line)?;

    Some(RawSpot::new(
        caps.get(1)?.as_str().trim_end_matches(|c| c == '-' || c == '#' || c == ':').to_string(),
        caps.get(3)?.as_str().to_string(),
        caps.get(2)?.as_str().parse().ok()?,
        caps.get(5)?.as_str().parse().ok()?,
        caps.get(6)?.as_str().parse().ok()?,
        caps.get(4)?.as_str().to_string(),
    ))
}
```

**Step 2: Update services/mod.rs**

```rust
mod rbn_client;
mod spot_store;

pub use rbn_client::{RbnClient, RbnCommand, RbnMessage};
pub use spot_store::SpotStore;
```

**Step 3: Verify build**

Run: `cd rbn-vfd-linux && cargo build`
Expected: Compiles successfully

**Step 4: Commit**

```bash
git add rbn-vfd-linux/src/services/
git commit -m "feat: add RBN telnet client with async tokio task"
```

---

## Task 6: Create VFD Display Service

**Files:**
- Create: `rbn-vfd-linux/src/services/vfd_display.rs`
- Modify: `rbn-vfd-linux/src/services/mod.rs`

**Step 1: Create vfd_display.rs**

```rust
use crate::models::AggregatedSpot;
use rand::Rng;
use serialport::SerialPort;
use std::io::Write;
use std::time::{Duration, Instant};

const DISPLAY_WIDTH: usize = 20;
const DISPLAY_LINES: usize = 2;

// ESC/POS commands
const CLEAR_DISPLAY: &[u8] = &[0x0C];
const MOVE_LINE1: &[u8] = &[0x1B, 0x5B, 0x31, 0x3B, 0x31, 0x48]; // ESC [ 1;1 H
const MOVE_LINE2: &[u8] = &[0x1B, 0x5B, 0x32, 0x3B, 0x31, 0x48]; // ESC [ 2;1 H

/// VFD Display controller
pub struct VfdDisplay {
    port: Option<Box<dyn SerialPort>>,
    port_name: String,
    scroll_index: usize,
    scroll_interval: Duration,
    last_update: Instant,
    force_random_mode: bool,
    random_state: RandomCharState,
    current_lines: [String; 2],
}

struct RandomCharState {
    showing_char: bool,
    char_col: usize,
    char_row: usize,
    character: char,
}

impl Default for RandomCharState {
    fn default() -> Self {
        Self {
            showing_char: false,
            char_col: 0,
            char_row: 0,
            character: ' ',
        }
    }
}

impl VfdDisplay {
    pub fn new() -> Self {
        Self {
            port: None,
            port_name: String::new(),
            scroll_index: 0,
            scroll_interval: Duration::from_secs(3),
            last_update: Instant::now(),
            force_random_mode: false,
            random_state: RandomCharState::default(),
            current_lines: [String::new(), String::new()],
        }
    }

    /// Get available serial ports
    pub fn available_ports() -> Vec<String> {
        serialport::available_ports()
            .unwrap_or_default()
            .into_iter()
            .map(|p| p.port_name)
            .collect()
    }

    /// Open a serial port
    pub fn open(&mut self, port_name: &str) -> Result<(), String> {
        self.close();

        let port = serialport::new(port_name, 9600)
            .data_bits(serialport::DataBits::Eight)
            .parity(serialport::Parity::None)
            .stop_bits(serialport::StopBits::One)
            .timeout(Duration::from_millis(1000))
            .open()
            .map_err(|e| format!("Failed to open {}: {}", port_name, e))?;

        self.port = Some(port);
        self.port_name = port_name.to_string();
        self.clear();
        Ok(())
    }

    /// Close the serial port
    pub fn close(&mut self) {
        if self.port.is_some() {
            self.clear();
        }
        self.port = None;
        self.port_name.clear();
    }

    /// Check if port is open
    pub fn is_open(&self) -> bool {
        self.port.is_some()
    }

    /// Get current port name
    pub fn port_name(&self) -> &str {
        &self.port_name
    }

    /// Set scroll interval
    pub fn set_scroll_interval(&mut self, seconds: u32) {
        self.scroll_interval = Duration::from_secs(seconds as u64);
    }

    /// Set force random mode
    pub fn set_force_random_mode(&mut self, enabled: bool) {
        self.force_random_mode = enabled;
    }

    /// Clear the display
    pub fn clear(&mut self) {
        if let Some(ref mut port) = self.port {
            let _ = port.write_all(CLEAR_DISPLAY);
        }
        self.current_lines = [String::new(), String::new()];
    }

    /// Write text to a specific line (0 or 1)
    fn write_line(&mut self, line: usize, text: &str) {
        if let Some(ref mut port) = self.port {
            let move_cmd = if line == 0 { MOVE_LINE1 } else { MOVE_LINE2 };
            let _ = port.write_all(move_cmd);

            // Pad or truncate to exactly DISPLAY_WIDTH
            let padded: String = format!("{:width$}", text, width = DISPLAY_WIDTH)
                .chars()
                .take(DISPLAY_WIDTH)
                .collect();
            let _ = port.write_all(padded.as_bytes());
        }
        self.current_lines[line] = text.to_string();
    }

    /// Update display with spots (call periodically)
    pub fn update(&mut self, spots: &[AggregatedSpot]) {
        if !self.is_open() {
            return;
        }

        let now = Instant::now();
        if now.duration_since(self.last_update) < self.scroll_interval {
            return;
        }
        self.last_update = now;

        if self.force_random_mode || spots.is_empty() {
            self.update_random_mode();
            return;
        }

        match spots.len() {
            1 => {
                self.write_line(0, &spots[0].to_display_string());
                self.write_line(1, "");
            }
            2 => {
                self.write_line(0, &spots[0].to_display_string());
                self.write_line(1, &spots[1].to_display_string());
            }
            _ => {
                // Scroll through spots
                let idx1 = self.scroll_index % spots.len();
                let idx2 = (self.scroll_index + 1) % spots.len();
                self.write_line(0, &spots[idx1].to_display_string());
                self.write_line(1, &spots[idx2].to_display_string());
                self.scroll_index = (self.scroll_index + 1) % spots.len();
            }
        }
    }

    fn update_random_mode(&mut self) {
        let mut rng = rand::thread_rng();

        // 20% chance to show a character
        self.random_state.showing_char = rng.gen::<f32>() < 0.2;

        if self.random_state.showing_char {
            // Generate random character (A-Z, 0-9)
            self.random_state.character = if rng.gen::<bool>() {
                rng.gen_range(b'A'..=b'Z') as char
            } else {
                rng.gen_range(b'0'..=b'9') as char
            };
            self.random_state.char_col = rng.gen_range(0..DISPLAY_WIDTH);
            self.random_state.char_row = rng.gen_range(0..DISPLAY_LINES);

            // Create display with single character
            let mut line0 = " ".repeat(DISPLAY_WIDTH);
            let mut line1 = " ".repeat(DISPLAY_WIDTH);

            if self.random_state.char_row == 0 {
                line0.replace_range(
                    self.random_state.char_col..self.random_state.char_col + 1,
                    &self.random_state.character.to_string(),
                );
            } else {
                line1.replace_range(
                    self.random_state.char_col..self.random_state.char_col + 1,
                    &self.random_state.character.to_string(),
                );
            }

            self.write_line(0, &line0);
            self.write_line(1, &line1);
        } else {
            self.clear();
        }
    }

    /// Get current display lines for preview
    pub fn get_preview(&self) -> [String; 2] {
        self.current_lines.clone()
    }

    /// Get random mode state for preview
    pub fn is_in_random_mode(&self) -> bool {
        self.force_random_mode
    }
}
```

**Step 2: Update services/mod.rs**

```rust
mod rbn_client;
mod spot_store;
mod vfd_display;

pub use rbn_client::{RbnClient, RbnCommand, RbnMessage};
pub use spot_store::SpotStore;
pub use vfd_display::VfdDisplay;
```

**Step 3: Verify build**

Run: `cd rbn-vfd-linux && cargo build`
Expected: Compiles successfully

**Step 4: Commit**

```bash
git add rbn-vfd-linux/src/services/
git commit -m "feat: add VFD display service with scrolling and random mode"
```

---

## Task 7: Create Main Application with egui UI

**Files:**
- Create: `rbn-vfd-linux/src/app.rs`
- Modify: `rbn-vfd-linux/src/main.rs`

**Step 1: Create app.rs**

```rust
use crate::config::Config;
use crate::services::{RbnClient, RbnMessage, SpotStore, VfdDisplay};
use eframe::egui;
use std::time::{Duration, Instant};

pub struct RbnVfdApp {
    config: Config,
    spot_store: SpotStore,
    vfd_display: VfdDisplay,
    rbn_client: Option<RbnClient>,

    // UI state
    callsign_input: String,
    selected_port: String,
    available_ports: Vec<String>,
    status_message: String,
    is_connected: bool,

    // Timing
    last_purge: Instant,
    last_port_refresh: Instant,

    // Runtime handle for async operations
    runtime: tokio::runtime::Runtime,
}

impl RbnVfdApp {
    pub fn new(_cc: &eframe::CreationContext<'_>) -> Self {
        let config = Config::load();
        let spot_store = SpotStore::new(config.min_snr, config.max_age_minutes);

        let mut vfd_display = VfdDisplay::new();
        vfd_display.set_scroll_interval(config.scroll_interval_seconds);

        let available_ports = VfdDisplay::available_ports();
        let selected_port = if available_ports.contains(&config.serial_port) {
            config.serial_port.clone()
        } else {
            available_ports.first().cloned().unwrap_or_default()
        };

        let runtime = tokio::runtime::Runtime::new().expect("Failed to create tokio runtime");

        Self {
            callsign_input: config.callsign.clone(),
            config,
            spot_store,
            vfd_display,
            rbn_client: None,
            selected_port,
            available_ports,
            status_message: "Ready".to_string(),
            is_connected: false,
            last_purge: Instant::now(),
            last_port_refresh: Instant::now(),
            runtime,
        }
    }

    fn connect_rbn(&mut self) {
        if self.callsign_input.trim().is_empty() {
            self.status_message = "Please enter a callsign".to_string();
            return;
        }

        let callsign = self.callsign_input.trim().to_uppercase();
        self.config.callsign = callsign.clone();

        let client = self.runtime.block_on(async {
            let client = RbnClient::new();
            let _ = client.connect(callsign).await;
            client
        });

        self.rbn_client = Some(client);
        self.is_connected = true;
        self.status_message = "Connecting...".to_string();
    }

    fn disconnect_rbn(&mut self) {
        if let Some(ref client) = self.rbn_client {
            let _ = self.runtime.block_on(client.disconnect());
        }
        self.rbn_client = None;
        self.is_connected = false;
        self.status_message = "Disconnected".to_string();
    }

    fn open_vfd(&mut self) {
        if self.selected_port.is_empty() {
            self.status_message = "Please select a serial port".to_string();
            return;
        }

        match self.vfd_display.open(&self.selected_port) {
            Ok(()) => {
                self.config.serial_port = self.selected_port.clone();
                self.status_message = format!("VFD opened on {}", self.selected_port);
            }
            Err(e) => {
                self.status_message = e;
            }
        }
    }

    fn close_vfd(&mut self) {
        self.vfd_display.close();
        self.status_message = "VFD closed".to_string();
    }

    fn process_rbn_messages(&mut self) {
        if let Some(ref mut client) = self.rbn_client {
            while let Some(msg) = client.try_recv() {
                match msg {
                    RbnMessage::Status(s) => {
                        self.status_message = s;
                    }
                    RbnMessage::Spot(raw) => {
                        self.spot_store.add_spot(raw);
                    }
                    RbnMessage::Disconnected => {
                        self.is_connected = false;
                    }
                }
            }
        }
    }

    fn update_periodic(&mut self) {
        let now = Instant::now();

        // Purge old spots every 10 seconds
        if now.duration_since(self.last_purge) > Duration::from_secs(10) {
            self.spot_store.purge_old_spots();
            self.last_purge = now;
        }

        // Refresh available ports every 5 seconds
        if now.duration_since(self.last_port_refresh) > Duration::from_secs(5) {
            self.available_ports = VfdDisplay::available_ports();
            self.last_port_refresh = now;
        }

        // Update VFD display
        let spots = self.spot_store.get_spots_by_recency();
        self.vfd_display.update(&spots);
    }
}

impl eframe::App for RbnVfdApp {
    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        self.process_rbn_messages();
        self.update_periodic();

        // Request continuous updates
        ctx.request_repaint_after(Duration::from_millis(100));

        egui::CentralPanel::default().show(ctx, |ui| {
            ui.heading("RBN VFD Display");
            ui.separator();

            // Connection section
            ui.horizontal(|ui| {
                ui.label("Callsign:");
                ui.text_edit_singleline(&mut self.callsign_input);
                if ui.button("Connect").clicked() && !self.is_connected {
                    self.connect_rbn();
                }
                if ui.button("Disconnect").clicked() && self.is_connected {
                    self.disconnect_rbn();
                }
            });

            ui.horizontal(|ui| {
                ui.label("Serial Port:");
                egui::ComboBox::from_id_salt("port_select")
                    .selected_text(&self.selected_port)
                    .show_ui(ui, |ui| {
                        for port in &self.available_ports {
                            ui.selectable_value(&mut self.selected_port, port.clone(), port);
                        }
                    });
                if ui.button("Open").clicked() && !self.vfd_display.is_open() {
                    self.open_vfd();
                }
                if ui.button("Close").clicked() && self.vfd_display.is_open() {
                    self.close_vfd();
                }
            });

            ui.label(format!(
                "Status: {} | VFD: {}",
                self.status_message,
                if self.vfd_display.is_open() {
                    self.vfd_display.port_name()
                } else {
                    "not connected"
                }
            ));

            ui.separator();

            // Filters section
            ui.label("Filters:");

            ui.horizontal(|ui| {
                ui.label("Min SNR:");
                let mut snr = self.config.min_snr as f32;
                if ui.add(egui::Slider::new(&mut snr, 0.0..=50.0).suffix(" dB")).changed() {
                    self.config.min_snr = snr as i32;
                    self.spot_store.set_min_snr(self.config.min_snr);
                }
            });

            ui.horizontal(|ui| {
                ui.label("Max Age:");
                for minutes in [5, 10, 15, 30] {
                    if ui
                        .radio(self.config.max_age_minutes == minutes, format!("{} min", minutes))
                        .clicked()
                    {
                        self.config.max_age_minutes = minutes;
                        self.spot_store.set_max_age_minutes(minutes);
                    }
                }
            });

            ui.horizontal(|ui| {
                ui.label("Scroll:");
                for seconds in [1, 3, 5, 10, 30] {
                    if ui
                        .radio(
                            self.config.scroll_interval_seconds == seconds,
                            format!("{} sec", seconds),
                        )
                        .clicked()
                    {
                        self.config.scroll_interval_seconds = seconds;
                        self.vfd_display.set_scroll_interval(seconds);
                    }
                }
            });

            ui.separator();

            ui.horizontal(|ui| {
                let mut force_random = self.vfd_display.is_in_random_mode();
                if ui.checkbox(&mut force_random, "Force random character mode (testing)").changed() {
                    self.vfd_display.set_force_random_mode(force_random);
                }
                if ui.button("Restore Defaults").clicked() {
                    self.config.reset_to_defaults();
                    self.spot_store.set_min_snr(self.config.min_snr);
                    self.spot_store.set_max_age_minutes(self.config.max_age_minutes);
                    self.vfd_display.set_scroll_interval(self.config.scroll_interval_seconds);
                }
            });

            ui.separator();

            // VFD Preview
            ui.label("VFD Preview:");
            let preview = self.vfd_display.get_preview();
            egui::Frame::none()
                .fill(egui::Color32::from_rgb(0, 20, 0))
                .inner_margin(8.0)
                .show(ui, |ui| {
                    ui.style_mut().override_text_style = Some(egui::TextStyle::Monospace);
                    ui.colored_label(
                        egui::Color32::from_rgb(0, 255, 0),
                        format!("{:20}", preview[0]),
                    );
                    ui.colored_label(
                        egui::Color32::from_rgb(0, 255, 0),
                        format!("{:20}", preview[1]),
                    );
                });

            ui.separator();

            // Active spots list
            let spots = self.spot_store.get_spots_by_recency();
            ui.label(format!("Active Spots ({}):", spots.len()));

            egui::ScrollArea::vertical()
                .max_height(200.0)
                .show(ui, |ui| {
                    for spot in &spots {
                        ui.label(format!(
                            "{:8.1}  {:10}  {:3} dB  {:3} WPM  ({} {})",
                            spot.frequency_khz,
                            spot.callsign,
                            spot.highest_snr,
                            spot.average_speed.round() as i32,
                            spot.spot_count,
                            if spot.spot_count == 1 { "spot" } else { "spots" }
                        ));
                    }
                });
        });
    }

    fn on_exit(&mut self, _gl: Option<&eframe::glow::Context>) {
        // Save config on exit
        if let Err(e) = self.config.save() {
            eprintln!("Failed to save config: {}", e);
        }
        self.vfd_display.close();
    }
}
```

**Step 2: Update main.rs**

```rust
mod app;
mod config;
mod models;
mod services;

fn main() -> eframe::Result<()> {
    let options = eframe::NativeOptions {
        viewport: eframe::egui::ViewportBuilder::default()
            .with_inner_size([500.0, 600.0])
            .with_min_inner_size([400.0, 400.0]),
        ..Default::default()
    };

    eframe::run_native(
        "RBN VFD Display",
        options,
        Box::new(|cc| Ok(Box::new(app::RbnVfdApp::new(cc)))),
    )
}
```

**Step 3: Verify build**

Run: `cd rbn-vfd-linux && cargo build`
Expected: Compiles successfully

**Step 4: Commit**

```bash
git add rbn-vfd-linux/src/
git commit -m "feat: add egui application with full UI"
```

---

## Task 8: Fix RBN Client for Proper Async Operation

**Files:**
- Modify: `rbn-vfd-linux/src/services/rbn_client.rs`

The initial implementation has issues with the async select loop. This task refactors it to work correctly.

**Step 1: Replace rbn_client.rs with working implementation**

```rust
use crate::models::RawSpot;
use regex::Regex;
use tokio::io::{AsyncBufReadExt, AsyncWriteExt, BufReader};
use tokio::net::TcpStream;
use tokio::sync::mpsc;

const RBN_HOST: &str = "rbn.telegraphy.de";
const RBN_PORT: u16 = 7000;

/// Messages sent from the RBN client to the main app
#[derive(Debug, Clone)]
pub enum RbnMessage {
    Status(String),
    Spot(RawSpot),
    Disconnected,
}

/// Commands sent to the RBN client
#[derive(Debug)]
pub enum RbnCommand {
    Connect(String),
    Disconnect,
}

/// Handle to communicate with the RBN client task
pub struct RbnClient {
    cmd_tx: mpsc::Sender<RbnCommand>,
    msg_rx: mpsc::Receiver<RbnMessage>,
}

impl RbnClient {
    /// Create a new RBN client and spawn the background task
    pub fn new() -> Self {
        let (cmd_tx, cmd_rx) = mpsc::channel(16);
        let (msg_tx, msg_rx) = mpsc::channel(256);

        std::thread::spawn(move || {
            let rt = tokio::runtime::Builder::new_current_thread()
                .enable_all()
                .build()
                .expect("Failed to create tokio runtime");
            rt.block_on(rbn_task(cmd_rx, msg_tx));
        });

        Self { cmd_tx, msg_rx }
    }

    /// Send a connect command
    pub fn connect(&self, callsign: String) {
        let tx = self.cmd_tx.clone();
        let _ = tx.blocking_send(RbnCommand::Connect(callsign));
    }

    /// Send a disconnect command
    pub fn disconnect(&self) {
        let tx = self.cmd_tx.clone();
        let _ = tx.blocking_send(RbnCommand::Disconnect);
    }

    /// Try to receive a message (non-blocking)
    pub fn try_recv(&mut self) -> Option<RbnMessage> {
        self.msg_rx.try_recv().ok()
    }
}

async fn rbn_task(mut cmd_rx: mpsc::Receiver<RbnCommand>, msg_tx: mpsc::Sender<RbnMessage>) {
    let spot_regex = Regex::new(
        r"DX de (\S+):\s+(\d+\.?\d*)\s+(\S+)\s+(\w+)\s+(\d+)\s+dB\s+(\d+)\s+WPM",
    )
    .expect("Invalid regex");

    loop {
        // Wait for a connect command
        let callsign = loop {
            match cmd_rx.recv().await {
                Some(RbnCommand::Connect(cs)) => break cs,
                Some(RbnCommand::Disconnect) => continue,
                None => return, // Channel closed
            }
        };

        let _ = msg_tx
            .send(RbnMessage::Status(format!(
                "Connecting to {}:{}...",
                RBN_HOST, RBN_PORT
            )))
            .await;

        // Try to connect
        let stream = match TcpStream::connect((RBN_HOST, RBN_PORT)).await {
            Ok(s) => s,
            Err(e) => {
                let _ = msg_tx
                    .send(RbnMessage::Status(format!("Connection failed: {}", e)))
                    .await;
                let _ = msg_tx.send(RbnMessage::Disconnected).await;
                continue;
            }
        };

        let _ = msg_tx
            .send(RbnMessage::Status(
                "Connected, waiting for login prompt...".to_string(),
            ))
            .await;

        // Handle the connection
        handle_connection(stream, &callsign, &mut cmd_rx, &msg_tx, &spot_regex).await;

        let _ = msg_tx.send(RbnMessage::Disconnected).await;
    }
}

async fn handle_connection(
    stream: TcpStream,
    callsign: &str,
    cmd_rx: &mut mpsc::Receiver<RbnCommand>,
    msg_tx: &mpsc::Sender<RbnMessage>,
    spot_regex: &Regex,
) {
    let (reader, mut writer) = stream.into_split();
    let mut reader = BufReader::new(reader);
    let mut line = String::new();
    let mut logged_in = false;

    loop {
        tokio::select! {
            // Check for commands
            cmd = cmd_rx.recv() => {
                match cmd {
                    Some(RbnCommand::Disconnect) | None => {
                        let _ = msg_tx.send(RbnMessage::Status("Disconnected".to_string())).await;
                        return;
                    }
                    Some(RbnCommand::Connect(_)) => {
                        // Already connected, ignore
                    }
                }
            }

            // Read from stream
            result = reader.read_line(&mut line) => {
                match result {
                    Ok(0) => {
                        let _ = msg_tx.send(RbnMessage::Status("Connection closed by server".to_string())).await;
                        return;
                    }
                    Ok(_) => {
                        // Handle login
                        if !logged_in && line.to_lowercase().contains("please enter your call") {
                            if writer.write_all(format!("{}\r\n", callsign).as_bytes()).await.is_ok() {
                                let _ = msg_tx.send(RbnMessage::Status(format!("Logged in as {}", callsign))).await;
                                logged_in = true;
                            }
                        }

                        // Parse spots
                        if line.starts_with("DX de") {
                            if let Some(spot) = parse_spot_line(&line, spot_regex) {
                                let _ = msg_tx.send(RbnMessage::Spot(spot)).await;
                            }
                        }

                        line.clear();
                    }
                    Err(e) => {
                        let _ = msg_tx.send(RbnMessage::Status(format!("Read error: {}", e))).await;
                        return;
                    }
                }
            }
        }
    }
}

fn parse_spot_line(line: &str, regex: &Regex) -> Option<RawSpot> {
    let caps = regex.captures(line)?;

    Some(RawSpot::new(
        caps.get(1)?
            .as_str()
            .trim_end_matches(|c| c == '-' || c == '#' || c == ':')
            .to_string(),
        caps.get(3)?.as_str().to_string(),
        caps.get(2)?.as_str().parse().ok()?,
        caps.get(5)?.as_str().parse().ok()?,
        caps.get(6)?.as_str().parse().ok()?,
        caps.get(4)?.as_str().to_string(),
    ))
}
```

**Step 2: Update app.rs to use simplified client API**

In app.rs, change `connect_rbn` method:

```rust
fn connect_rbn(&mut self) {
    if self.callsign_input.trim().is_empty() {
        self.status_message = "Please enter a callsign".to_string();
        return;
    }

    let callsign = self.callsign_input.trim().to_uppercase();
    self.config.callsign = callsign.clone();

    let client = RbnClient::new();
    client.connect(callsign);

    self.rbn_client = Some(client);
    self.is_connected = true;
    self.status_message = "Connecting...".to_string();
}

fn disconnect_rbn(&mut self) {
    if let Some(ref client) = self.rbn_client {
        client.disconnect();
    }
    self.rbn_client = None;
    self.is_connected = false;
    self.status_message = "Disconnected".to_string();
}
```

Also remove the `runtime` field from `RbnVfdApp` and its initialization since we no longer need it.

**Step 3: Verify build**

Run: `cd rbn-vfd-linux && cargo build`
Expected: Compiles successfully

**Step 4: Commit**

```bash
git add rbn-vfd-linux/src/
git commit -m "fix: refactor RBN client for proper async operation"
```

---

## Task 9: Test and Refine Application

**Step 1: Run the application**

Run: `cd rbn-vfd-linux && cargo run`
Expected: Window opens, UI is displayed

**Step 2: Test without hardware**

- Enter a callsign and click Connect
- Verify status shows connection progress
- Verify spots appear in the list (if RBN is reachable)
- Test filter controls (SNR slider, radio buttons)
- Test Restore Defaults button
- Close and reopen - verify settings persist

**Step 3: Commit any fixes**

```bash
git add -A
git commit -m "fix: address issues found during testing"
```

---

## Task 10: Final Cleanup and Documentation

**Files:**
- Create: `rbn-vfd-linux/README.md`

**Step 1: Create README.md**

```markdown
# RBN VFD Display (Linux)

A Linux application that displays amateur radio spots from the Reverse Beacon Network on an ELO 20x2 VFD customer-facing display.

## Building

```bash
cargo build --release
```

## Running

```bash
cargo run --release
```

## Configuration

Settings are stored in `~/.config/rbn-vfd-display/settings.ini` and are automatically saved on exit.

## Features

- Connects to RBN telnet server (rbn.telegraphy.de:7000)
- Aggregates spots by callsign and frequency (within 1 kHz)
- Displays spots on ELO 20x2 VFD via serial port
- Configurable SNR filter, max age, and scroll interval
- Random character display mode when idle
- Settings persist between sessions

## License

MIT
```

**Step 2: Commit**

```bash
git add rbn-vfd-linux/README.md
git commit -m "docs: add README for Linux version"
```

**Step 3: Final commit with all changes**

```bash
git add -A
git commit -m "feat: complete RBN VFD Display Linux port"
```

---

## Summary

| Task | Description |
|------|-------------|
| 1 | Initialize Rust project with dependencies |
| 2 | Create RawSpot and AggregatedSpot data models |
| 3 | Create configuration module with XDG paths |
| 4 | Create SpotStore service |
| 5 | Create RBN telnet client |
| 6 | Create VFD display service |
| 7 | Create main application with egui UI |
| 8 | Fix RBN client async operation |
| 9 | Test and refine |
| 10 | Documentation and cleanup |
