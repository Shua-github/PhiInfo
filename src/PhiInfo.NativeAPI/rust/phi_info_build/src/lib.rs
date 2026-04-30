use std::{
    path::{Path, PathBuf},
    process::Command,
};

mod dotnet;

const DOTNET_VERSION: &str = "10";

pub fn target_to_rid(target: &str) -> String {
    let t = target.to_lowercase();

    let parts: Vec<&str> = t.split('-').collect();
    if parts.len() < 3 {
        return "unknown".to_string();
    }

    let arch = normalize_arch(parts[0]);

    let os_part = parts[2];

    let os = match os_part {
        "windows" => "win",
        "linux" => "linux",
        "android" => "linux-bionic",
        "darwin" => "osx",
        "ios" => "ios",
        "tvos" => "tvos",
        "freebsd" => "freebsd",
        _ => panic!("不支持"),
    };

    format!("{os}-{arch}")
}

fn normalize_arch(arch: &str) -> &str {
    match arch {
        "x86_64" | "amd64" => "x64",
        "i686" | "i386" => "x86",
        "aarch64" | "arm64" => "arm64",
        "armv7" | "arm" => "arm",
        "riscv64" => "riscv64",
        "loongarch64" => "loongarch64",
        _ => arch,
    }
}

fn dotnet_publish(csproj: &Path, rid: &str) {
    let output = Command::new("dotnet")
        .args([
            "publish",
            &csproj.to_string_lossy(),
            "-c",
            "Release",
            "-r",
            rid,
            "/p:NativeLib=Static",
        ])
        .output()
        .expect("dotnet publish failed");

    if !output.status.success() {
        panic!(
            "dotnet publish error:\n{}\n{}",
            String::from_utf8_lossy(&output.stdout),
            String::from_utf8_lossy(&output.stderr),
        );
    }
}

fn publish_output_dir(root: &Path, rid: &str) -> PathBuf {
    root.join("bin")
        .join("Release")
        .join(format!("net{}.0", DOTNET_VERSION))
        .join(rid)
        .join("publish")
}

fn watch_sources(root: &Path) {
    fn walk(dir: &Path) {
        if let Ok(entries) = std::fs::read_dir(dir) {
            for e in entries.flatten() {
                let path = e.path();

                if path.is_dir() {
                    walk(&path);
                } else if let Some(ext) = path.extension()
                    && (ext == "cs" || ext == "csproj")
                {
                    println!("cargo:rerun-if-changed={}", path.display());
                }
            }
        }
    }

    walk(root);
}

fn nuget_native_path(home: &Path, rid: &str, version: &str) -> PathBuf {
    home.join(".nuget")
        .join("packages")
        .join(format!("microsoft.netcore.app.runtime.nativeaot.{rid}"))
        .join(version)
        .join("runtimes")
        .join(rid)
        .join("native")
}

fn link_args(rid: &str) {
    if !rid.starts_with("win") {
        println!("cargo:rustc-link-arg=-Wl,-z,nostart-stop-gc");
    }   
}

fn find_runtime_version(home: &Path, rid: &str, major: &str) -> Option<String> {
    let base = home
        .join(".nuget")
        .join("packages")
        .join(format!("microsoft.netcore.app.runtime.nativeaot.{rid}"));

    let prefix = format!("{major}.");
    let mut versions = vec![];

    let entries = std::fs::read_dir(base).ok()?;

    for e in entries.flatten() {
        let name = e.file_name().to_string_lossy().to_string();

        if name.starts_with(&prefix) {
            versions.push(name);
        }
    }

    versions.sort_by(|a, b| compare_versions(b, a));
    versions.first().cloned()
}

fn compare_versions(a: &str, b: &str) -> std::cmp::Ordering {
    let pa: Vec<u32> = a.split('.').filter_map(|x| x.parse().ok()).collect();
    let pb: Vec<u32> = b.split('.').filter_map(|x| x.parse().ok()).collect();

    for (x, y) in pa.iter().zip(pb.iter()) {
        match x.cmp(y) {
            std::cmp::Ordering::Equal => continue,
            o => return o,
        }
    }

    pa.len().cmp(&pb.len())
}

pub fn setup(csproj_path: PathBuf, output_name: &str) {
    assert!(csproj_path.exists(), "csproj not found");

    let target_env = std::env::var("TARGET").expect("TARGET not set");
    let rid = target_to_rid(&target_env);

    let csharp_root = csproj_path
        .parent()
        .expect("invalid csproj path")
        .to_path_buf();

    watch_sources(&csharp_root);

    dotnet_publish(&csproj_path, &rid);

    let home = dirs::home_dir().expect("no home dir");
    let version = find_runtime_version(&home, &rid, DOTNET_VERSION).expect("runtime version not found");
    let nuget_native = nuget_native_path(&home, &rid, &version);
    let publish_dir = publish_output_dir(&csharp_root, &rid);

    let suffix = if rid.starts_with("win") {
        "lib"
    } else {
        "a"
    };
    println!("cargo:rustc-link-arg={}", publish_dir.join(format!("{}.{}", output_name,suffix)).display());

    let (system_libs, ilc_files) = dotnet::get_lib_list(&rid);
    link_args(&rid);
    for lib in &system_libs {
        println!("cargo:rustc-link-lib={}", lib);
    }
    for file in &ilc_files {
        println!("cargo:rustc-link-arg={}", nuget_native.join(file).display());
    }
}
