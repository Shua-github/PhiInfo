use phi_info_sys::*;
use std::slice;

#[derive(Debug)]
pub struct PhiResponse {
    pub code: u16,
    pub mime: String,
    pub data: Vec<u8>,
}

pub struct PhiInfo;

impl PhiInfo {
    fn check_error(res: FfiResult) -> Result<(), String> {
        if res.code == 0 {
            Ok(())
        } else {
            if res.message_and_stack_trace.is_empty() {
                return Err(format!("Unknown error with code {}", res.code));
            }
            unsafe {
                let err = String::from_utf8_lossy(slice::from_raw_parts(
                    res.message_and_stack_trace.data,
                    res.message_and_stack_trace.length,
                ))
                .into_owned();
                phi_info_free(res.message_and_stack_trace.data as _);
                Err(err)
            }
        }
    }

    pub fn init(files: &[&str], image_format: &str, cldb_data: &[u8]) -> Result<(), String> {
        let ffi_files_raw: Vec<FfiString> = files
            .iter()
            .map(|s| FfiString {
                data: s.as_ptr(),
                length: s.len(),
            })
            .collect();

        let ffi_files = FfiArray {
            data: ffi_files_raw.as_ptr(),
            length: ffi_files_raw.len(),
        };

        let ffi_image_format = FfiString {
            data: image_format.as_ptr(),
            length: image_format.len(),
        };

        let ffi_cldb = FfiArray {
            data: cldb_data.as_ptr(),
            length: cldb_data.len(),
        };

        let res = unsafe { phi_info_init(ffi_files, ffi_image_format, ffi_cldb) };
        Self::check_error(res)
    }

    pub fn reset() -> Result<(), String> {
        let res = unsafe { phi_info_reset() };
        Self::check_error(res)
    }

    pub fn call_router(path: &str) -> PhiResponse {
        let ffi_path = FfiArray {
            data: path.as_ptr(),
            length: path.len(),
        };

        unsafe {
            let res = phi_info_call_router(ffi_path);

            let mime_str = if res.mime.is_empty() {
                String::new()
            } else {
                String::from_utf8_lossy(slice::from_raw_parts(res.mime.data, res.mime.length))
                    .into_owned()
            };

            let data_vec = if res.data.is_empty() {
                Vec::new()
            } else {
                slice::from_raw_parts(res.data.data, res.data.length).to_vec()
            };

            let response = PhiResponse {
                code: res.code,
                mime: mime_str,
                data: data_vec,
            };

            phi_info_free(res.mime.data as _);
            phi_info_free(res.data.data as _);

            response
        }
    }

    pub fn get_image_formats() -> Vec<String> {
        unsafe {
            let ffi_array = phi_info_get_image_formats();
            if ffi_array.is_empty() {
                return Vec::new();
            }
            let mut formats = Vec::with_capacity(ffi_array.length);
            let items = slice::from_raw_parts(ffi_array.data, ffi_array.length);

            for item in items {
                if item.is_empty() {
                    continue;
                }
                let s = String::from_utf8_lossy(slice::from_raw_parts(item.data, item.length))
                    .into_owned();
                formats.push(s);
                phi_info_free(item.data as _);
            }
            phi_info_free(ffi_array.data as _);

            formats
        }
    }
}
