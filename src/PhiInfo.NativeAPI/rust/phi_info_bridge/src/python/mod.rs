use pyo3::pymodule;

#[pymodule(name = "phi_info_bridge")]
mod extension {
    use phi_info::PhiInfo;
    use phi_info::PhiResponse;
    use pyo3::prelude::*;

    #[pyclass]
    pub struct PyPhiResponse {
        #[pyo3(get)]
        pub code: u16,

        #[pyo3(get)]
        pub mime: String,

        #[pyo3(get)]
        pub data: Vec<u8>,
    }

    impl From<PhiResponse> for PyPhiResponse {
        fn from(r: PhiResponse) -> Self {
            Self {
                code: r.code,
                mime: r.mime,
                data: r.data,
            }
        }
    }

    #[pyfunction]
    fn init(files: Vec<String>, image_format: String, cldb_data: Vec<u8>) -> PyResult<()> {
        PhiInfo::init(
            &files.iter().map(|s| s.as_str()).collect::<Vec<_>>(),
            &image_format,
            &cldb_data,
        )
        .map_err(pyo3::exceptions::PyRuntimeError::new_err)?;

        Ok(())
    }

    #[pyfunction]
    fn reset() -> PyResult<()> {
        PhiInfo::reset().map_err(pyo3::exceptions::PyRuntimeError::new_err)?;
        Ok(())
    }

    #[pyfunction]
    fn call_router(path: String) -> PyResult<PyPhiResponse> {
        Ok(PhiInfo::call_router(&path).into())
    }

    #[pyfunction]
    fn get_image_formats() -> PyResult<Vec<String>> {
        Ok(PhiInfo::get_image_formats())
    }
}
