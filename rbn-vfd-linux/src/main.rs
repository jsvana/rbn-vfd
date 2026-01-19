mod config;
mod models;

fn main() {
    let config = config::Config::load();
    println!("Loaded config: {:?}", config);
}
