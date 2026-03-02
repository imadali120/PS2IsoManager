namespace PS2IsoManager.Models;

public class GameEntry
{
    public string DisplayName { get; set; } = string.Empty;
    public string GameId { get; set; } = string.Empty;
    public byte ChunkCount { get; set; }
    public MediaType Media { get; set; }

    public string FormattedGameId => GameId.Replace('.', '_').Replace('\\', '_');
}
