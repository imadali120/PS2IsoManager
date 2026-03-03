namespace PS2IsoManager.Services;

/// <summary>
/// Exact replica of OPL's USBA_crc32 from usbsupport.c.
/// The algorithm has quirks: the table is built with signed arithmetic,
/// the initial CRC is the residual from the table-building loop, and
/// index 255 of the table is never written (defaults to 0).
/// </summary>
public static class OplCrc32
{
    private static readonly uint[] CrcTab;
    private static readonly int InitialCrc;

    static OplCrc32()
    {
        CrcTab = new uint[256];
        int crc = 0;
        int count;

        // Build table exactly as OPL does (signed int arithmetic)
        for (int table = 256; table != 0; table--)
        {
            crc = table << 23;
            for (count = 8; count != 0; count--)
            {
                if (crc < 0) // signed: MSB set
                    crc = crc << 1;
                else
                    crc = (crc << 1) ^ 0x04C11DB7;
            }
            int idx = 255 - table;
            if (idx >= 0 && idx < 256)
                CrcTab[idx] = (uint)crc;
        }
        // CrcTab[255] is never written — stays 0 (matches OPL's uninitialized memory behavior)
        // crc retains value from last table iteration (table=1)
        InitialCrc = crc;
    }

    public static uint Compute(string gameName)
    {
        var buffer = new byte[33];
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(gameName);
        Array.Copy(nameBytes, buffer, Math.Min(nameBytes.Length, 32));

        // Replicate OPL's do-while loop: process bytes until null terminator (inclusive)
        int crc = InitialCrc;
        int count = 0;
        byte b;
        do
        {
            b = buffer[count++];
            crc = (int)(CrcTab[((uint)crc >> 24) ^ b] ^ (((uint)crc << 8) & 0xFFFFFF00u));
        } while (b != 0);

        return (uint)crc;
    }

    public static string ComputeHex(string gameName)
    {
        return Compute(gameName).ToString("X8");
    }
}
