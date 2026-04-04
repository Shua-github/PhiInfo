#pragma warning disable IDE1006
#pragma warning disable IDE0130

namespace global.PhiInfo.HttpServer.Type;

public record AppInfo(string version, string type);

public record ServerInfo(string version, string platform, AppInfo app);