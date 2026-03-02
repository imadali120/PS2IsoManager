using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PS2IsoManager.Services;

public static class Iso9660Reader
{
    private const int SectorSize = 2048;
    private const int PvdSector = 16;

    public static string? ExtractGameId(string isoPath)
    {
        try
        {
            using var fs = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(fs);

            // Read Primary Volume Descriptor at sector 16
            fs.Seek(PvdSector * SectorSize, SeekOrigin.Begin);
            var pvd = reader.ReadBytes(SectorSize);

            // Verify PVD signature (byte 0 = 1, bytes 1-5 = "CD001")
            if (pvd[0] != 1 || Encoding.ASCII.GetString(pvd, 1, 5) != "CD001")
                return null;

            // Root directory record is at offset 156 in the PVD
            // Location of extent (LBA) is at offset 2 (little-endian) within the dir record
            int rootLba = BitConverter.ToInt32(pvd, 156 + 2);
            int rootSize = BitConverter.ToInt32(pvd, 156 + 10);

            // Read the root directory
            fs.Seek((long)rootLba * SectorSize, SeekOrigin.Begin);
            var rootDir = reader.ReadBytes(rootSize);

            // Find SYSTEM.CNF in the root directory
            int offset = 0;
            while (offset < rootDir.Length)
            {
                byte recordLen = rootDir[offset];
                if (recordLen == 0)
                {
                    // Skip to next sector boundary
                    offset = ((offset / SectorSize) + 1) * SectorSize;
                    if (offset >= rootDir.Length) break;
                    continue;
                }

                byte nameLen = rootDir[offset + 32];
                if (nameLen > 0 && offset + 33 + nameLen <= rootDir.Length)
                {
                    string fileName = Encoding.ASCII.GetString(rootDir, offset + 33, nameLen);
                    // ISO9660 filenames may have ";1" version suffix
                    fileName = fileName.Split(';')[0].TrimEnd('.');

                    if (fileName.Equals("SYSTEM.CNF", StringComparison.OrdinalIgnoreCase))
                    {
                        int fileLba = BitConverter.ToInt32(rootDir, offset + 2);
                        int fileSize = BitConverter.ToInt32(rootDir, offset + 10);

                        fs.Seek((long)fileLba * SectorSize, SeekOrigin.Begin);
                        var cnfData = reader.ReadBytes(Math.Min(fileSize, 1024));
                        string cnfText = Encoding.ASCII.GetString(cnfData);

                        return ParseGameIdFromCnf(cnfText);
                    }
                }

                offset += recordLen;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ParseGameIdFromCnf(string cnfText)
    {
        // Look for BOOT2 = cdrom0:\SLUS_202.65;1 (or similar patterns)
        var match = Regex.Match(cnfText, @"BOOT2?\s*=\s*\S*\\([A-Z]{4}[_-]\d{3}\.\d{2})", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value.ToUpperInvariant();
        }

        // Fallback: try to find a game ID pattern anywhere
        match = Regex.Match(cnfText, @"([A-Z]{4}[_-]\d{3}\.\d{2})", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    public static Models.MediaType DetectMediaType(string isoPath)
    {
        var fi = new FileInfo(isoPath);
        // CD-ROM ISOs are typically under 700 MB
        return fi.Length <= 700L * 1024 * 1024 ? Models.MediaType.CD : Models.MediaType.DVD;
    }
}
