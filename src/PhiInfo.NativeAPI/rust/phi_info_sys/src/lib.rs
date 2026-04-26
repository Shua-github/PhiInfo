#[repr(C)]
#[derive(Debug)]
pub struct FfiArray<T> {
    pub data: *const T,
    pub length: usize,
}

impl<T> FfiArray<T> {
    pub fn is_empty(&self) -> bool {
        self.data.is_null() || self.length == 0
    }
}

// utf8
pub type FfiString = FfiArray<u8>;

#[repr(C)]
#[derive(Debug)]
pub struct FfiResponse {
    pub code: u16,
    pub mime: FfiString,
    pub data: FfiArray<u8>,
}

#[repr(C)]
#[derive(Debug)]
pub struct FfiResult {
    pub code: u8,
    pub message_and_stack_trace: FfiString,
}

unsafe extern "C" {
    pub unsafe fn phi_info_init(
        ffi_files: FfiArray<FfiString>,
        ffi_image_format: FfiString,
        ffi_cldb_data: FfiArray<u8>,
    ) -> FfiResult;

    pub unsafe fn phi_info_reset() -> FfiResult;

    pub unsafe fn phi_info_call_router(path: FfiArray<u8>) -> FfiResponse;

    pub unsafe fn phi_info_get_image_formats() -> FfiArray<FfiString>;

    pub unsafe fn phi_info_free(ptr: *const std::ffi::c_void);
}
