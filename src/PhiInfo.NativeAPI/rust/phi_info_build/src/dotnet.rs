// https://github.com/dotnet/runtime/tree/main/src/coreclr/nativeaot/BuildIntegration

#[derive(Debug)]
struct RidInfo {
    target_os: String,
    arch: String,
    libc_flavor: String,
    is_apple: bool,
    is_ios_like: bool,
}

fn parse_rid(rid: &str) -> RidInfo {
    let rid_lower = rid.to_lowercase();
    let parts: Vec<&str> = rid_lower.split('-').collect();
    let arch = parts.last().unwrap().to_string();

    let original_target_os = parts[..parts.len() - 1].join("-");

    let mut target_os = original_target_os.clone();
    let mut libc_flavor = String::new();

    if target_os.starts_with("win") {
        target_os = "win".to_string();
    } else if target_os.starts_with("linux-") {
        libc_flavor = target_os["linux-".len()..].to_string();
        target_os = "linux".to_string();
    } else if target_os == "android" {
        libc_flavor = "bionic".to_string();
    }

    let is_apple = target_os == "osx"
        || target_os.starts_with("ios")
        || target_os.starts_with("tvos")
        || target_os == "maccatalyst";
    let is_ios_like =
        target_os == "maccatalyst" || target_os.starts_with("ios") || target_os.starts_with("tvos");

    RidInfo {
        target_os,
        arch,
        libc_flavor,
        is_apple,
        is_ios_like,
    }
}

fn link_args(info: &RidInfo) -> Vec<String> {
    let mut args = Vec::new();

    if info.is_apple {
        args.push("-Wl,-dead_strip".to_string());

        fn fw(name: &str, args: &mut Vec<String>) {
            args.push("-framework".to_string());
            args.push(name.to_string());
        }

        fw("CoreFoundation", &mut args);
        fw("Foundation", &mut args);
        fw("Security", &mut args);
        fw("Network", &mut args);
        fw("CryptoKit", &mut args);

        if !info.target_os.starts_with("tvos") {
            fw("GSS", &mut args);
        }
    }

    if info.target_os == "linux" {
        args.push("-Wl,--gc-sections".to_string());
    }

    args
}

pub fn get_lib_list(rid: &str) -> (Vec<String>, Vec<String>, Vec<String>) {
    let info = parse_rid(rid);
    let system = system_libs(&info);
    let ilc = ilc_libs(&info);
    let link_args = link_args(&info);
    (system, ilc, link_args)
}

fn system_libs(info: &RidInfo) -> Vec<String> {
    if info.target_os == "win" {
        windows_system_libs()
    } else {
        unix_system_libs(info)
    }
}

fn windows_system_libs() -> Vec<String> {
    vec![
        "advapi32", "bcrypt", "crypt32", "iphlpapi", "kernel32", "mswsock", "ncrypt", "normaliz",
        "ntdll", "ole32", "oleaut32", "secur32", "user32", "version", "ws2_32",
    ]
    .into_iter()
    .map(String::from)
    .collect()
}

fn unix_system_libs(info: &RidInfo) -> Vec<String> {
    let mut libs = Vec::new();

    libs.push("dl".to_string());
    libs.push("m".to_string());
    if info.libc_flavor != "bionic" {
        libs.push("pthread".to_string());
    }

    if !info.is_apple && info.libc_flavor != "bionic" {
        libs.push("rt".to_string());
    }

    if info.libc_flavor == "bionic" {
        libs.push("log".to_string());
    }

    if info.arch == "riscv64" || info.arch == "loongarch64" {
        libs.push("atomic".to_string());
    }

    libs.push("z".to_string());

    if info.is_ios_like {
        libs.push("stdc++".to_string());
    }

    if info.is_apple {
        libs.push("objc".to_string());
        libs.push("swiftCore".to_string());
        libs.push("swiftFoundation".to_string());
        libs.push("icucore".to_string());
    }

    if !info.is_apple && info.libc_flavor != "bionic" {
        libs.push("ssl".to_string());
        libs.push("crypto".to_string());
    }

    if info.target_os == "freebsd" {
        libs.push("gssapi_krb5".to_string());
        libs.push("inotify".to_string());
    }

    libs
}

fn ilc_libs(info: &RidInfo) -> Vec<String> {
    if info.target_os == "win" {
        windows_ilc_libs(info)
    } else {
        unix_ilc_libs(info)
    }
}

fn windows_ilc_libs(info: &RidInfo) -> Vec<String> {
    let mut libs = Vec::new();

    libs.push("bootstrapperdll.obj".to_string());

    libs.push("Runtime.WorkstationGC.lib".to_string());

    libs.push("eventpipe-disabled.lib".to_string());

    if info.arch == "x64" {
        libs.push("Runtime.VxsortEnabled.lib".to_string());
    }
    libs.push("standalonegc-disabled.lib".to_string());

    libs.push("aotminipal.lib".to_string());

    libs.push("zlibstatic.lib".to_string());
    libs.push("brotlicommon.lib".to_string());
    libs.push("brotlienc.lib".to_string());
    libs.push("brotlidec.lib".to_string());

    libs.push("System.Globalization.Native.Aot.lib".to_string());
    libs.push("System.IO.Compression.Native.Aot.lib".to_string());

    libs
}

fn unix_ilc_libs(info: &RidInfo) -> Vec<String> {
    let mut libs = Vec::new();

    libs.push("libbootstrapperdll.o".to_string());

    libs.push("libRuntime.WorkstationGC.a".to_string());

    libs.push("libeventpipe-disabled.a".to_string());

    if info.arch == "x64" {
        libs.push("libRuntime.VxsortEnabled.a".to_string());
    }

    libs.push("libstandalonegc-disabled.a".to_string());

    libs.push("libaotminipal.a".to_string());

    if !info.is_ios_like {
        libs.push("libstdc++compat.a".to_string());
    }

    libs.push("libbrotlienc.a".to_string());
    libs.push("libbrotlidec.a".to_string());
    libs.push("libbrotlicommon.a".to_string());
    libs.push("libSystem.Native.a".to_string());
    libs.push("libSystem.Globalization.Native.a".to_string());
    libs.push("libSystem.IO.Compression.Native.a".to_string());
    if !info.target_os.starts_with("tvos") && info.libc_flavor != "bionic" {
        libs.push("libSystem.Net.Security.Native.a".to_string());
    }
    if info.is_apple {
        libs.push("libSystem.Security.Cryptography.Native.Apple.a".to_string());
    } else if info.target_os == "android" {
        libs.push("libSystem.Security.Cryptography.Native.Android.a".to_string());
    } else {
        libs.push("libSystem.Security.Cryptography.Native.OpenSsl.a".to_string());
    }

    libs
}
