using System.Numerics;
using Anarres.Core.Dbc;
using Anarres.Core.Models;

namespace Anarres.Core.Tests;

public static class ModelTests
{
    private const int SIZE = 64;

    // A 2x2 uncompressed BGRA texture of one colour
    private static byte[] Texture(byte red, byte green, byte blue)
    {
        byte[] pixels = new byte[16];
        for (int i = 0; i < 4; ++i)
        {
            pixels[i * 4] = blue;
            pixels[i * 4 + 1] = green;
            pixels[i * 4 + 2] = red;
            pixels[i * 4 + 3] = 255;
        }

        return BlpTests.Header(3, 8, 0, 2, 2, pixels);
    }

    private static ModelInstance Load(M2Builder builder)
    {
        Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Spells\\Test.m2"] = builder.Model(),
            ["Spells\\Test00.skin"] = M2Builder.Skin(),
            ["test.blp"] = Texture(255, 0, 0)
        };
        return ModelLoader.Load(name => files.GetValueOrDefault(name),
                                new ModelChoice("Test", "Spells\\Test.m2", ModelChoice.NoTextures));
    }

    public static void ReadsModels()
    {
        M2Model model = M2Model.Read(new M2Builder().Model());
        Assert.Equal("Test", model.Name, "name");
        Assert.Equal(4, model.Vertices.Length, "vertices");
        Assert.Equal("test.blp", model.Textures[0].FileName, "texture");
        Assert.True(model.Sequences[0].IsInline, "the animation is in the file");
        Assert.Equal(2, model.Bones[0].Rotation.Keys(0).Values.Length, "rotation keys");
        M2Skin skin = M2Skin.Read(M2Builder.Skin(), model.Vertices.Length);
        Assert.Equal("0,1,2,0,2,3", string.Join(",", skin.Indices), "triangles");
        Assert.Equal(1, skin.Batches.Length, "one batch");
        Assert.Throws<InvalidDataException>(() => M2Model.Read(new byte[16]), "not a model");
    }

    public static void NamesModelFiles()
    {
        Assert.Equal("Spells\\Fireball_Missile.m2", M2Model.ModelFile("Spells/Fireball_Missile.mdx"), "mdx to m2");
        Assert.Equal("Creature\\Wolf\\Wolf00.skin", M2Model.SkinPath("Creature\\Wolf\\Wolf.m2"), "skin");
        Assert.Equal("Creature\\Wolf\\Wolf0004-01.anim", M2Model.AnimPath("Creature\\Wolf\\Wolf.m2", 4, 1),
                     "animation file");
    }

    public static void AnimatesBones()
    {
        ModelInstance instance = Load(new M2Builder());
        instance.Advance(0.25f, new Vector3(10, 0, 0), Vector3.UnitZ);
        // A quarter turn after 250 ms of a half turn over 500 ms: +Y swings round to -X
        Vector3 moved = Vector3.Transform(Vector3.UnitY, instance.BoneMatrices[0]);
        Assert.True(Vector3.Distance(moved, -Vector3.UnitX) < 0.01f, "bone turned: " + moved);
    }

    public static void RendersTexturedModels()
    {
        ModelInstance instance = Load(new M2Builder());
        ModelCamera camera = new ModelCamera { Yaw = 0f, Pitch = 0f };
        camera.Fit(new Vector3(-1), new Vector3(1));
        uint[] pixels = new uint[SIZE * SIZE];
        ModelRenderer renderer = new ModelRenderer();
        renderer.Render(instance, camera, SIZE, SIZE, pixels);
        uint centre = pixels[SIZE / 2 * SIZE + SIZE / 2];
        Assert.Equal(0xFFFF0000u, centre, "the red square is in the middle");
        Assert.True(pixels[0] != 0xFFFF0000u, "the corner is background");
        Assert.Equal(2, renderer.TrianglesDrawn, "two triangles");
    }

    public static void EmitsParticles()
    {
        ModelInstance instance = Load(new M2Builder { Particles = true });
        for (int frame = 0; frame < 10; ++frame)
            instance.Advance(0.05f, new Vector3(10, 0, 0), Vector3.UnitZ);
        IReadOnlyList<Particle> live = instance.Particles[0].Live;
        Assert.True(live.Count is >= 45 and <= 55, "about 100 a second for half a second: " + live.Count);
        Assert.True(live.All(particle => particle.Position.Z >= 0f), "they go up");

        ModelCamera camera = new ModelCamera { Yaw = 0f, Pitch = 0f };
        camera.Fit(new Vector3(-1), new Vector3(1));
        ModelRenderer renderer = new ModelRenderer();
        renderer.Render(instance, camera, SIZE, SIZE, new uint[SIZE * SIZE]);
        Assert.Equal(live.Count, renderer.ParticlesDrawn, "every particle drawn");
    }

    public static void FindsSpellAndCreatureModels()
    {
        DbcDefinitions definitions = DbcDefinitions.Default;
        DbcTable visuals = new DbcTable(definitions.Find(ModelSources.SPELL_VISUAL)!);
        DbcTable kits = new DbcTable(definitions.Find(ModelSources.VISUAL_KIT)!);
        DbcTable names = new DbcTable(definitions.Find(ModelSources.EFFECT_NAME)!);
        DbcTable displays = new DbcTable(definitions.Find(ModelSources.CREATURE_DISPLAY)!);
        DbcTable models = new DbcTable(definitions.Find(ModelSources.CREATURE_MODEL)!);
        int visual = visuals.AddRow(1);
        visuals.SetUInt(visual, visuals.Definition.IndexOf("MissileModel"), 10);
        visuals.SetUInt(visual, visuals.Definition.IndexOf("ImpactKit"), 5);
        int kit = kits.AddRow(5);
        kits.SetUInt(kit, kits.Definition.IndexOf("ChestEffect"), 11);
        kits.SetUInt(kit, kits.Definition.IndexOf("BaseEffect"), 11);
        names.SetString(names.AddRow(10), names.Definition.IndexOf("FileName"), "Spells\\Fireball_Missile.mdx");
        names.SetString(names.AddRow(11), names.Definition.IndexOf("FileName"), "Spells\\Fireball_Impact.mdx");
        int display = displays.AddRow(100);
        displays.SetUInt(display, displays.Definition.IndexOf("ModelID"), 7);
        displays.SetString(display, displays.Definition.IndexOf("TextureVariation_1"), "WolfSkinGrey");
        models.SetString(models.AddRow(7), models.Definition.IndexOf("ModelName"), "Creature\\Wolf\\Wolf.mdx");

        Dictionary<string, DbcTable> tables = new Dictionary<string, DbcTable>
        {
            [ModelSources.SPELL_VISUAL] = visuals,
            [ModelSources.VISUAL_KIT] = kits,
            [ModelSources.EFFECT_NAME] = names,
            [ModelSources.CREATURE_DISPLAY] = displays,
            [ModelSources.CREATURE_MODEL] = models
        };
        ModelSources sources = new ModelSources(file => tables.GetValueOrDefault(file), _ => false);
        List<ModelChoice> found = sources.ForSpellVisual(1);
        Assert.Equal("Spells\\Fireball_Missile.m2|Spells\\Fireball_Impact.m2",
                     string.Join("|", found.Select(choice => choice.Path)), "missile then impact, once each");
        Assert.Equal("Impact kit, chest; Impact kit, base", found[1].Label, "both places named");
        ModelChoice wolf = sources.ForCreatureDisplay(100)!;
        Assert.Equal("Creature\\Wolf\\Wolf.m2", wolf.Path, "creature model");
        Assert.Equal("Creature\\Wolf\\WolfSkinGrey.blp", wolf.Textures[M2Texture.MONSTER_SKIN_1], "its skin");
    }
}
