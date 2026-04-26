using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using PhiInfo.Core;
using PhiInfo.Processing;
using PhiInfo.Processing.DataProvider;
using Shua.Zip;
using Shua.Zip.ReadAt;
using SixLabors.ImageSharp;
using CallConv = System.Runtime.CompilerServices.CallConvCdecl;

namespace PhiInfo.NativeAPI;

public static class PhiInfoNativeApi
{
    private static PhiInfoRouter? _router;
    private static PhiInfoContext? _context;

    [UnmanagedCallersOnly(EntryPoint = "phi_info_init", CallConvs = [typeof(CallConv)])]
    public static FfiResult PhiInfoInit(FfiArray<FfiArray<byte>> ffiFiles, FfiArray<byte> ffiImageFormat,
        FfiArray<byte> ffiCldbData)
    {
        try
        {
            ResetInternal();
            var files = ffiFiles.AsSpan().ToArray();
            var imgFmtName = Encoding.UTF8.GetString(ffiImageFormat.AsSpan());
            var cldbStream = new MemoryStream(ffiCldbData.AsSpan().ToArray());

            var manager = Configuration.Default.ImageFormatsManager;
            var imgFormat = manager.ImageFormats.FirstOrDefault(f =>
                                string.Equals(f.Name, imgFmtName, StringComparison.OrdinalIgnoreCase))
                            ?? throw new ArgumentException($"Unknown image format: {imgFmtName}");

            var urls = files.Select(f => Encoding.UTF8.GetString(f.AsSpan())).ToArray();

            var zips = new ShuaZip[urls.Length];
            for (var i = 0; i < urls.Length; i++) zips[i] = new ShuaZip(CreateReadAt(urls[i]));

            _context = new PhiInfoContext(new AndroidPackagesDataProvider(zips, cldbStream));
            _router = new PhiInfoRouter(_context, "native", imgFormat);
        }
        catch (Exception ex)
        {
            ResetInternal();
            return ex;
        }

        return FfiResult.OK;
    }

    [UnmanagedCallersOnly(EntryPoint = "phi_info_reset", CallConvs = [typeof(CallConv)])]
    public static FfiResult PhiInfoReset()
    {
        try
        {
            ResetInternal();
            return FfiResult.OK;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "phi_info_call_router", CallConvs = [typeof(CallConv)])]
    public static FfiResponse PhiInfoCallRouter(FfiArray<byte> path)
    {
        try
        {
            var pathStr = Encoding.UTF8.GetString(path.AsSpan());
            if (string.IsNullOrEmpty(pathStr))
                return CreateErrorResponse("Missing path");

            var router = _router;
            if (router is null)
                return CreateErrorResponse("Not initialized");

            FfiResponse response = router.Handle(pathStr);
            return response;
        }
        catch (Exception ex)
        {
            return CreateErrorResponse(ex.Message);
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "phi_info_get_image_formats", CallConvs = [typeof(CallConv)])]
    public static FfiArray<FfiArray<byte>> PhiInfoGetImageFormats()
    {
        try
        {
            var names = Configuration.Default.ImageFormatsManager.ImageFormats
                .Select(f => (FfiArray<byte>)Encoding.UTF8.GetBytes(f.Name))
                .ToArray();

            FfiArray<FfiArray<byte>> result = names;
            return result;
        }
        catch
        {
            return default;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "phi_info_free", CallConvs = [typeof(CallConv)])]
    public static void PhiInfoFree(IntPtr ptr)
    {
        if (ptr != IntPtr.Zero) Marshal.FreeHGlobal(ptr);
    }

    private static IReadAt CreateReadAt(string url)
    {
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return new HttpReadAt(url);

        string localPath;
        if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            localPath = new Uri(url).LocalPath;
        else
            localPath = url;

        if (!File.Exists(localPath))
            throw new FileNotFoundException($"File not found: {localPath}");
        return new MmapReadAt(localPath);
    }

    private static FfiResponse CreateErrorResponse(string message)
    {
        return new FfiResponse
        {
            Code = 500,
            Mime = "text/plain"u8,
            Data = Encoding.UTF8.GetBytes(message)
        };
    }

    private static void ResetInternal()
    {
        _router = null;
        _context?.Dispose();
        _context = null;
    }
}