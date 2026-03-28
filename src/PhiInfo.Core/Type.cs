#pragma warning disable IDE1006
#pragma warning disable IDE0130

using System;
using System.Collections.Generic;

namespace PhiInfo.Core.Type
{
    public struct SongLevel
    {
        public string charter { get; set; }
        public int all_combo_num { get; set; }
        public double difficulty { get; set; }
    }

    public struct SongInfo
    {
        public string id { get; set; }
        public string key { get; set; }
        public string name { get; set; }
        public string composer { get; set; }
        public string illustrator { get; set; }
        public double preview_time { get; set; }
        public double preview_end_time { get; set; }
        public Dictionary<string, SongLevel> levels { get; set; }
    }

    public struct Folder
    {
        public string title { get; set; }
        public string sub_title { get; set; }
        public string cover { get; set; }
        public List<FileItem> files { get; set; }
    }

    public struct FileItem
    {
        public string key { get; set; }
        public int sub_index { get; set; }
        public string name { get; set; }
        public string date { get; set; }
        public string supervisor { get; set; }
        public string category { get; set; }
        public string content { get; set; }
        public string properties { get; set; }
    }

    public struct Avatar
    {
        public string name { get; set; }
        public string addressable_key { get; set; }
    }

    public struct AllInfo
    {
        public List<SongInfo> songs { get; set; }
        public List<Folder> collection { get; set; }
        public List<Avatar> avatars { get; set; }
        public List<string> tips { get; set; }
        public List<ChapterInfo> chapters { get; set; }
    }

    public struct Catalog
    {
        public string m_KeyDataString { get; set; }
        public string m_BucketDataString { get; set; }
        public string m_EntryDataString { get; set; }
    }

    public struct Image
    {
        public uint format { get; set; }
        public uint width { get; set; }
        public uint height { get; set; }
        public byte[] data { get; set; }

        public readonly byte[] WithHeader()
        {
            var result = new byte[1 + 4 + 4 + 4 + data.Length];
            result[0] = 72;
            BitConverter.GetBytes(format).CopyTo(result, 1);
            BitConverter.GetBytes(height).CopyTo(result, 5);
            BitConverter.GetBytes(width).CopyTo(result, 9);
            data.CopyTo(result, 13);
            return result;
        }
    }

    public struct Music
    {
        public float length { get; set; }
        public byte[] data { get; set; }
    }

    public struct Text
    {
        public string content { get; set; }
    }

    public struct ChapterInfo
    {
        public string code { get; set; }
        public string banner { get; set; }
        public List<string> songs { get; set; }
    }
}