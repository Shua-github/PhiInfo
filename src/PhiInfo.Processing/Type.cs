using System;
using System.IO;

#pragma warning disable IDE1006
#pragma warning disable IDE0130

namespace PhiInfo.Processing.Type;

[Obsolete]
public record AppInfo(string version, string type);

public record struct Suffix(string image, string text, string music);

public record ApiInfo(string version, string type, Suffix suffix);

public record Response(ushort code, string? mime, byte[]? data);

public interface IOutputWriter
{
    Stream Create(string path, string mime);
}