# Spot Display Improvements Design

Date: 2026-01-19

## Overview

Five improvements to the RBN VFD Display application:

1. Add 1-minute max age option
2. Update active spots immediately when filters change
3. VFD preview works without physical display connection
4. Display age on active spots with ring segment indicator

## Architecture Changes

### Data Flow (Before)
```
RbnClient → SpotStore (filters on insert) → VfdDisplay (needs port open)
```

### Data Flow (After)
```
RbnClient → SpotStore (stores all) → App (filters on read) → VfdDisplay (always updates state)
```

## Component Changes

### SpotStore

**Remove SNR filtering from `add_spot()`** - Store all incoming spots regardless of SNR. Remove `min_snr` field from SpotStore.

**New retrieval method:**
```rust
pub fn get_filtered_spots(&self, min_snr: i32, max_age: Duration) -> Vec<AggregatedSpot>
```
Filters by both SNR and age at read time, sorts by frequency.

**Purging** - Use fixed 30-minute cutoff to prevent unbounded memory growth. Display filtering uses user's configured max_age (1-30 minutes).

### AggregatedSpot

**New age methods:**
```rust
pub fn age_seconds(&self) -> u64  // seconds since last_spotted
pub fn age_fraction(&self, max_age: Duration) -> f32  // 0.0 (new) to 1.0 (expired)
```

### VfdDisplay

**Decouple state from serial writes:**

```rust
fn update_state(&mut self, spots: &[AggregatedSpot])  // Always runs, updates current_lines
fn write_to_port(&mut self)  // Only if port is open
```

The public `update()` calls both. Preview reflects state whether or not physical VFD is connected.

### UI (app.rs)

**Max age options:** Change from `[5, 10, 15, 30]` to `[1, 5, 10, 15, 30]` minutes.

**Active spots table - new Age column:**
- Numeric age: `"XXs"` for seconds, `"Xm"` for minutes
- Ring segment: ~8px radius circle drawn with `painter.arc()`
  - Full ring = just spotted (0% age consumed)
  - Empty ring = about to expire (100% age consumed)
  - Arc sweeps counter-clockwise from 12 o'clock
  - Static green color

**Filter responsiveness:** Spots list updates immediately when min_snr or max_age changes (filtering at display time).

## Files to Modify

1. `src/models/spot.rs` - Add `age_seconds()` and `age_fraction()` methods
2. `src/services/spot_store.rs` - Remove min_snr filtering, add `get_filtered_spots()`
3. `src/services/vfd_display.rs` - Decouple state updates from serial writes
4. `src/app.rs` - Add 1-min option, update spots retrieval, add age column with ring
5. `src/config.rs` - No changes needed (default max_age stays at 10)
