namespace Anarres.Core.Models;

// Loads a model, its skin, animations and textures from the client (its patches and custom files included)
public static class ModelLoader
{
    // read gives a file from the client by its path, or null when no archive has it
    public static ModelInstance Load(Func<string, byte[]?> read, ModelChoice choice, List<string>? missing = null)
    {
        byte[] data = read(choice.Path) ??
                      throw new FileNotFoundException("The client has no " + choice.Path + ".");
        M2Model model = M2Model.Read(data, (id, variation) => read(M2Model.AnimPath(choice.Path, id, variation)));
        byte[] skinData = read(M2Model.SkinPath(choice.Path)) ??
                          throw new FileNotFoundException("The client has no " + M2Model.SkinPath(choice.Path) +
                                                          ".");
        M2Skin skin = M2Skin.Read(skinData, model.Vertices.Length);

        Dictionary<string, ModelTexture> loaded =
            new Dictionary<string, ModelTexture>(StringComparer.OrdinalIgnoreCase);
        ModelTexture[] textures = new ModelTexture[model.Textures.Length];
        for (int i = 0; i < textures.Length; ++i)
        {
            M2Texture texture = model.Textures[i];
            string file = texture.Type == M2Texture.HARDCODED
                              ? texture.FileName
                              : choice.Textures.GetValueOrDefault(texture.Type, string.Empty);
            textures[i] = ModelTexture.White;
            if (file.Length == 0)
                continue;
            string key = file + "|" + texture.Flags;
            if (loaded.TryGetValue(key, out ModelTexture? known))
            {
                textures[i] = known;
                continue;
            }

            byte[]? bytes = read(file);
            try
            {
                if (bytes != null)
                    textures[i] = loaded[key] = ModelTexture.FromBlp(bytes, texture.Flags);
                else
                    missing?.Add(file);
            }
            catch (InvalidDataException)
            {
                missing?.Add(file);
            }
        }

        return new ModelInstance(model, skin, textures);
    }
}
