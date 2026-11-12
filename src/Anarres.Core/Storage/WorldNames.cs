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
}
