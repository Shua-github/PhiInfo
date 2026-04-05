using System;
using System.IO;
using AssetRipper.TextureDecoder.Etc;
using AssetRipper.TextureDecoder.Rgb.Formats;
using Fmod5Sharp;
using Fmod5Sharp.CodecRebuilders;
using PhiInfo.Core.Type;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PhiInfo.Processing;

using Image = SixLabors.ImageSharp.Image;

public static class PhiInfoDecoders
{
    public static byte[] DecoderMusic(Music raw)
    {
        var bank = FsbLoader.LoadFsbFromByteArray(raw.data);
        var music = FmodVorbisRebuilder.RebuildOggFile(bank.Samples[0]);
        return music;
    }

    private static Image LoadImage(Core.Type.Image raw)
    {
        switch (raw.format)
        {
            case 3:
                return Image.LoadPixelData<Rgb24>(raw.data, (int)raw.width, (int)raw.height);

            case 4:
                return Image.LoadPixelData<Rgba32>(raw.data, (int)raw.width, (int)raw.height);

            case 34:
            {
                EtcDecoder.DecompressETC<ColorBGRA<byte>, byte>(
                    raw.data, (int)raw.width, (int)raw.height, out var data);
                return Image.LoadPixelData<Bgra32>(data, (int)raw.width, (int)raw.height);
            }

            case 47:
            {
                EtcDecoder.DecompressETC2A8<ColorBGRA<byte>, byte>(
                    raw.data, (int)raw.width, (int)raw.height, out var data);
                return Image.LoadPixelData<Bgra32>(data, (int)raw.width, (int)raw.height);
            }

            default:
                throw new NotSupportedException($"Unknown format: {raw.format}");
        }
    }

    public static Image DecoderImage(Core.Type.Image raw)
    {
        var img = LoadImage(raw);
        img.Mutate(x => x.Flip(FlipMode.Vertical));
        return img;
    }

    public static byte[] DecoderImageToBmp(Core.Type.Image raw)
    {
        using var img = DecoderImage(raw);
        using var ms = new MemoryStream();
        img.Save(ms, new BmpEncoder());
        return ms.ToArray();
    }
}