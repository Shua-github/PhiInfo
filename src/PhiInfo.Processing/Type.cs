#pragma warning disable IDE1006
#pragma warning disable IDE0130

namespace PhiInfo.Processing.Type;

public record AppInfo(string version, string type);

public record ServerInfo(string version, string platform, AppInfo app);

public record Response(ushort code, string? mime, byte[]? data);