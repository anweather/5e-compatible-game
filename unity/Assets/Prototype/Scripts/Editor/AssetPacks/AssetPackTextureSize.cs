using System;
using System.Collections.Generic;
using UnityEditor;

// Caps the Asset Store packs' textures at 1024 px when they import. The packs
// ship most maps at 2048 (terrain layers, rocks, trees, the character, even
// particle smoke), which was about 400 MB of a 790 MB build, though the game
// never shows them close enough to need it. The packs aren't in git, so their
// own import settings can't be changed for every machine; this does it on
// import instead. Our own textures under Assets/Prototype keep their settings.
// Raising Version makes every machine reimport the textures.
public class AssetPackTextureSize : AssetPostprocessor
{
    internal const int MaxSize = 1024;
    const uint Version = 1;

    static List<string> folders;

    public override uint GetVersion() => Version;

    void OnPreprocessTexture()
    {
        if (!InPack(assetPath)) return;
        var importer = (TextureImporter)assetImporter;
        if (importer.maxTextureSize > MaxSize) importer.maxTextureSize = MaxSize;
        // A platform override (the packs set one for Standalone) wins over the default size.
        var standalone = importer.GetPlatformTextureSettings("Standalone");
        if (standalone.overridden && standalone.maxTextureSize > MaxSize)
        {
            standalone.maxTextureSize = MaxSize;
            importer.SetPlatformTextureSettings(standalone);
        }
    }

    internal static bool InPack(string path)
    {
        folders ??= AssetPackRestore.Folders();
        return folders.Exists(f => path.StartsWith(f + "/", StringComparison.Ordinal));
    }
}
