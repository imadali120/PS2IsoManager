namespace PS2IsoManager.Services;

public static class OplCrc32
{
    private const uint Polynomial = 0x04C11DB7;
    private static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i << 24;
            for (int j = 0; j < 8; j++)
            {
                // OPL's inverted branching: XOR when MSB is NOT set
                if ((crc & 0x80000000) == 0)
                    crc = (crc << 1) ^ Polynomial;
                else
                    crc <<= 1;
            }
            table[i] = crc;
        }
        return table;
    }

    public static uint Compute(string gameName)
    {
        // OPL pads the name into a 32-byte null buffer and processes name.Length + 1 bytes
        var buffer = new byte[32];
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(gameName);
        Array.Copy(nameBytes, buffer, Math.Min(nameBytes.Length, 32));

        int length = nameBytes.Length + 1; // includes null terminator

        uint crc = 0xFFFFFFFF;
        for (int i = 0; i < length; i++)
        {
            // Index with 255 - tableIndex (OPL's non-standard indexing)
            byte tableIndex = (byte)((crc >> 24) ^ buffer[i]);
            crc = (crc << 8) ^ Table[255 - tableIndex];
        }

        return crc;
    }

    public static string ComputeHex(string gameName)
    {
        return Compute(gameName).ToString("X8");
    }
}
