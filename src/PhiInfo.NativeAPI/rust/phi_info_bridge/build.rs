use std::env;

fn main() {
    if env::var("CARGO_FEATURE_NODE").is_ok() {
        napi_build::setup();
    }

    let workspace_root = std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("..");

    let csproj = workspace_root.join("..").join("PhiInfo.NativeAPI.csproj");
    phi_info_build::setup(csproj, "PhiInfo.NativeAPI");
}
