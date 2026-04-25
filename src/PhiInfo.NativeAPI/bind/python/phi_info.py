import ctypes
from dataclasses import dataclass
from typing import list, Any

nint = ctypes.c_ssize_t

class FfiString(ctypes.Structure):
    _fields_ = [
        ("Data", ctypes.POINTER(ctypes.c_uint8)),
        ("Length", nint)
    ]

class FfiByteArray(ctypes.Structure):
    _fields_ = [
        ("Data", ctypes.POINTER(ctypes.c_uint8)),
        ("Length", nint)
    ]

class FfiStringArray(ctypes.Structure):
    _fields_ = [
        ("Data", ctypes.POINTER(FfiString)),
        ("Length", nint)
    ]

class FfiResponseStruct(ctypes.Structure):
    _fields_ = [
        ("Code", ctypes.c_uint16),
        ("Mime", FfiString),
        ("Data", FfiByteArray)
    ]

@dataclass
class Response:
    code: int
    mime: str
    data: bytes

class PhiInfoException(Exception):
    """PhiInfo 原生层返回的异常"""
    pass

class PhiInfoAPI:
    def __init__(self, lib_path: str):
        self.lib = ctypes.cdll.LoadLibrary(lib_path)
        self._setup_signatures()

    def _setup_signatures(self):
        self.lib.phi_info_init.argtypes = [FfiStringArray, FfiString, FfiByteArray]
        self.lib.phi_info_init.restype = ctypes.c_uint8

        self.lib.phi_info_reset.argtypes = []
        self.lib.phi_info_reset.restype = ctypes.c_uint8

        self.lib.phi_info_call_router.argtypes = [FfiString]
        self.lib.phi_info_call_router.restype = FfiResponseStruct

        self.lib.phi_info_get_image_formats.argtypes = []
        self.lib.phi_info_get_image_formats.restype = FfiStringArray

        self.lib.phi_info_get_last_error.argtypes = []
        self.lib.phi_info_get_last_error.restype = FfiString

        self.lib.phi_info_clear_error.argtypes = []
        self.lib.phi_info_clear_error.restype = ctypes.c_int32

        self.lib.phi_info_free.argtypes = [ctypes.c_void_p]
        self.lib.phi_info_free.restype = None

    def _free_ptr(self, ptr):
        if ptr:
            self.lib.phi_info_free(ctypes.cast(ptr, ctypes.c_void_p))

    def _to_ffi_string(self, s: str) -> tuple[FfiString, Any]:
        encoded = s.encode('utf-8')
        length = len(encoded)
        buf = (ctypes.c_uint8 * length)(*encoded)
        ptr = ctypes.cast(buf, ctypes.POINTER(ctypes.c_uint8))
        return FfiString(ptr, length), buf

    def _to_ffi_byte_array(self, b: bytes) -> tuple[FfiByteArray, Any]:
        length = len(b)
        buf = (ctypes.c_uint8 * length)(*b)
        ptr = ctypes.cast(buf, ctypes.POINTER(ctypes.c_uint8))
        return FfiByteArray(ptr, length), buf

    def _to_ffi_string_array(self, strings: list[str]) -> tuple[FfiStringArray, Any]:
        length = len(strings)
        ffi_strings = []
        refs = []
        for s in strings:
            ffi_s, ref = self._to_ffi_string(s)
            ffi_strings.append(ffi_s)
            refs.append(ref)

        arr_buf = (FfiString * length)(*ffi_strings)
        ptr = ctypes.cast(arr_buf, ctypes.POINTER(FfiString))
        return FfiStringArray(ptr, length), (arr_buf, refs)

    def _read_and_free_ffi_string(self, ffi_str: FfiString) -> str:
        if not ffi_str.Data or ffi_str.Length == 0:
            self._free_ptr(ffi_str.Data)
            return ""
        
        res_bytes = ctypes.string_at(ffi_str.Data, ffi_str.Length)
        self._free_ptr(ffi_str.Data)
        return res_bytes.decode('utf-8')

    def _check_error(self):
        err_ffi = self.lib.phi_info_get_last_error()
        err_msg = self._read_and_free_ffi_string(err_ffi)
        if err_msg:
            self.lib.phi_info_clear_error()
            raise PhiInfoException(f"Native API Error: {err_msg}")

    def init(self, files: list[str], image_format: str, cldb_data: bytes) -> None:
        ffi_files, ref_files = self._to_ffi_string_array(files)
        ffi_fmt, ref_fmt = self._to_ffi_string(image_format)
        ffi_cldb, ref_cldb = self._to_ffi_byte_array(cldb_data)

        result_code = self.lib.phi_info_init(ffi_files, ffi_fmt, ffi_cldb)
        if result_code != 0:
            self._check_error()

    def reset(self) -> None:
        result_code = self.lib.phi_info_reset()
        if result_code != 0:
            self._check_error()

    def call_router(self, path: str) -> Response:
        ffi_path, ref_path = self._to_ffi_string(path)
        
        res_ffi = self.lib.phi_info_call_router(ffi_path)

        code = res_ffi.Code
        mime = self._read_and_free_ffi_string(res_ffi.Mime)

        data_bytes = b""
        if res_ffi.Data.Data and res_ffi.Data.Length > 0:
            data_bytes = ctypes.string_at(res_ffi.Data.Data, res_ffi.Data.Length)
        self._free_ptr(res_ffi.Data.Data)

        return Response(code=code, mime=mime, data=data_bytes)

    def get_image_formats(self) -> list[str]:
        res_ffi = self.lib.phi_info_get_image_formats()
        
        if not res_ffi.Data:
            self._check_error()
            return []

        formats = []
        for i in range(res_ffi.Length):
            ffi_s = res_ffi.Data[i]
            formats.append(self._read_and_free_ffi_string(ffi_s))

        self._free_ptr(res_ffi.Data)
        return formats