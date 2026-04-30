use std::env;

#[cfg(feature = "node")]
fn node_setup() {
    napi_build::setup();
}

#[cfg(not(feature = "node"))]
fn node_setup() {}

fn main() {
    node_setup();

    let workspace_root = std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("..");

    let csproj = workspace_root.join("..").join("PhiInfo.NativeAPI.csproj");
    phi_info_build::setup(csproj, "PhiInfo.NativeAPI");
}
