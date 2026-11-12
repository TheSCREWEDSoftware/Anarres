using MySqlConnector;

namespace Anarres.Core.Storage;

// Names of items, creatures and game objects from AzerothCore's world database, for showing beside their IDs
public static class WorldNames
{
    public const string ITEMS = "item_template";
    public const string CREATURES = "creature_template";
    public const string GAME_OBJECTS = "gameobject_template";

    // Every entry and name of one template table
    public static async Task<Dictionary<uint, string>> LoadAsync(MySqlSettings world, string table,
                                                                 CancellationToken cancellation = default)
    {
        Dictionary<uint, string> names = new Dictionary<uint, string>();
        await using MySqlConnection connection = new MySqlConnection(world.ConnectionString());
        await connection.OpenAsync(cancellation);
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT `entry`, `name` FROM `" + table.Replace("`", string.Empty) + "`";
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellation);
        while (await reader.ReadAsync(cancellation))
            names[Convert.ToUInt32(reader.GetValue(0))] = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        return names;
    }

    // Each item's quality (0 poor to 7 heirloom) and display, for colouring its name and finding its icon
    public static async Task<Dictionary<uint, (byte Quality, uint Display)>> ItemDetailsAsync(
        MySqlSettings world, CancellationToken cancellation = default)
    {
        Dictionary<uint, (byte, uint)> details = new Dictionary<uint, (byte, uint)>();
        await using MySqlConnection connection = new MySqlConnection(world.ConnectionString());
        await connection.OpenAsync(cancellation);
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT `entry`, `Quality`, `displayid` FROM `" + ITEMS + "`";
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellation);
        while (await reader.ReadAsync(cancellation))
            details[Convert.ToUInt32(reader.GetValue(0))] = (Convert.ToByte(reader.GetValue(1)),
                                                             Convert.ToUInt32(reader.GetValue(2)));
        return details;
    }

    // The display an entry is drawn with (a creature's first model), and an item's InventoryType; null if missing
    public static async Task<(uint Display, uint Slot)?> DisplayAsync(MySqlSettings world, string table, uint entry,
                                                                     CancellationToken cancellation = default)
    {
        string query = table switch
        {
            CREATURES => "SELECT `CreatureDisplayID`, 0 FROM `creature_template_model` WHERE `CreatureID` = @entry " +
                         "ORDER BY `Idx` LIMIT 1",
            GAME_OBJECTS => "SELECT `displayId`, 0 FROM `gameobject_template` WHERE `entry` = @entry",
            ITEMS => "SELECT `displayid`, `InventoryType` FROM `item_template` WHERE `entry` = @entry",
            _ => throw new ArgumentException("No displays in " + table + ".")
        };
        await using MySqlConnection connection = new MySqlConnection(world.ConnectionString());
        await connection.OpenAsync(cancellation);
        await using MySqlCommand command = connection.CreateCommand();
        command.CommandText = query;
        command.Parameters.AddWithValue("@entry", entry);
        await using MySqlDataReader reader = await command.ExecuteReaderAsync(cancellation);
        if (!await reader.ReadAsync(cancellation))
            return null;
        return (Convert.ToUInt32(reader.GetValue(0)), Convert.ToUInt32(reader.GetValue(1)));
    }
}
