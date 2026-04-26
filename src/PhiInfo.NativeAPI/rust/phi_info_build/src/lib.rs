use std::{
    path::{Path, PathBuf},
    process::Command,
};

const DOTNET_VERSION: &str = "10";

fn target_to_rid(target: &str) -> &'static str {
    match target {
        "x86_64-unknown-linux-gnu" => "linux-x64",
        "aarch64-unknown-linux-gnu" => "linux-arm64",

        "x86_64-pc-windows-msvc" => "win-x64",
        "aarch64-pc-windows-msvc" => "win-arm64",

        "aarch64-linux-android" => "linux-bionic-arm64",
        "x86_64-linux-android" => "linux-bionic-x64",

        _ => panic!("unsupported target: {}", target),
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

fn link_linux(nuget_native: &Path) {
    println!("cargo:rustc-link-arg=-Wl,-z,nostart-stop-gc");

    let bootstrapper = nuget_native.join("libbootstrapperdll.o");
    println!("cargo:rustc-link-arg={}", bootstrapper.display());

    let native_libs = [
        "Runtime.WorkstationGC",
        "System.Native",
        "System.Globalization.Native",
        "System.IO.Compression.Native",
        "System.Net.Security.Native",
        "System.Security.Cryptography.Native.OpenSsl",
    ];
    for lib in native_libs {
        println!("cargo:rustc-link-lib=static={}", lib);
    }

    println!("cargo:rustc-link-lib=static=eventpipe-disabled");
    println!("cargo:rustc-link-lib=static=Runtime.VxsortDisabled");
    println!("cargo:rustc-link-lib=static=standalonegc-disabled");
    println!("cargo:rustc-link-lib=static=aotminipal");

    println!("cargo:rustc-link-lib=static=brotlienc");
    println!("cargo:rustc-link-lib=static=brotlidec");
    println!("cargo:rustc-link-lib=static=brotlicommon");
    println!("cargo:rustc-link-lib=static=z");

    println!("cargo:rustc-link-lib=static=stdc++compat");
    println!("cargo:rustc-link-lib=dl");
    println!("cargo:rustc-link-lib=rt");
    println!("cargo:rustc-link-lib=pthread");
    println!("cargo:rustc-link-lib=m");
    println!("cargo:rustc-link-lib=ssl");
    println!("cargo:rustc-link-lib=crypto");
}

fn link_windows() {
    println!("cargo:rustc-link-lib=static=Runtime.WorkstationGC");

    let sys_libs = [
        "bcrypt", "ole32", "advapi32", "crypt32", "ncrypt", "iphlpapi", "ws2_32",
    ];
    for l in sys_libs {
        println!("cargo:rustc-link-lib={}", l);
    }

    println!("cargo:rustc-link-arg=bootstrapperdll.obj");
    println!("cargo:rustc-link-arg=dllmain.obj");

    let extra = [
        "System.IO.Compression.Native.Aot",
        "System.Globalization.Native.Aot",
        "zlibstatic",
        "eventpipe-disabled",
        "Runtime.VxsortDisabled",
        "standalonegc-disabled",
        "aotminipal",
    ];
    for l in extra {
        println!("cargo:rustc-link-lib=static={}", l);
    }
}

fn link_android(nuget_native: &Path) {
    println!("cargo:rustc-link-arg=-Wl,--allow-multiple-definition");
    println!(
        "cargo:rustc-link-arg={}",
        nuget_native.join("libbootstrapperdll.o").display()
    );

    let libs = [
        "System.Native",
        "System.IO.Compression.Native",
        "System.Security.Cryptography.Native.OpenSsl",
        "Runtime.WorkstationGC",
        "eventpipe-disabled",
        "standalonegc-disabled",
        "aotminipal",
        "brotlicommon",
        "brotlienc",
        "brotlidec",
        "stdc++compat",
    ];

    for l in libs {
        println!("cargo:rustc-link-lib=static={}", l);
    }

    println!("cargo:rustc-link-lib=dl");
    println!("cargo:rustc-link-lib=log");
    println!("cargo:rustc-link-lib=z");
    println!("cargo:rustc-link-lib=m");
}

fn link_platform(rid: &str, nuget_native: &Path) {
    if rid.starts_with("win-") {
        link_windows();
    } else if rid.starts_with("linux-bionic-") {
        link_android(nuget_native);
    } else if rid.starts_with("linux-") {
        link_linux(nuget_native);
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

    dotnet_publish(&csproj_path, rid);

    let home = dirs::home_dir().expect("no home dir");
    let version =
        find_runtime_version(&home, rid, DOTNET_VERSION).expect("runtime version not found");
    let nuget_native = nuget_native_path(&home, rid, &version);
    let publish_dir = publish_output_dir(&csharp_root, rid);

    println!("cargo:rustc-link-search={}", publish_dir.display());
    println!("cargo:rustc-link-search={}", nuget_native.display());

    println!("cargo:rustc-link-lib=static={}", output_name);

    link_platform(rid, &nuget_native);
}
