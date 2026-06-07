using System.Data;
using Jailbreak.Contract;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Helpers;

namespace Jailbreak;

public sealed class GuardGunsDatabase
{
    private const string TableName = "jb_guard_guns";

    private readonly ISwiftlyCore _core;
    private readonly UtilsConfig _utilsConfig;
    private readonly Dictionary<ulong, GuardGunsSettings> _cache = [];
    private readonly object _lock = new();

    public GuardGunsDatabase(ISwiftlyCore core, IOptions<UtilsConfig> utilsConfig)
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
                primary_weapon INTEGER NOT NULL,
                secondary_weapon INTEGER NOT NULL
            )
            """;

        command.ExecuteNonQuery();
    }

    public GuardGunsSettings? GetSettings(ulong steamId)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(steamId, out var cached))
                return cached;
        }

        var settings = LoadSettings(steamId);
        if (settings == null)
            return null;

        lock (_lock)
        {
            _cache[steamId] = settings;
        }

        return settings;
    }

    public void SaveSettings(ulong steamId, ItemDefinitionIndex primaryWeapon, ItemDefinitionIndex secondaryWeapon)
    {
        var settings = new GuardGunsSettings(primaryWeapon, secondaryWeapon);

        using var connection = OpenConnection();
        using var update = connection.CreateCommand();

        update.CommandText = $"""
            UPDATE {TableName}
            SET primary_weapon = @primary_weapon,
                secondary_weapon = @secondary_weapon
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
                    primary_weapon,
                    secondary_weapon
                ) VALUES (
                    @steam_id,
                    @primary_weapon,
                    @secondary_weapon
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

    private GuardGunsSettings? LoadSettings(ulong steamId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();

        command.CommandText = $"""
            SELECT primary_weapon,
                   secondary_weapon
            FROM {TableName}
            WHERE steam_id = @steam_id
            """;

        AddParameter(command, "@steam_id", steamId.ToString());

        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        var primary = (ItemDefinitionIndex)Convert.ToInt32(reader["primary_weapon"]);
        var secondary = (ItemDefinitionIndex)Convert.ToInt32(reader["secondary_weapon"]);

        if (!SpecialDayWeapons.PrimaryWeapons.Contains(primary) || !SpecialDayWeapons.SecondaryWeapons.Contains(secondary))
            return null;

        return new GuardGunsSettings(primary, secondary);
    }

    private IDbConnection OpenConnection()
    {
        var connection = _core.Database.GetConnection(_utilsConfig.DatabaseConnection);
        connection.Open();
        return connection;
    }

    private static void AddSettingsParameters(IDbCommand command, ulong steamId, GuardGunsSettings settings)
    {
        AddParameter(command, "@steam_id", steamId.ToString());
        AddParameter(command, "@primary_weapon", Convert.ToInt32(settings.PrimaryWeapon));
        AddParameter(command, "@secondary_weapon", Convert.ToInt32(settings.SecondaryWeapon));
    }

    private static void AddParameter(IDbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    public sealed record GuardGunsSettings(ItemDefinitionIndex PrimaryWeapon, ItemDefinitionIndex SecondaryWeapon);
}
