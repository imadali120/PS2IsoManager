using System.IO;
using PS2IsoManager.Models;

namespace PS2IsoManager.Services;

public static class UlCfgService
{
    private const int RecordSize = 64;
    private const byte UsbExtremeMagic = 0x08;

    public static List<GameEntry> ReadAll(string ulCfgPath)
    {
        var entries = new List<GameEntry>();
        if (!File.Exists(ulCfgPath))
            return entries;

        var data = File.ReadAllBytes(ulCfgPath);
        int count = data.Length / RecordSize;

        for (int i = 0; i < count; i++)
        {
            int offset = i * RecordSize;
            var entry = ParseRecord(data, offset);
            if (entry != null)
                entries.Add(entry);
        }

        return entries;
    }

    private static GameEntry? ParseRecord(byte[] data, int offset)
    {
        if (offset + RecordSize > data.Length)
            return null;

        // 0x00: 32 bytes display name (ASCII, null-padded)
        string displayName = System.Text.Encoding.ASCII.GetString(data, offset, 32).TrimEnd('\0');

        // 0x20: 15 bytes "ul." + Game ID (null-padded)
        string idField = System.Text.Encoding.ASCII.GetString(data, offset + 0x20, 15).TrimEnd('\0');
        string gameId = idField.StartsWith("ul.") ? idField.Substring(3) : idField;

        // 0x2F: 1 byte chunk count
        byte chunkCount = data[offset + 0x2F];

        // 0x30: 1 byte media type
        byte mediaRaw = data[offset + 0x30];
        var media = mediaRaw == (byte)MediaType.CD ? MediaType.CD : MediaType.DVD;

        return new GameEntry
        {
            DisplayName = displayName,
            GameId = gameId,
            ChunkCount = chunkCount,
            Media = media
        };
    }

    public static void WriteAll(string ulCfgPath, List<GameEntry> entries)
    {
        using var fs = new FileStream(ulCfgPath, FileMode.Create, FileAccess.Write);
        foreach (var entry in entries)
        {
            var record = BuildRecord(entry);
            fs.Write(record, 0, record.Length);
        }
    }

    public static void AppendEntry(string ulCfgPath, GameEntry entry)
    {
        using var fs = new FileStream(ulCfgPath, FileMode.Append, FileAccess.Write);
        var record = BuildRecord(entry);
        fs.Write(record, 0, record.Length);
    }

    public static void DeleteEntry(string ulCfgPath, string gameId)
    {
        var entries = ReadAll(ulCfgPath);
        entries.RemoveAll(e => e.GameId == gameId);
        WriteAll(ulCfgPath, entries);
    }

    public static void RenameEntry(string ulCfgPath, string gameId, string newName)
    {
        var entries = ReadAll(ulCfgPath);
        var entry = entries.Find(e => e.GameId == gameId);
        if (entry != null)
        {
            entry.DisplayName = newName;
            WriteAll(ulCfgPath, entries);
        }
    }

    private static byte[] BuildRecord(GameEntry entry)
    {
        var record = new byte[RecordSize];

        // 0x00: Display name (32 bytes, ASCII, null-padded)
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(entry.DisplayName);
        Array.Copy(nameBytes, 0, record, 0, Math.Min(nameBytes.Length, 32));

        // 0x20: "ul." + Game ID (15 bytes, null-padded)
        string idField = "ul." + entry.GameId;
        var idBytes = System.Text.Encoding.ASCII.GetBytes(idField);
        Array.Copy(idBytes, 0, record, 0x20, Math.Min(idBytes.Length, 15));

        // 0x2F: Chunk count
        record[0x2F] = entry.ChunkCount;

        // 0x30: Media type
        record[0x30] = (byte)entry.Media;

        // 0x35: USBExtreme magic
        record[0x35] = UsbExtremeMagic;

        return record;
    }
}
