using System;
using System.CommandLine;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using PhiInfo.Core;
using PhiInfo.Core.Asset;
using PhiInfo.Processing;
using SixLabors.ImageSharp;
using JsonContext = PhiInfo.Processing.JsonContext;

namespace PhiInfo.CLI;

internal static class Tool
{
    private static readonly JsonContext JsonContext = new(new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        WriteIndented = true
    });

    private static readonly Command InfoSongsCommand = CreateInfoCommand("songs", context =>
        JsonSerializer.SerializeToUtf8Bytes(context.Info.ExtractSongs(), JsonContext.ListSongInfo));

    private static readonly Command InfoCollectionCommand = CreateInfoCommand("collection", context =>
        JsonSerializer.SerializeToUtf8Bytes(context.Info.ExtractCollection(), JsonContext.ListFolder));

    private static readonly Command InfoAvatarsCommand = CreateInfoCommand("avatars", context =>
        JsonSerializer.SerializeToUtf8Bytes(context.Info.ExtractAvatars(), JsonContext.ListAvatar));

    private static readonly Command InfoTipsCommand = CreateInfoCommand("tips", context =>
        JsonSerializer.SerializeToUtf8Bytes(context.Info.ExtractTips(), JsonContext.ListString));

    private static readonly Command InfoChaptersCommand = CreateInfoCommand("chapters", context =>
        JsonSerializer.SerializeToUtf8Bytes(context.Info.ExtractChapters(), JsonContext.ListChapterInfo));

    private static readonly Command InfoAllCommand = CreateInfoCommand("all", context =>
        JsonSerializer.SerializeToUtf8Bytes(context.Info.ExtractAllInfo(), JsonContext.AllInfo));

    private static readonly Command InfoVersionCommand = CreateInfoCommand("version", context =>
        JsonSerializer.SerializeToUtf8Bytes(context.Info.GetPhiVersion(), JsonContext.PhiVersion));

    private static readonly Argument<string> BundleNameArgument = new("name")
    {
        Description = "Bundle Name"
    };

    private static readonly Command InfoCommand = new("info", "Print info JSON")
    {
        Subcommands =
        {
            InfoSongsCommand,
            InfoCollectionCommand,
            InfoAvatarsCommand,
            InfoTipsCommand,
            InfoChaptersCommand,
            InfoAllCommand,
            InfoVersionCommand
        }
    };

    private static readonly Command AssetTextCommand = new("text", "Print text asset bytes to stdout")
    {
        Arguments = { BundleNameArgument },
        Action = new CommandLineAction(HandleAssetTextCommand)
    };

    private static readonly Command AssetMusicCommand = new("music", "Print music asset bytes to stdout")
    {
        Arguments = { BundleNameArgument },
        Action = new CommandLineAction(HandleAssetMusicCommand)
    };

    private static readonly Command AssetImageCommand = new("image", "Print image asset bytes to stdout")
    {
        Arguments = { BundleNameArgument },
        Action = new CommandLineAction(HandleAssetImageCommand)
    };

    private static readonly Command AssetCommand = new("asset", "Print catalog or asset bytes")
    {
        Subcommands =
        {
            AssetTextCommand,
            AssetMusicCommand,
            AssetImageCommand
        },
        Action = new CommandLineAction(HandleAssetCatalogCommand)
    };

    public static readonly Command Command = new("tool", "Run tool mode commands")
    {
        Subcommands =
        {
            InfoCommand,
            AssetCommand
        }
    };

    private static Command CreateInfoCommand(string name, Func<PhiInfoContext, byte[]> producer)
    {
        return new Command(name, $"Print {name} info")
        {
            Action = new CommandLineAction(parseResult =>
            {
                try
                {
                    var output = producer(Program.GetContext(parseResult));
                    WriteStdout(output);
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);
                    return 1;
                }
            })
        };
    }

    private static int HandleAssetCatalogCommand(ParseResult parseResult)
    {
        try
        {
            var context = Program.GetContext(parseResult);
            var dict = context.Catalog.GetAll()
                .Where(v => v.Key.IsString && v.Value != null && v.Value.Value.IsString)
                .ToDictionary(v => v.Key.Str!, v => v.Value!.Value.Str!);

            var json = JsonSerializer.SerializeToUtf8Bytes(dict, JsonContext.DictionaryStringString);
            WriteStdout(json);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int HandleAssetTextCommand(ParseResult parseResult)
    {
        try
        {
            var context = Program.GetContext(parseResult);
            var name = parseResult.GetValue(BundleNameArgument)!;
            using var textData = context.Bundle.Get<UnityText>(name);
            WriteStdout(Encoding.UTF8.GetBytes(textData.Content));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int HandleAssetMusicCommand(ParseResult parseResult)
    {
        try
        {
            var context = Program.GetContext(parseResult);
            var name = parseResult.GetValue(BundleNameArgument)!;
            var musicData = PhiInfoDecoders.DecoderMusic(context.Bundle.Get<UnityMusic>(name));
            WriteStdout(musicData);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int HandleAssetImageCommand(ParseResult parseResult)
    {
        try
        {
            var context = Program.GetContext(parseResult);
            var name = parseResult.GetValue(BundleNameArgument)!;
            var formatName = parseResult.GetValue(Program.ImageFormatOption)!;
            var manager = Configuration.Default.ImageFormatsManager;
            var format = manager.FindByName(formatName)!;

            using var image = PhiInfoDecoders.DecoderImage(context.Bundle.Get<UnityImage>(name));
            using var stdout = Console.OpenStandardOutput();
            image.Save(stdout, manager.GetEncoder(format));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void WriteStdout(byte[] bytes)
    {
        using var stdout = Console.OpenStandardOutput();
        stdout.Write(bytes, 0, bytes.Length);
    }
}