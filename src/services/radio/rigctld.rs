//! rigctld (Hamlib) radio controller via TCP

use super::{RadioController, RadioError, RadioMode, RadioResult};
use std::io::{BufRead, BufReader, Write};
use std::net::TcpStream;
use std::time::Duration;

/// Controller for rigctld (Hamlib network daemon)
pub struct RigctldController {
    host: String,
    port: u16,
    stream: Option<TcpStream>,
}

impl RigctldController {
    pub fn new(host: String, port: u16) -> Self {
        Self {
            host,
            port,
            stream: None,
        }
    }

    fn send_command(&mut self, command: &str) -> RadioResult<String> {
        let stream = self.stream.as_mut().ok_or(RadioError::NotConnected)?;

        // Send command
        writeln!(stream, "{}", command).map_err(|e| RadioError::CommandFailed(e.to_string()))?;
        stream
            .flush()
            .map_err(|e| RadioError::CommandFailed(e.to_string()))?;

        // Read response
        let mut reader = BufReader::new(stream.try_clone().map_err(|e| {
            RadioError::CommandFailed(format!("Failed to clone stream: {}", e))
        })?);
        let mut response = String::new();
        reader
            .read_line(&mut response)
            .map_err(|e| RadioError::CommandFailed(e.to_string()))?;

        let response = response.trim().to_string();

        // Check for error response (rigctld returns "RPRT <error_code>" on failure)
        if response.starts_with("RPRT") {
            let parts: Vec<&str> = response.split_whitespace().collect();
            if parts.len() >= 2 {
                if let Ok(code) = parts[1].parse::<i32>() {
                    if code != 0 {
                        return Err(RadioError::CommandFailed(format!(
                            "rigctld error code: {}",
                            code
                        )));
                    }
                }
            }
        }

        Ok(response)
    }
}

impl RadioController for RigctldController {
    fn is_connected(&self) -> bool {
        self.stream.is_some()
    }

    fn connect(&mut self) -> RadioResult<()> {
        let addr = format!("{}:{}", self.host, self.port);
        let stream = TcpStream::connect_timeout(
            &addr.parse().map_err(|e| {
                RadioError::ConnectionFailed(format!("Invalid address: {}", e))
            })?,
            Duration::from_secs(3),
        )
        .map_err(|e| {
            RadioError::ConnectionFailed(format!(
                "Cannot connect to rigctld at {}. Is rigctld running? ({})",
                addr, e
            ))
        })?;

        stream
            .set_read_timeout(Some(Duration::from_secs(3)))
            .map_err(|e| RadioError::ConnectionFailed(e.to_string()))?;
        stream
            .set_write_timeout(Some(Duration::from_secs(3)))
            .map_err(|e| RadioError::ConnectionFailed(e.to_string()))?;

        self.stream = Some(stream);
        Ok(())
    }

    fn disconnect(&mut self) {
        self.stream = None;
    }

    fn tune(&mut self, frequency_khz: f64, mode: RadioMode) -> RadioResult<()> {
        if self.stream.is_none() {
            return Err(RadioError::NotConnected);
        }

        // Convert kHz to Hz for rigctld
        let frequency_hz = (frequency_khz * 1000.0) as u64;

        // Set frequency: F <freq_hz>
        self.send_command(&format!("F {}", frequency_hz))?;

        // Set mode: M <mode> <passband>
        // Using 0 for passband lets rigctld use the radio's default
        self.send_command(&format!("M {} 0", mode.to_rigctld_mode()))?;

        Ok(())
    }

    fn backend_name(&self) -> &'static str {
        "rigctld"
    }
}
