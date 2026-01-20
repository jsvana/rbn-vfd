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
