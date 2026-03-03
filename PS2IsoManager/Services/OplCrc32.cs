namespace PS2IsoManager.Services;

/// <summary>
/// Exact replica of OPL's USBA_crc32 from system.c (Open PS2 Loader).
/// Verified against the actual source at github.com/ps2homebrew/Open-PS2-Loader.
/// </summary>
public static class OplCrc32
{
    private static readonly uint[] CrcTab = BuildTable();

    private static uint[] BuildTable()
    {
        var crctab = new uint[256];
        for (int table = 0; table < 256; table++)
        {
            int crc = table << 24;
            for (int count = 8; count > 0; count--)
            {
                if (crc < 0) // signed: MSB set
                    crc = crc << 1;
                else
                    crc = (crc << 1) ^ 0x04C11DB7;
            }
            crctab[255 - table] = (uint)crc;
        }
        return crctab;
    }

    public static uint Compute(string gameName)
    {
        var buffer = new byte[33];
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(gameName);
        Array.Copy(nameBytes, buffer, Math.Min(nameBytes.Length, 32));

        // Initial CRC is 0 (residual from table building: 255 << 24 shifted 8 times = 0)
        int crc = 0;
        int count = 0;
        do
        {
            int b = buffer[count++];
            crc = (int)(CrcTab[b ^ ((crc >> 24) & 0xFF)] ^ (((uint)crc << 8) & 0xFFFFFF00u));
        } while (buffer[count - 1] != 0 && count <= 32);

        return (uint)crc;
    }

    public static string ComputeHex(string gameName)
    {
        return Compute(gameName).ToString("X8");
    }
}
