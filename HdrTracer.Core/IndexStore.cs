using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HdrTracer.Core;

public static class IndexStore
{
    private const uint Magic = 0x43525448;
    private const int Version = 4;

    public sealed class CacheData
    {
        public required string DriveLetter { get; init; }
        public uint VolumeSerial { get; init; }
        public ulong JournalId { get; init; }
        public long LastUsn { get; init; }
        public required FileIndex Index { get; init; }
    }

    public static string GetCachePath(string driveLetter)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HdrTracer", "indexes");
        Directory.CreateDirectory(dir);
        var letter = driveLetter.TrimEnd(':');
        return Path.Combine(dir, $"{letter}.dat");
    }

    public static void Save(CacheData data)
    {
        var path = GetCachePath(data.DriveLetter);
        var tmp = path + ".tmp";

        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write(Magic);
            bw.Write(Version);

            bw.Write((char)data.DriveLetter[0]);
            bw.Write((char)data.DriveLetter[1]);

            bw.Write(data.VolumeSerial);
            bw.Write(data.JournalId);
            bw.Write(data.LastUsn);

            data.Index.WriteTo(bw);

            var ngram = data.Index.Ngram;
            if (ngram is not null)
            {
                bw.Write((byte)1);
                bw.Write(data.Index.NgramBuiltAtCount);
                ngram.WriteTo(bw);
            }
            else
            {
                bw.Write((byte)0);
            }
        }

        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }

    public static CacheData? TryLoad(string driveLetter)
    {
        var path = GetCachePath(driveLetter);
        if (!File.Exists(path)) return null;

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
            using var br = new BinaryReader(fs);

            if (br.ReadUInt32() != Magic) return null;
            if (br.ReadInt32() != Version) return null;

            var letter = "" + br.ReadChar() + br.ReadChar();
            if (!string.Equals(letter, driveLetter, StringComparison.OrdinalIgnoreCase)) return null;

            uint volumeSerial = br.ReadUInt32();
            ulong journalId = br.ReadUInt64();
            long lastUsn = br.ReadInt64();

            var index = FileIndex.ReadFrom(br);
            index.DriveLetter = letter;

            byte hasNgram = br.ReadByte();
            if (hasNgram == 1)
            {
                int builtAtCount = br.ReadInt32();
                var ngram = NgramIndex.ReadFrom(br);
                index.SetNgramIndex(ngram, builtAtCount);
            }

            return new CacheData
            {
                DriveLetter = letter,
                VolumeSerial = volumeSerial,
                JournalId = journalId,
                LastUsn = lastUsn,
                Index = index
            };
        }
        catch
        {
            return null;
        }
    }

    public static void Delete(string driveLetter)
    {
        try { File.Delete(GetCachePath(driveLetter)); } catch { }
    }

    public static uint GetVolumeSerial(string driveLetter)
    {
        var sb = new System.Text.StringBuilder(261);
        var fs = new System.Text.StringBuilder(261);
        if (GetVolumeInformation(
                driveLetter + "\\",
                sb, sb.Capacity,
                out uint serial,
                out _, out _,
                fs, fs.Capacity))
        {
            return serial;
        }
        return 0;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GetVolumeInformation(
        string rootPathName,
        System.Text.StringBuilder volumeNameBuffer,
        int volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        System.Text.StringBuilder fileSystemNameBuffer,
        int fileSystemNameSize);
}