using System.Data;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;

namespace Jailbreak;

public sealed class ShopDatabase
{
    private const string OwnedItemsTable = "jb_shop_owned_items";
    private const string EquippedItemsTable = "jb_shop_equipped_items";

    private readonly ISwiftlyCore _core;
    private readonly UtilsConfig _utilsConfig;

    public ShopDatabase(ISwiftlyCore core, IOptions<UtilsConfig> utilsConfig)
    {
        _core = core;
        _utilsConfig = utilsConfig.Value;
    }

    public void Initialize()
    {
        using var connection = OpenConnection();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                CREATE TABLE IF NOT EXISTS {OwnedItemsTable} (
                    steam_id VARCHAR(32) NOT NULL,
                    item_id VARCHAR(128) NOT NULL,
                    purchased_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    PRIMARY KEY (steam_id, item_id)
                )
                """;
            command.ExecuteNonQuery();
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"""
                CREATE TABLE IF NOT EXISTS {EquippedItemsTable} (
                    steam_id VARCHAR(32) NOT NULL,
                    slot_id VARCHAR(128) NOT NULL,
                    item_id VARCHAR(128) NOT NULL,
                    PRIMARY KEY (steam_id, slot_id)
                )
                """;
            command.ExecuteNonQuery();
        }
    }

    public ShopPlayerState LoadPlayerState(ulong steamId)
    {
        using var connection = OpenConnection();
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var equipped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT item_id FROM {OwnedItemsTable} WHERE steam_id = @steam_id";
            AddParameter(command, "@steam_id", steamId.ToString());

            using var reader = command.ExecuteReader();
            while (reader.Read())
                owned.Add(Convert.ToString(reader["item_id"]) ?? "");
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT slot_id, item_id FROM {EquippedItemsTable} WHERE steam_id = @steam_id";
            AddParameter(command, "@steam_id", steamId.ToString());

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var slot = Convert.ToString(reader["slot_id"]);
                var item = Convert.ToString(reader["item_id"]);
                if (!string.IsNullOrWhiteSpace(slot) && !string.IsNullOrWhiteSpace(item))
                    equipped[slot] = item;
            }
        }

        owned.RemoveWhere(string.IsNullOrWhiteSpace);
        return new ShopPlayerState(owned, equipped);
    }

    public void AddOwnedItem(ulong steamId, string itemId)
    {
        using var connection = OpenConnection();

        using var exists = connection.CreateCommand();
        exists.CommandText = $"SELECT COUNT(*) FROM {OwnedItemsTable} WHERE steam_id = @steam_id AND item_id = @item_id";
        AddParameter(exists, "@steam_id", steamId.ToString());
        AddParameter(exists, "@item_id", itemId);
        if (Convert.ToInt32(exists.ExecuteScalar()) > 0)
            return;

        using var insert = connection.CreateCommand();
        insert.CommandText = $"INSERT INTO {OwnedItemsTable} (steam_id, item_id) VALUES (@steam_id, @item_id)";
        AddParameter(insert, "@steam_id", steamId.ToString());
        AddParameter(insert, "@item_id", itemId);
        insert.ExecuteNonQuery();
    }

    public void RemoveOwnedItem(ulong steamId, string itemId)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {OwnedItemsTable} WHERE steam_id = @steam_id AND item_id = @item_id";
        AddParameter(command, "@steam_id", steamId.ToString());
        AddParameter(command, "@item_id", itemId);
        command.ExecuteNonQuery();
    }

    public void SetEquippedItem(ulong steamId, string slot, string itemId)
    {
        using var connection = OpenConnection();

        using var update = connection.CreateCommand();
        update.CommandText = $"""
            UPDATE {EquippedItemsTable}
            SET item_id = @item_id
            WHERE steam_id = @steam_id AND slot_id = @slot_id
            """;
        AddEquipmentParameters(update, steamId, slot, itemId);

        if (update.ExecuteNonQuery() > 0)
            return;

        using var insert = connection.CreateCommand();
        insert.CommandText = $"""
            INSERT INTO {EquippedItemsTable} (steam_id, slot_id, item_id)
            VALUES (@steam_id, @slot_id, @item_id)
            """;
        AddEquipmentParameters(insert, steamId, slot, itemId);
        insert.ExecuteNonQuery();
    }

    public void RemoveEquippedItem(ulong steamId, string slot)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {EquippedItemsTable} WHERE steam_id = @steam_id AND slot_id = @slot_id";
        AddParameter(command, "@steam_id", steamId.ToString());
        AddParameter(command, "@slot_id", slot);
        command.ExecuteNonQuery();
    }

    private IDbConnection OpenConnection()
    {
        var connection = _core.Database.GetConnection(_utilsConfig.DatabaseConnection);
        connection.Open();
        return connection;
    }

    private static void AddEquipmentParameters(IDbCommand command, ulong steamId, string slot, string itemId)
    {
        AddParameter(command, "@steam_id", steamId.ToString());
        AddParameter(command, "@slot_id", slot);
        AddParameter(command, "@item_id", itemId);
    }

    private static void AddParameter(IDbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    public sealed record ShopPlayerState(
        HashSet<string> OwnedItemIds,
        Dictionary<string, string> EquippedItems);
}
