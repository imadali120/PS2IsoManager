using System.IO;

namespace PS2IsoManager.Services;

public static class IsoSplitterService
{
    private const long ChunkSize = 1_073_741_824; // 1 GiB
    private const int BufferSize = 4 * 1024 * 1024; // 4 MiB

    public static async Task<byte> SplitAsync(
        string isoPath,
        string outputDir,
        string gameName,
        string gameId,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        string crcHex = OplCrc32.ComputeHex(gameName);
        var fi = new FileInfo(isoPath);
        long totalSize = fi.Length;
        byte chunkCount = (byte)Math.Ceiling((double)totalSize / ChunkSize);

        // Phase 1: Pre-allocate ALL chunk files before writing any data.
        // This lets FAT32 find contiguous space for each file and avoids
        // fragmentation from interleaved allocations.
        var chunkPaths = new string[chunkCount];
        for (int part = 0; part < chunkCount; part++)
        {
            ct.ThrowIfCancellationRequested();

            string chunkName = $"ul.{crcHex}.{gameId}.{part:X2}";
            chunkPaths[part] = Path.Combine(outputDir, chunkName);

            long thisChunkSize = Math.Min(ChunkSize, totalSize - (long)part * ChunkSize);
            using var prealloc = new FileStream(chunkPaths[part], FileMode.Create, FileAccess.Write, FileShare.None);
            prealloc.SetLength(thisChunkSize);
        }

        // Phase 2: Write data into the pre-allocated files
        var buffer = new byte[BufferSize];
        long totalBytesRead = 0;

        using var input = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan);

        for (int part = 0; part < chunkCount; part++)
        {
            ct.ThrowIfCancellationRequested();

            long chunkSize = Math.Min(ChunkSize, totalSize - totalBytesRead);

            using var output = new FileStream(chunkPaths[part], FileMode.Open, FileAccess.Write, FileShare.None, BufferSize, FileOptions.SequentialScan);

            long bytesRemaining = chunkSize;
            while (bytesRemaining > 0)
            {
                ct.ThrowIfCancellationRequested();

                int toRead = (int)Math.Min(buffer.Length, bytesRemaining);
                int bytesRead = await input.ReadAsync(buffer.AsMemory(0, toRead), ct);
                if (bytesRead == 0) break;

                await output.WriteAsync(buffer.AsMemory(0, bytesRead), ct);

                totalBytesRead += bytesRead;
                bytesRemaining -= bytesRead;

                progress?.Report((double)totalBytesRead / totalSize);
            }
        }

        return chunkCount;
    }

    public static void DeleteChunks(string outputDir, string gameName, string gameId, byte chunkCount)
    {
        string crcHex = OplCrc32.ComputeHex(gameName);
        for (int i = 0; i < chunkCount; i++)
        {
            string chunkName = $"ul.{crcHex}.{gameId}.{i:X2}";
            string chunkPath = Path.Combine(outputDir, chunkName);
            if (File.Exists(chunkPath))
                File.Delete(chunkPath);
        }
    }

    public static void RenameChunks(string outputDir, string oldName, string newName, string gameId, byte chunkCount)
    {
        string oldCrc = OplCrc32.ComputeHex(oldName);
        string newCrc = OplCrc32.ComputeHex(newName);

        for (int i = 0; i < chunkCount; i++)
        {
            string oldChunk = Path.Combine(outputDir, $"ul.{oldCrc}.{gameId}.{i:X2}");
            string newChunk = Path.Combine(outputDir, $"ul.{newCrc}.{gameId}.{i:X2}");
            if (File.Exists(oldChunk))
                File.Move(oldChunk, newChunk);
        }
    }
}
