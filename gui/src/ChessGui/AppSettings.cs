using System.Text.Json;
using ChessCore;
using System.Text.Json.Serialization;

namespace ChessGui;

public enum PlayerKind
{
    Human,
    Engine1,
    Engine2,
}

/// <summary>What is remembered about one engine slot.</summary>
public sealed class EngineSettings
{
    public string? Path { get; set; }

    public string? Arguments { get; set; }

    /// <summary>Keep analysing the current position whenever this engine isn't playing a move.</summary>
    public bool Analyse { get; set; }
}

/// <summary>User preferences, saved as JSON so the engines and setup survive restarts.</summary>
public sealed class AppSettings
{
    public const int EngineSlotCount = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new PlayerKindConverter(), new JsonStringEnumConverter() },
    };

    public List<EngineSettings> Engines { get; set; } = [new(), new()];

    /// <summary>Single-engine settings files kept the engine here; moved into <see cref="Engines"/> on load.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EnginePath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EngineArguments { get; set; }

    public PlayerKind WhitePlayer { get; set; } = PlayerKind.Human;

    public PlayerKind BlackPlayer { get; set; } = PlayerKind.Engine1;

    public bool ShowEngineArrow { get; set; } = true;

    public int MatchGames { get; set; } = 1000;

    /// <summary>
    /// Random moves added after each match opening, for variety between repeated openings.
    /// They are any legal move, blunders included, so 0 is best until the 293 openings run out.
    /// </summary>
    public int MatchRandomPlies { get; set; }

    /// <summary>
    /// How many match games are played at the same time. Each one runs its own pair of
    /// engine processes, so this is what decides how many CPU cores a match uses. One
    /// game at a time is the only setting that shows every move on the board.
    /// </summary>
    public int MatchParallelGames { get; set; } = 1;

    /// <summary>How long engines think in a match: a plain "go", a fixed time or node count per move, or a clock.</summary>
    public TimeControlKind MatchTimeControlKind { get; set; } = TimeControlKind.Clock;

    public int MatchMoveTimeMs { get; set; } = 500;

    public double MatchClockSeconds { get; set; } = 10;

    public double MatchIncrementSeconds { get; set; } = 0.1;

    public long MatchNodes { get; set; } = 100_000;

    /// <summary>The SPRT run on match results: H0 and H1 in Elo, and whether the match stops once it decides.</summary>
    public double SprtElo0 { get; set; }

    public double SprtElo1 { get; set; } = 10;

    public bool SprtStopMatch { get; set; } = true;

    public MatchTimeControl ToMatchTimeControl() => new()
    {
        Kind = MatchTimeControlKind,
        MoveTimeMs = Math.Max(1, MatchMoveTimeMs),
        BaseMs = Math.Max(1, (long)Math.Round(MatchClockSeconds * 1000)),
        IncrementMs = Math.Max(0, (long)Math.Round(MatchIncrementSeconds * 1000)),
        Nodes = Math.Max(1, MatchNodes),
    };

    public SprtSettings ToSprtSettings() => new() { Elo0 = SprtElo0, Elo1 = Math.Max(SprtElo1, SprtElo0 + 1) };

    /// <summary>Option values the user changed, keyed by engine executable path, then option name.</summary>
    public Dictionary<string, Dictionary<string, string>> EngineOptions { get; set; } = [];

    /// <summary>Settings location; set CHESSGUI_SETTINGS to use a different file.</summary>
    public static string FilePath { get; } =
        Environment.GetEnvironmentVariable("CHESSGUI_SETTINGS")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChessGui", "settings.json");

    public static AppSettings Load()
    {
        var settings = new AppSettings();
        try
        {
            if (File.Exists(FilePath))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable settings are not worth failing over; start from defaults.
        }
        settings.Normalize();
        return settings;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void Normalize()
    {
        while (Engines.Count < EngineSlotCount)
            Engines.Add(new EngineSettings());

        if (EnginePath is not null && Engines[0].Path is null)
        {
            Engines[0].Path = EnginePath;
            Engines[0].Arguments = EngineArguments;
        }
        EnginePath = null;
        EngineArguments = null;
    }

    /// <summary>Reads "Engine" from single-engine settings files as Engine 1.</summary>
    private sealed class PlayerKindConverter : JsonConverter<PlayerKind>
    {
        public override PlayerKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType != JsonTokenType.String ? PlayerKind.Human : reader.GetString() switch
            {
                "Engine1" or "Engine" => PlayerKind.Engine1,
                "Engine2" => PlayerKind.Engine2,
                _ => PlayerKind.Human,
            };

        public override void Write(Utf8JsonWriter writer, PlayerKind value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }
}
