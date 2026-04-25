from phi_info import PhiInfoAPI
import json
from env import DLL, CLDB, APK_PATH

if __name__ == "__main__":
    api = PhiInfoAPI(DLL)
    print(api.get_image_formats())
    api.init([f"file://{APK_PATH}"], "JPEG", CLDB)
    print(api.call_router("/api_info.json"))
    print(api.call_router("/info/version.json"))
    print(json.loads(api.call_router("/info/tips.json").data)["zh_cn"][2])