//! No-op radio controller for when radio control is disabled

use super::{RadioController, RadioError, RadioMode, RadioResult};

/// A no-op controller that does nothing (used when radio is disabled)
pub struct NoOpController;

impl NoOpController {
    pub fn new() -> Self {
        Self
    }
}

impl Default for NoOpController {
    fn default() -> Self {
        Self::new()
    }
}

impl RadioController for NoOpController {
    fn is_connected(&self) -> bool {
        false
    }

    fn connect(&mut self) -> RadioResult<()> {
        Err(RadioError::NotConfigured)
    }

    fn disconnect(&mut self) {
        // No-op
    }

    fn tune(&mut self, _frequency_khz: f64, _mode: RadioMode) -> RadioResult<()> {
        Err(RadioError::NotConfigured)
    }

    fn backend_name(&self) -> &'static str {
        "None"
    }
}
