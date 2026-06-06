using System.Data;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Natives;

namespace Jailbreak;

public sealed class WardenDatabase
{
    private const string TableName = "jb_warden_settings";

    private static readonly Color DefaultLaserColor = new(255, 40, 40, 230);
    private static readonly Color DefaultBeamColor = new(80, 170, 255, 220);

    private readonly ISwiftlyCore _core;
    private readonly UtilsConfig _utilsConfig;
    private readonly Dictionary<ulong, WardenVisualSettings> _cache = [];
    private readonly object _lock = new();

    public WardenDatabase(ISwiftlyCore core, IOptions<UtilsConfig> utilsConfig)
    {
        _core = core;
        _utilsConfig = utilsConfig.Value;
    }

    public void Initialize()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = $"""
            CREATE TABLE IF NOT EXISTS {TableName} (
                steam_id VARCHAR(32) PRIMARY KEY,
                laser_r SMALLINT NOT NULL DEFAULT 255,
                laser_g SMALLINT NOT NULL DEFAULT 40,
                laser_b SMALLINT NOT NULL DEFAULT 40,
                laser_a SMALLINT NOT NULL DEFAULT 230,
                laser_rainbow SMALLINT NOT NULL DEFAULT 0,
                beam_r SMALLINT NOT NULL DEFAULT 80,
                beam_g SMALLINT NOT NULL DEFAULT 170,
                beam_b SMALLINT NOT NULL DEFAULT 255,
                beam_a SMALLINT NOT NULL DEFAULT 220,
                beam_rainbow SMALLINT NOT NULL DEFAULT 0
            )
            """;

        command.ExecuteNonQuery();

        EnsureColumn(connection, "laser_rainbow", "SMALLINT NOT NULL DEFAULT 0");
        EnsureColumn(connection, "beam_rainbow", "SMALLINT NOT NULL DEFAULT 0");
    }

    public WardenVisualSettings GetWardenSettings(ulong steamId)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(steamId, out var cached))
                return cached;
        }

        var settings = LoadWardenSettings(steamId);

        lock (_lock)
        {
            _cache[steamId] = settings;
        }

        return settings;
    }

    public Color GetWardenLaserColor(ulong steamId)
    {
        return GetWardenSettings(steamId).LaserColor;
    }

    public Color GetWardenBeamColor(ulong steamId)
    {
        return GetWardenSettings(steamId).BeamColor;
    }

    public bool IsWardenLaserRainbow(ulong steamId)
    {
        return GetWardenSettings(steamId).LaserRainbow;
    }

    public bool IsWardenBeamRainbow(ulong steamId)
    {
        return GetWardenSettings(steamId).BeamRainbow;
    }

    public void SaveWardenLaserColor(ulong steamId, Color color)
    {
        var current = GetWardenSettings(steamId);
        SaveWardenSettings(steamId, current with { LaserColor = color, LaserRainbow = false });
    }

    public void SaveWardenBeamColor(ulong steamId, Color color)
    {
        var current = GetWardenSettings(steamId);
        SaveWardenSettings(steamId, current with { BeamColor = color, BeamRainbow = false });
    }

    public void SaveWardenLaserRainbow(ulong steamId)
    {
        var current = GetWardenSettings(steamId);
        SaveWardenSettings(steamId, current with { LaserRainbow = true });
    }

    public void SaveWardenBeamRainbow(ulong steamId)
    {
        var current = GetWardenSettings(steamId);
        SaveWardenSettings(steamId, current with { BeamRainbow = true });
    }

    public void SaveWardenSettings(ulong steamId, WardenVisualSettings settings)
    {
        using var connection = OpenConnection();
        using var update = connection.CreateCommand();

        update.CommandText = $"""
            UPDATE {TableName}
            SET laser_r = @laser_r,
                laser_g = @laser_g,
                laser_b = @laser_b,
                laser_a = @laser_a,
                laser_rainbow = @laser_rainbow,
                beam_r = @beam_r,
                beam_g = @beam_g,
                beam_b = @beam_b,
                beam_a = @beam_a,
                beam_rainbow = @beam_rainbow
            WHERE steam_id = @steam_id
            """;

        AddSettingsParameters(update, steamId, settings);
        var affectedRows = update.ExecuteNonQuery();

        if (affectedRows == 0)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = $"""
                INSERT INTO {TableName} (
                    steam_id,
                    laser_r,
                    laser_g,
                    laser_b,
                    laser_a,
                    laser_rainbow,
                    beam_r,
                    beam_g,
                    beam_b,
                    beam_a,
                    beam_rainbow
                ) VALUES (
                    @steam_id,
                    @laser_r,
                    @laser_g,
                    @laser_b,
                    @laser_a,
                    @laser_rainbow,
                    @beam_r,
                    @beam_g,
                    @beam_b,
                    @beam_a,
                    @beam_rainbow
                )
                """;

            AddSettingsParameters(insert, steamId, settings);
            insert.ExecuteNonQuery();
        }

        lock (_lock)
        {
            _cache[steamId] = settings;
        }
    }

    public void RemoveFromCache(ulong steamId)
    {
        lock (_lock)
        {
            _cache.Remove(steamId);
        }
    }

    public void ClearCache()
    {
        lock (_lock)
        {
            _cache.Clear();
        }
    }

    private WardenVisualSettings LoadWardenSettings(ulong steamId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT laser_r,
                   laser_g,
                   laser_b,
                   laser_a,
                   laser_rainbow,
                   beam_r,
                   beam_g,
                   beam_b,
                   beam_a,
                   beam_rainbow
            FROM {TableName}
            WHERE steam_id = @steam_id
            """;

        AddParameter(command, "@steam_id", steamId.ToString());

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return WardenVisualSettings.Default;

        return new WardenVisualSettings(
            new Color(
                ToByte(reader["laser_r"]),
                ToByte(reader["laser_g"]),
                ToByte(reader["laser_b"]),
                ToByte(reader["laser_a"])),
            ToBool(reader["laser_rainbow"]),
            new Color(
                ToByte(reader["beam_r"]),
                ToByte(reader["beam_g"]),
                ToByte(reader["beam_b"]),
                ToByte(reader["beam_a"])),
            ToBool(reader["beam_rainbow"]));
    }

    private IDbConnection OpenConnection()
    {
        var connection = _core.Database.GetConnection(_utilsConfig.DatabaseConnection);
        connection.Open();
        return connection;
    }

    private static void AddSettingsParameters(IDbCommand command, ulong steamId, WardenVisualSettings settings)
    {
        AddParameter(command, "@steam_id", steamId.ToString());
        AddColorParameters(command, "laser", settings.LaserColor);
        AddParameter(command, "@laser_rainbow", settings.LaserRainbow ? 1 : 0);
        AddColorParameters(command, "beam", settings.BeamColor);
        AddParameter(command, "@beam_rainbow", settings.BeamRainbow ? 1 : 0);
    }

    private static void AddColorParameters(IDbCommand command, string prefix, Color color)
    {
        AddParameter(command, $"@{prefix}_r", color.R);
        AddParameter(command, $"@{prefix}_g", color.G);
        AddParameter(command, $"@{prefix}_b", color.B);
        AddParameter(command, $"@{prefix}_a", color.A);
    }

    private static void AddParameter(IDbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static byte ToByte(object value)
    {
        return Convert.ToByte(value);
    }

    private void EnsureColumn(IDbConnection connection, string columnName, string definition)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"ALTER TABLE {TableName} ADD COLUMN {columnName} {definition}";
            command.ExecuteNonQuery();
        }
        catch
        {
            // Existing databases may already have the column. Keep startup portable across sqlite/mysql/postgres.
        }
    }

    private static bool ToBool(object value)
    {
        return Convert.ToInt32(value) != 0;
    }

    public sealed record WardenVisualSettings(Color LaserColor, bool LaserRainbow, Color BeamColor, bool BeamRainbow)
    {
        public static WardenVisualSettings Default { get; } = new(DefaultLaserColor, false, DefaultBeamColor, false);
    }
}
