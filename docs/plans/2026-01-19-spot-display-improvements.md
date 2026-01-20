# Spot Display Improvements Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add age display with ring indicators, filter-at-display-time, and VFD preview without serial connection.

**Architecture:** SpotStore stores all spots and filters on retrieval. VfdDisplay updates state independent of serial connection. UI shows age with shrinking ring segment.

**Tech Stack:** Rust, eframe/egui, serialport

---

## Task 1: Add Age Methods to AggregatedSpot

**Files:**
- Modify: `src/models/spot.rs:39-103`

**Step 1: Add age_seconds method**

In `src/models/spot.rs`, add after line 75 (after the `key()` method):

```rust
/// Get age in seconds since last spotted
pub fn age_seconds(&self) -> u64 {
    self.last_spotted.elapsed().as_secs()
}
```

**Step 2: Add age_fraction method**

Add after `age_seconds()`:

```rust
/// Get age as fraction of max_age (0.0 = just spotted, 1.0 = expired)
pub fn age_fraction(&self, max_age: std::time::Duration) -> f32 {
    let age = self.last_spotted.elapsed();
    (age.as_secs_f32() / max_age.as_secs_f32()).min(1.0)
}
```

**Step 3: Verify build**

Run: `cargo build`
Expected: Compiles with no new errors

**Step 4: Commit**

```bash
git add src/models/spot.rs
git commit -m "feat(models): add age_seconds and age_fraction methods to AggregatedSpot"
```

---

## Task 2: Refactor SpotStore to Filter at Display Time

**Files:**
- Modify: `src/services/spot_store.rs`

**Step 1: Remove min_snr field and filtering from add_spot**

Replace the entire `SpotStore` struct and `new()` method (lines 7-21):

```rust
/// Thread-safe store for aggregated spots
#[derive(Clone)]
pub struct SpotStore {
    spots: Arc<Mutex<HashMap<String, AggregatedSpot>>>,
}

impl SpotStore {
    pub fn new() -> Self {
        Self {
            spots: Arc::new(Mutex::new(HashMap::new())),
        }
    }
```

**Step 2: Remove set_min_snr method**

Delete the `set_min_snr` method (lines 24-28).

**Step 3: Remove set_max_age_minutes method**

Delete the `set_max_age_minutes` method (lines 31-35).

**Step 4: Simplify add_spot to store all spots**

Replace the `add_spot` method:

```rust
/// Add or update a spot (stores all spots, filtering happens at retrieval)
pub fn add_spot(&self, raw: RawSpot) {
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
```

**Step 5: Update purge_old_spots to use fixed 30-minute cutoff**

Replace the `purge_old_spots` method:

```rust
/// Remove spots older than 30 minutes (hard limit for memory management)
pub fn purge_old_spots(&self) {
    let cutoff = Instant::now() - Duration::from_secs(30 * 60);

    if let Ok(mut spots) = self.spots.lock() {
        spots.retain(|_, spot| spot.last_spotted >= cutoff);
    }
}
```

**Step 6: Add get_filtered_spots method**

Add new method after `purge_old_spots`:

```rust
/// Get spots filtered by min_snr and max_age, sorted by frequency
pub fn get_filtered_spots(&self, min_snr: i32, max_age: Duration) -> Vec<AggregatedSpot> {
    let cutoff = Instant::now() - max_age;

    if let Ok(spots) = self.spots.lock() {
        let mut result: Vec<_> = spots
            .values()
            .filter(|spot| spot.highest_snr >= min_snr && spot.last_spotted >= cutoff)
            .cloned()
            .collect();
        result.sort_by(|a, b| a.frequency_khz.partial_cmp(&b.frequency_khz).unwrap());
        result
    } else {
        Vec::new()
    }
}
```

**Step 7: Update get_spots_by_frequency to not filter (used only for VFD)**

Keep existing implementation but update comment:

```rust
/// Get all spots sorted by frequency (no filtering, used internally)
pub fn get_spots_by_frequency(&self) -> Vec<AggregatedSpot> {
```

**Step 8: Verify build**

Run: `cargo build`
Expected: Errors in app.rs (expected - will fix in Task 4)

**Step 9: Commit**

```bash
git add src/services/spot_store.rs
git commit -m "feat(spot_store): filter at display time instead of insert time"
```

---

## Task 3: Refactor VfdDisplay to Decouple State from Serial

**Files:**
- Modify: `src/services/vfd_display.rs`

**Step 1: Rename update to update_state, make it always run**

Find the `update` method (around line 172) and split it. First, rename and modify:

```rust
/// Update display state with spots (always runs, even without serial connection)
pub fn update(&mut self, spots: &[AggregatedSpot]) {
    // Random mode updates on its own timing (duty cycle within each second)
    if self.force_random_mode || spots.is_empty() {
        self.update_random_mode_state();
        self.write_to_port();
        return;
    }

    // Spot display uses scroll interval
    let now = Instant::now();
    if now.duration_since(self.last_update) < self.scroll_interval {
        return;
    }
    self.last_update = now;

    // Update current_lines based on spots
    match spots.len() {
        1 => {
            self.current_lines[0] = spots[0].to_display_string();
            self.current_lines[1] = String::new();
        }
        2 => {
            self.current_lines[0] = spots[0].to_display_string();
            self.current_lines[1] = spots[1].to_display_string();
        }
        _ => {
            // Scroll through spots
            let idx1 = self.scroll_index % spots.len();
            let idx2 = (self.scroll_index + 1) % spots.len();
            self.current_lines[0] = spots[idx1].to_display_string();
            self.current_lines[1] = spots[idx2].to_display_string();
            self.scroll_index = (self.scroll_index + 1) % spots.len();
        }
    }

    self.write_to_port();
}
```

**Step 2: Add write_to_port helper method**

Add after the `update` method:

```rust
/// Write current_lines to serial port if connected
fn write_to_port(&mut self) {
    if let Some(ref mut port) = self.port {
        // Clear and home cursor
        let _ = port.write_all(CLEAR_DISPLAY);

        // Write line 1 (exactly 20 chars)
        let padded1 = Self::format_line(&self.current_lines[0]);
        let _ = port.write_all(padded1.as_bytes());

        // Write line 2 (exactly 20 chars)
        let padded2 = Self::format_line(&self.current_lines[1]);
        let _ = port.write_all(padded2.as_bytes());
    }
}
```

**Step 3: Rename update_random_mode to update_random_mode_state and decouple**

Replace the `update_random_mode` method:

```rust
fn update_random_mode_state(&mut self) {
    // Get current time info
    let now = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default();
    let current_second = now.as_secs();
    let ms_in_second = (now.as_millis() % 1000) as u32;

    // Calculate threshold: e.g., 20% duty cycle = first 200ms of each second
    let threshold_ms = self.random_char_percent * 10;
    let should_show = ms_in_second < threshold_ms && self.random_char_percent > 0;

    // Check if this is a new second - generate new random char and position
    if current_second != self.random_state.last_second {
        self.random_state.last_second = current_second;
        let mut rng = rand::thread_rng();
        // Generate random character (A-Z, 0-9)
        self.random_state.character = if rng.gen::<bool>() {
            rng.gen_range(b'A'..=b'Z') as char
        } else {
            rng.gen_range(b'0'..=b'9') as char
        };
        self.random_state.char_col = rng.gen_range(0..DISPLAY_WIDTH);
        self.random_state.char_row = rng.gen_range(0..DISPLAY_LINES);
    }

    // Update current_lines based on random state
    if should_show && !self.random_state.showing_char {
        self.random_state.showing_char = true;

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

        self.current_lines[0] = line0;
        self.current_lines[1] = line1;
    } else if !should_show && self.random_state.showing_char {
        self.random_state.showing_char = false;
        self.current_lines[0] = String::new();
        self.current_lines[1] = String::new();
    }
}
```

**Step 4: Remove old write_display and write_line methods**

Delete the `write_display` method (lines 145-159) and `write_line` method (lines 162-169) as they're no longer needed.

**Step 5: Update clear method**

Replace the `clear` method:

```rust
/// Clear the display
pub fn clear(&mut self) {
    self.current_lines = [String::new(), String::new()];
    if let Some(ref mut port) = self.port {
        let _ = port.write_all(CLEAR_DISPLAY);
    }
}
```

**Step 6: Verify build**

Run: `cargo build`
Expected: Still errors in app.rs (fixing next)

**Step 7: Commit**

```bash
git add src/services/vfd_display.rs
git commit -m "feat(vfd_display): decouple preview state from serial connection"
```

---

## Task 4: Update App UI

**Files:**
- Modify: `src/app.rs`

**Step 1: Update SpotStore instantiation**

Find line 30 and change:

```rust
let spot_store = SpotStore::new(config.min_snr, config.max_age_minutes);
```

To:

```rust
let spot_store = SpotStore::new();
```

**Step 2: Remove set_min_snr calls**

Find and delete line 271:

```rust
self.spot_store.set_min_snr(snr);
```

And delete line 343:

```rust
self.spot_store.set_min_snr(self.config.min_snr);
```

**Step 3: Remove set_max_age_minutes calls**

Find and delete line 287:

```rust
self.spot_store.set_max_age_minutes(age);
```

And delete line 344:

```rust
self.spot_store.set_max_age_minutes(self.config.max_age_minutes);
```

**Step 4: Add 1-minute option to max age**

Find line 280 and change:

```rust
let age_options = [5u32, 10, 15, 30];
```

To:

```rust
let age_options = [1u32, 5, 10, 15, 30];
```

**Step 5: Update VFD display to use filtered spots**

Find line 171 in `update_periodic`:

```rust
let spots = self.spot_store.get_spots_by_frequency();
```

Change to:

```rust
let max_age = std::time::Duration::from_secs(self.config.max_age_minutes as u64 * 60);
let spots = self.spot_store.get_filtered_spots(self.config.min_snr, max_age);
```

**Step 6: Update active spots list to use filtered spots**

Find line 440:

```rust
let spots = self.spot_store.get_spots_by_frequency();
```

Change to:

```rust
let max_age = std::time::Duration::from_secs(self.config.max_age_minutes as u64 * 60);
let spots = self.spot_store.get_filtered_spots(self.config.min_snr, max_age);
```

**Step 7: Add Duration import at top of file**

Add to the imports at line 4:

```rust
use std::time::{Duration, Instant};
```

**Step 8: Verify build**

Run: `cargo build`
Expected: Compiles successfully

**Step 9: Commit**

```bash
git add src/app.rs
git commit -m "feat(app): use filter-at-display-time and add 1-minute max age option"
```

---

## Task 5: Add Age Column with Ring Segment to Active Spots Table

**Files:**
- Modify: `src/app.rs`

**Step 1: Add Age header to table**

Find the header section (around line 445-470). After the "#" column header, add:

```rust
ui.label(
    egui::RichText::new(format!("{:>6}", "Age"))
        .monospace()
        .strong(),
);
```

**Step 2: Create helper function for ring drawing**

Add this function before the `impl eframe::App for RbnVfdApp` block (around line 176):

```rust
/// Draw an age ring indicator
fn draw_age_ring(ui: &mut egui::Ui, fraction: f32) {
    let size = 16.0;
    let (response, painter) = ui.allocate_painter(egui::Vec2::splat(size), egui::Sense::hover());
    let center = response.rect.center();
    let radius = size / 2.0 - 2.0;

    // Ring color - static green
    let color = egui::Color32::from_rgb(0, 200, 0);

    // Draw background circle (dim)
    painter.circle_stroke(center, radius, egui::Stroke::new(2.0, egui::Color32::from_rgb(40, 40, 40)));

    // Draw arc for remaining time (1.0 - fraction = remaining)
    let remaining = 1.0 - fraction;
    if remaining > 0.001 {
        // Arc from 12 o'clock (-PI/2), sweeping counter-clockwise
        let start_angle = -std::f32::consts::FRAC_PI_2;
        let sweep = remaining * std::f32::consts::TAU;

        // Draw arc as series of line segments
        let segments = 32;
        let points: Vec<egui::Pos2> = (0..=segments)
            .map(|i| {
                let t = i as f32 / segments as f32;
                let angle = start_angle - t * sweep; // Counter-clockwise
                egui::Pos2::new(
                    center.x + radius * angle.cos(),
                    center.y + radius * angle.sin(),
                )
            })
            .collect();

        for i in 0..points.len() - 1 {
            painter.line_segment([points[i], points[i + 1]], egui::Stroke::new(2.0, color));
        }
    }
}
```

**Step 3: Add age display in spot rows**

Find the spot row rendering loop (around line 475-500). After the spot_count label, add:

```rust
// Age display
let age_secs = spot.age_seconds();
let age_text = if age_secs < 60 {
    format!("{:>3}s", age_secs)
} else {
    format!("{:>3}m", age_secs / 60)
};
ui.label(
    egui::RichText::new(age_text)
        .monospace(),
);

// Ring indicator
let max_age = Duration::from_secs(self.config.max_age_minutes as u64 * 60);
let fraction = spot.age_fraction(max_age);
draw_age_ring(ui, fraction);
```

**Step 4: Verify build**

Run: `cargo build`
Expected: Compiles successfully

**Step 5: Run the application to verify visually**

Run: `cargo run --release`
Expected: Application launches, age column visible with ring indicators

**Step 6: Commit**

```bash
git add src/app.rs
git commit -m "feat(app): add age column with ring segment indicator to active spots"
```

---

## Task 6: Final Verification and Cleanup

**Step 1: Run clippy**

Run: `cargo clippy`
Expected: No errors, warnings are acceptable

**Step 2: Build release**

Run: `cargo build --release`
Expected: Compiles successfully

**Step 3: Manual test checklist**

- [ ] Launch application without VFD connected
- [ ] Verify VFD Preview shows content when spots arrive (without serial port open)
- [ ] Change Min SNR filter - spots should immediately update
- [ ] Change Max Age to 1 minute - verify spots filter correctly
- [ ] Verify age column shows seconds/minutes
- [ ] Verify ring shrinks over time as spots age
- [ ] Open VFD serial port - verify display mirrors preview

**Step 4: Final commit (if any fixes needed)**

```bash
git add -A
git commit -m "fix: address clippy warnings and polish"
```

---

## Summary of Changes

| File | Changes |
|------|---------|
| `src/models/spot.rs` | Add `age_seconds()` and `age_fraction()` methods |
| `src/services/spot_store.rs` | Remove min_snr filtering, add `get_filtered_spots()` |
| `src/services/vfd_display.rs` | Decouple state updates from serial writes |
| `src/app.rs` | Add 1-min option, filtered display, age column with ring |
