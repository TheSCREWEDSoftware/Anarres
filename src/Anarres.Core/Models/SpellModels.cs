using Anarres.Core.Dbc;

namespace Anarres.Core.Models;

// What a spell can be shown with: its visuals in the client, and entries the world database turns into models
public enum SpellModelKind
{
    Visual,
    Shapeshift,
    Creature,
    GameObject,
    Item
}

public sealed record SpellModelRequest(SpellModelKind Kind, uint Id, string Label);

// The models a spell brings: visuals, summons, transforms, forms, mounts, objects and items
public static class SpellModels
{
    private const int EFFECTS = 3;
    private const uint EFFECT_CREATE_ITEM = 24;
    private const uint EFFECT_SUMMON = 28;
    private const uint EFFECT_TRANS_DOOR = 50;
    private const uint EFFECT_SUMMON_PET = 56;
    private const uint EFFECT_SUMMON_OBJECT_WILD = 76;
    private const uint EFFECT_SUMMON_OBJECT_SLOT1 = 104;
    private const uint EFFECT_SUMMON_OBJECT_SLOT4 = 107;
    private const uint EFFECT_APPLY_AURA = 6;
    private const uint EFFECT_APPLY_AREA_AURA_PARTY = 35;
    private const uint AURA_SHAPESHIFT = 36;
    private const uint AURA_TRANSFORM = 56;
    private const uint AURA_MOUNTED = 78;

    public static List<SpellModelRequest> Plan(DbcTable spells, int row)
    {
        List<SpellModelRequest> found = new List<SpellModelRequest>();
        for (int visual = 1; visual <= 2; ++visual)
        {
            uint id = Raw(spells, row, "SpellVisualID_" + visual);
            if (id != 0)
                found.Add(new SpellModelRequest(SpellModelKind.Visual, id, "Visual " + id));
        }

        for (int effect = 1; effect <= EFFECTS; ++effect)
        {
            uint type = Raw(spells, row, "Effect_" + effect);
            uint aura = Raw(spells, row, "EffectAura_" + effect);
            uint misc = Raw(spells, row, "EffectMiscValue_" + effect);
            string where = "Effect " + effect + ": ";
            bool appliesAura = type == EFFECT_APPLY_AURA || type == EFFECT_APPLY_AREA_AURA_PARTY || aura != 0;
            if (type is EFFECT_SUMMON or EFFECT_SUMMON_PET && misc != 0)
                found.Add(new SpellModelRequest(SpellModelKind.Creature, misc, where + "summons creature " + misc));
            else if ((type is EFFECT_TRANS_DOOR or EFFECT_SUMMON_OBJECT_WILD ||
                      type is >= EFFECT_SUMMON_OBJECT_SLOT1 and <= EFFECT_SUMMON_OBJECT_SLOT4) && misc != 0)
                found.Add(new SpellModelRequest(SpellModelKind.GameObject, misc,
                                                where + "summons game object " + misc));
            else if (type == EFFECT_CREATE_ITEM && Raw(spells, row, "EffectItemType_" + effect) is var item &&
                     item != 0)
                found.Add(new SpellModelRequest(SpellModelKind.Item, item, where + "creates item " + item));
            else if (appliesAura && aura == AURA_SHAPESHIFT && misc != 0)
                found.Add(new SpellModelRequest(SpellModelKind.Shapeshift, misc, where + "shapeshift form " + misc));
            else if (appliesAura && aura is AURA_TRANSFORM or AURA_MOUNTED && misc != 0)
                found.Add(new SpellModelRequest(SpellModelKind.Creature, misc,
                                                where + (aura == AURA_MOUNTED ? "mount" : "transforms into") +
                                                " creature " + misc));
        }

        for (int totem = 1; totem <= 2; ++totem)
        {
            uint item = Raw(spells, row, "Totem_" + totem);
            if (item != 0)
                found.Add(new SpellModelRequest(SpellModelKind.Item, item, "Needs item " + item));
        }

        return found;
    }

    private static uint Raw(DbcTable table, int row, string column)
    {
        int index = table.Definition.IndexOf(column);
        return index >= 0 ? table.GetRaw(row, index) : 0;
    }
}
