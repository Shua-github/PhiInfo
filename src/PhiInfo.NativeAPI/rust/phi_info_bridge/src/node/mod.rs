use napi::bindgen_prelude::*;
use napi_derive::napi;

use phi_info::{PhiInfo, PhiResponse};

#[napi(object)]
pub struct JsPhiResponse {
    pub code: u16,
    pub mime: String,
    pub data: Buffer,
}

#[napi]
pub fn init(files: Vec<String>, image_format: String, cldb_data: Buffer) -> Result<()> {
    let files_ref: Vec<&str> = files.iter().map(|s| s.as_str()).collect();

    PhiInfo::init(&files_ref, &image_format, cldb_data.as_ref())
        .map_err(|e| Error::from_reason(e.to_string()))?;

    Ok(())
}

#[napi]
pub fn reset() -> Result<()> {
    PhiInfo::reset().map_err(|e| Error::from_reason(e.to_string()))?;
    Ok(())
}

#[napi]
pub fn call_router(path: String) -> Result<JsPhiResponse> {
    let resp: PhiResponse = PhiInfo::call_router(&path);

    Ok(JsPhiResponse {
        code: resp.code,
        mime: resp.mime,
        data: resp.data.into(),
    })
}

#[napi]
pub fn get_image_formats() -> Result<Vec<String>> {
    Ok(PhiInfo::get_image_formats())
}
