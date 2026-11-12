using Anarres.Core.Dbc;

namespace Anarres.Core.Models;

// A model to show: where it is in the client, the textures the game would fill in, and what it's for
public sealed record ModelChoice(string Label, string Path, IReadOnlyDictionary<uint, string> Textures,
                                 float Scale = 1f)
{
    public static IReadOnlyDictionary<uint, string> NoTextures { get; } = new Dictionary<uint, string>();
}

// Finds the models behind spell visuals, creature, game object and item displays, from the client's tables
public sealed class ModelSources
{
    public const string SPELL_VISUAL = "SpellVisual.dbc";
    public const string VISUAL_KIT = "SpellVisualKit.dbc";
    public const string EFFECT_NAME = "SpellVisualEffectName.dbc";
    public const string CREATURE_DISPLAY = "CreatureDisplayInfo.dbc";
    public const string CREATURE_MODEL = "CreatureModelData.dbc";
    public const string OBJECT_DISPLAY = "GameObjectDisplayInfo.dbc";
    public const string ITEM_DISPLAY = "ItemDisplayInfo.dbc";
    public const string SHAPESHIFT = "SpellShapeshiftForm.dbc";

    private const string ITEM_FOLDER = "Item\\ObjectComponents\\";
    private const string TEXTURE_EXTENSION = ".blp";
    private const int SHAPESHIFT_DISPLAYS = 4;

    // The kits of a visual, in the order a cast plays them
    private static readonly (string Column, string Label)[] Kits =
    {
        ("PrecastKit", "Precast"), ("CastKit", "Cast"), ("ChannelKit", "Channel"), ("StateKit", "State"),
        ("StateDoneKit", "State done"), ("ImpactKit", "Impact"), ("CasterImpactKit", "Caster impact"),
        ("TargetImpactKit", "Target impact"), ("InstantAreaKit", "Instant area"), ("ImpactAreaKit", "Impact area"),
        ("PersistentAreaKit", "Persistent area"), ("MissileTargetingKit", "Missile targeting")
    };

    // Where on the caster or target a kit puts each of its effects
    private static readonly (string Column, string Label)[] KitEffects =
    {
        ("HeadEffect", "head"), ("ChestEffect", "chest"), ("BaseEffect", "base"), ("LeftHandEffect", "left hand"),
        ("RightHandEffect", "right hand"), ("BreathEffect", "breath"), ("LeftWeaponEffect", "left weapon"),
        ("RightWeaponEffect", "right weapon"), ("SpecialEffect_1", "special 1"), ("SpecialEffect_2", "special 2"),
        ("SpecialEffect_3", "special 3")
    };

    // An item's slot decides the folder its model is in; the rest are tried in turn
    private static readonly string[] ItemFolders =
    {
        "Weapon", "Shield", "Quiver", "Ammo", "Cape", "Shoulder", "Head", "Pouch"
    };

    private readonly Func<string, DbcTable?> _tables;
    private readonly Func<string, bool> _exists;

    // exists says whether the client has a file, for finding an item's folder
    public ModelSources(Func<string, DbcTable?> tables, Func<string, bool> exists)
    {
        _tables = tables;
        _exists = exists;
    }

    // Every model a spell visual plays: its missile, then each kit's effects, one entry per file
    public List<ModelChoice> ForSpellVisual(uint visual)
    {
        List<ModelChoice> found = new List<ModelChoice>();
        DbcTable? visuals = _tables(SPELL_VISUAL);
        int row = visuals?.FindRow(visual) ?? -1;
        if (visuals == null || row < 0)
            return found;

        uint missile = Raw(visuals, row, "MissileModel");
        if (missile != 0 && EffectFile(missile) is { } missilePath)
            Add(found, "Missile", missilePath);
        DbcTable? kits = _tables(VISUAL_KIT);
        if (kits == null)
            return found;
        foreach ((string column, string label) in Kits)
        {
            int kit = kits.FindRow(Raw(visuals, row, column));
            if (Raw(visuals, row, column) == 0 || kit < 0)
                continue;
            foreach ((string effectColumn, string place) in KitEffects)
            {
                uint effect = Raw(kits, kit, effectColumn);
                if (effect != 0 && EffectFile(effect) is { } path)
                    Add(found, label + " kit, " + place, path);
            }
        }

        return found;
    }

    // A creature display: its model, and its skins from TextureVariation in the model's folder
    public ModelChoice? ForCreatureDisplay(uint display, string label = "Creature display")
    {
        DbcTable? displays = _tables(CREATURE_DISPLAY);
        DbcTable? models = _tables(CREATURE_MODEL);
        int row = displays?.FindRow(display) ?? -1;
        if (displays == null || models == null || row < 0)
            return null;
        int model = models.FindRow(Raw(displays, row, "ModelID"));
        if (model < 0)
            return null;
        string path = M2Model.ModelFile(Text(models, model, "ModelName"));
        if (path.Length == 0)
            return null;
        string folder = Folder(path);
        Dictionary<uint, string> textures = new Dictionary<uint, string>();
        uint[] slots = { M2Texture.MONSTER_SKIN_1, M2Texture.MONSTER_SKIN_2, M2Texture.MONSTER_SKIN_3 };
        for (int i = 0; i < slots.Length; ++i)
        {
            string skin = Text(displays, row, "TextureVariation_" + (i + 1));
            if (skin.Length > 0)
                textures[slots[i]] = folder + skin + TEXTURE_EXTENSION;
        }

        float scale = Float(displays, row, "CreatureModelScale");
        return new ModelChoice(label + " " + display, path, textures, scale > 0 ? scale : 1f);
    }

    public ModelChoice? ForGameObjectDisplay(uint display)
    {
        DbcTable? displays = _tables(OBJECT_DISPLAY);
        int row = displays?.FindRow(display) ?? -1;
        if (displays == null || row < 0)
            return null;
        string path = M2Model.ModelFile(Text(displays, row, "ModelName"));
        return path.EndsWith(".m2", StringComparison.OrdinalIgnoreCase)
                   ? new ModelChoice("Game object display " + display, path, ModelChoice.NoTextures)
                   : null;
    }

    // An item display's first model, found in whichever ObjectComponents folder has it, with its texture
    public ModelChoice? ForItemDisplay(uint display)
    {
        DbcTable? displays = _tables(ITEM_DISPLAY);
        int row = displays?.FindRow(display) ?? -1;
        if (displays == null || row < 0)
            return null;
        string name = M2Model.ModelFile(Text(displays, row, "ModelName_1"));
        if (name.Length == 0)
            return null;
        string texture = Text(displays, row, "ModelTexture_1");
        foreach (string folder in ItemFolders)
        {
            string path = ITEM_FOLDER + folder + "\\" + name;
            if (!_exists(path))
                continue;
            Dictionary<uint, string> textures = new Dictionary<uint, string>();
            if (texture.Length > 0)
                textures[M2Texture.ITEM_SKIN] = ITEM_FOLDER + folder + "\\" + texture + TEXTURE_EXTENSION;
            return new ModelChoice("Item display " + display, path, textures);
        }

        return null;
    }

    // The creatures a shapeshift form turns into
    public List<ModelChoice> ForShapeshift(uint form)
    {
        List<ModelChoice> found = new List<ModelChoice>();
        DbcTable? forms = _tables(SHAPESHIFT);
        int row = forms?.FindRow(form) ?? -1;
        if (forms == null || row < 0)
            return found;
        for (int i = 1; i <= SHAPESHIFT_DISPLAYS; ++i)
        {
            uint display = Raw(forms, row, "CreatureDisplayID_" + i);
            if (display != 0 && ForCreatureDisplay(display, "Form " + form + ", display") is { } choice)
                found.Add(choice);
        }

        return found;
    }

    // An item display's inventory icon, as a path in the client
    public string? ItemIcon(uint display)
    {
        DbcTable? displays = _tables(ITEM_DISPLAY);
        int row = displays?.FindRow(display) ?? -1;
        if (displays == null || row < 0)
            return null;
        string icon = Text(displays, row, "InventoryIcon_1");
        return icon.Length > 0 ? "Interface\\Icons\\" + icon + TEXTURE_EXTENSION : null;
    }

    public string? EffectFile(uint effect)
    {
        DbcTable? names = _tables(EFFECT_NAME);
        int row = names?.FindRow(effect) ?? -1;
        if (names == null || row < 0)
            return null;
        string path = M2Model.ModelFile(Text(names, row, "FileName"));
        return path.Length > 0 ? path : null;
    }

    private static void Add(List<ModelChoice> found, string label, string path)
    {
        int existing = found.FindIndex(choice => choice.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
            found[existing] = found[existing] with { Label = found[existing].Label + "; " + label };
        else
            found.Add(new ModelChoice(label, path, ModelChoice.NoTextures));
    }

    private static string Folder(string path)
    {
        int slash = path.LastIndexOf('\\');
        return slash >= 0 ? path[..(slash + 1)] : string.Empty;
    }

    private static uint Raw(DbcTable table, int row, string column)
    {
        int index = table.Definition.IndexOf(column);
        return index >= 0 ? table.GetRaw(row, index) : 0;
    }

    private static float Float(DbcTable table, int row, string column)
    {
        int index = table.Definition.IndexOf(column);
        return index >= 0 ? BitConverter.UInt32BitsToSingle(table.GetRaw(row, index)) : 0f;
    }

    private static string Text(DbcTable table, int row, string column)
    {
        int index = table.Definition.IndexOf(column);
        return index >= 0 ? table.GetString(row, index) : string.Empty;
    }
}
