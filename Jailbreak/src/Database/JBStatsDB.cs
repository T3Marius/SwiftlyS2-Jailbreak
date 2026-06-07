using System.Data;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;

namespace Jailbreak;

public sealed class JBStatsDB
{
    private const string TableName = "jb_player_stats";

    private readonly ISwiftlyCore _core;
    private readonly UtilsConfig _utilsConfig;
    private readonly Dictionary<ulong, JBStatsRecord> _cache = [];
    private readonly object _lock = new();

    public JBStatsDB(ISwiftlyCore core, IOptions<UtilsConfig> utilsConfig)
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
                player_name VARCHAR(128) NOT NULL DEFAULT '',
                lr_wins INTEGER NOT NULL DEFAULT 0,
                lr_losses INTEGER NOT NULL DEFAULT 0,
                sd_wins INTEGER NOT NULL DEFAULT 0,
                sd_losses INTEGER NOT NULL DEFAULT 0
            )
            """;

        command.ExecuteNonQuery();
        EnsureColumn(connection, "sd_wins", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "sd_losses", "INTEGER NOT NULL DEFAULT 0");
    }

    public JBStatsRecord GetPlayerStats(ulong steamId, string playerName = "")
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(steamId, out var cached))
                return cached;
        }

        var stats = LoadPlayerStats(steamId, playerName);

        lock (_lock)
        {
            _cache[steamId] = stats;
        }

        return stats;
    }

    public IReadOnlyList<JBStatsRecord> GetTopLastRequestPlayers(int limit)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT steam_id,
                   player_name,
                   lr_wins,
                   lr_losses,
                   sd_wins,
                   sd_losses
            FROM {TableName}
            ORDER BY lr_wins DESC,
                     lr_losses ASC,
                     player_name ASC
            LIMIT @limit
            """;

        AddParameter(command, "@limit", Math.Max(1, limit));

        using var reader = command.ExecuteReader();
        var records = new List<JBStatsRecord>();
        while (reader.Read())
        {
            records.Add(ReadRecord(reader));
        }

        return records;
    }

    public JBStatsRecord AddLastRequestWin(ulong steamId, string playerName)
    {
        var current = GetPlayerStats(steamId, playerName);
        var updated = current with
        {
            PlayerName = playerName,
            LastRequestWins = current.LastRequestWins + 1
        };

        SavePlayerStats(updated);
        return updated;
    }

    public JBStatsRecord AddLastRequestLoss(ulong steamId, string playerName)
    {
        var current = GetPlayerStats(steamId, playerName);
        var updated = current with
        {
            PlayerName = playerName,
            LastRequestLosses = current.LastRequestLosses + 1
        };

        SavePlayerStats(updated);
        return updated;
    }

    public IReadOnlyList<JBStatsRecord> GetTopSpecialDayPlayers(int limit)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT steam_id,
                   player_name,
                   lr_wins,
                   lr_losses,
                   sd_wins,
                   sd_losses
            FROM {TableName}
            WHERE sd_wins > 0 OR sd_losses > 0
            ORDER BY sd_wins DESC,
                     sd_losses ASC,
                     player_name ASC
            LIMIT @limit
            """;

        AddParameter(command, "@limit", Math.Max(1, limit));

        using var reader = command.ExecuteReader();
        var records = new List<JBStatsRecord>();
        while (reader.Read())
        {
            records.Add(ReadRecord(reader));
        }

        return records;
    }

    public JBStatsRecord AddSpecialDayWin(ulong steamId, string playerName)
    {
        var current = GetPlayerStats(steamId, playerName);
        var updated = current with
        {
            PlayerName = playerName,
            SpecialDayWins = current.SpecialDayWins + 1
        };

        SavePlayerStats(updated);
        return updated;
    }

    public JBStatsRecord AddSpecialDayLoss(ulong steamId, string playerName)
    {
        var current = GetPlayerStats(steamId, playerName);
        var updated = current with
        {
            PlayerName = playerName,
            SpecialDayLosses = current.SpecialDayLosses + 1
        };

        SavePlayerStats(updated);
        return updated;
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

    private JBStatsRecord LoadPlayerStats(ulong steamId, string playerName)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT steam_id,
                   player_name,
                   lr_wins,
                   lr_losses,
                   sd_wins,
                   sd_losses
            FROM {TableName}
            WHERE steam_id = @steam_id
            """;

        AddParameter(command, "@steam_id", steamId.ToString());

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return new JBStatsRecord(steamId, playerName, 0, 0, 0, 0);

        return ReadRecord(reader);
    }

    private void SavePlayerStats(JBStatsRecord stats)
    {
        using var connection = OpenConnection();
        using var update = connection.CreateCommand();

        update.CommandText = $"""
            UPDATE {TableName}
            SET player_name = @player_name,
                lr_wins = @lr_wins,
                lr_losses = @lr_losses,
                sd_wins = @sd_wins,
                sd_losses = @sd_losses
            WHERE steam_id = @steam_id
            """;

        AddRecordParameters(update, stats);
        var affectedRows = update.ExecuteNonQuery();

        if (affectedRows == 0)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = $"""
                INSERT INTO {TableName} (
                    steam_id,
                    player_name,
                    lr_wins,
                    lr_losses,
                    sd_wins,
                    sd_losses
                ) VALUES (
                    @steam_id,
                    @player_name,
                    @lr_wins,
                    @lr_losses,
                    @sd_wins,
                    @sd_losses
                )
                """;

            AddRecordParameters(insert, stats);
            insert.ExecuteNonQuery();
        }

        lock (_lock)
        {
            _cache[stats.SteamId] = stats;
        }
    }

    private IDbConnection OpenConnection()
    {
        var connection = _core.Database.GetConnection(_utilsConfig.DatabaseConnection);
        connection.Open();
        return connection;
    }

    private static JBStatsRecord ReadRecord(IDataRecord reader)
    {
        return new JBStatsRecord(
            ulong.Parse(reader["steam_id"].ToString() ?? "0"),
            reader["player_name"].ToString() ?? "",
            Convert.ToInt32(reader["lr_wins"]),
            Convert.ToInt32(reader["lr_losses"]),
            Convert.ToInt32(reader["sd_wins"]),
            Convert.ToInt32(reader["sd_losses"]));
    }

    private static void AddRecordParameters(IDbCommand command, JBStatsRecord stats)
    {
        AddParameter(command, "@steam_id", stats.SteamId.ToString());
        AddParameter(command, "@player_name", stats.PlayerName);
        AddParameter(command, "@lr_wins", stats.LastRequestWins);
        AddParameter(command, "@lr_losses", stats.LastRequestLosses);
        AddParameter(command, "@sd_wins", stats.SpecialDayWins);
        AddParameter(command, "@sd_losses", stats.SpecialDayLosses);
    }

    private static void AddParameter(IDbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
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
            // Existing databases may already have the column.
        }
    }

    public sealed record JBStatsRecord(
        ulong SteamId,
        string PlayerName,
        int LastRequestWins,
        int LastRequestLosses,
        int SpecialDayWins,
        int SpecialDayLosses);
}
