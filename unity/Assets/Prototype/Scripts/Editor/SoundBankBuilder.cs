using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Prototype > Build Sound Bank (also run by Build Chemistry Yard): masters
// every sound the game plays from its sources and saves the results, with the
// SoundBank that lists them, in Generated/Resources (out of git: some come
// from an Asset Store pack). The recipe below is the mix: for each sound, its
// sources (CC0 recordings in Audio/CC0, the free pack's when it's restored,
// and procedural ones), how long it may run, and how loud it should be, as a
// loudness target in dB (SoundMastering.LoudnessDb). A missing source is
// skipped; a sound with none at all falls back to ProceduralSounds in game.
public static class SoundBankBuilder
{
    const string OutFolder = "Assets/Prototype/Generated/Resources/Sounds";
    const string BankPath = "Assets/Prototype/Generated/Resources/" + SoundBank.ResourceName + ".asset";
    const string CC0 = "Assets/Prototype/Audio/CC0";
    const string Pack = PackPaths.FreeSounds;
    const int Rate = 44100;

    sealed class Recipe
    {
        public string Sound;
        public float TargetDb, MaxSeconds;
        public bool Loop;
        public string[] Files = Array.Empty<string>();
        public Func<float[]>[] Procedural = Array.Empty<Func<float[]>>();
    }

    // Loudness targets, loudest first: the mix. Explosions and thunder dominate; impacts and
    // spells land in the middle; casts and the fire's burning bed sit underneath.
    static readonly Recipe[] Recipes =
    {
        new() { Sound = SoundIds.Explosion, TargetDb = -12f, MaxSeconds = 3f,
            Files = new[] { Pack + "/Explosion 1.wav", Pack + "/Explosion 2.wav", Pack + "/Explosion 8.wav", CC0 + "/rubberduck/explosion.ogg", CC0 + "/rubberduck/bang_01.ogg", CC0 + "/rubberduck/bang_03.ogg" } },
        new() { Sound = SoundIds.Thunder, TargetDb = -13f, MaxSeconds = 2.5f,
            Files = new[] { Pack + "/Cannon impact 9.wav", CC0 + "/rubberduck/cannon_01.ogg", CC0 + "/rubberduck/cannon_02.ogg", CC0 + "/rubberduck/cannon_04.ogg" } },
        new() { Sound = SoundIds.Fire, TargetDb = -16f, MaxSeconds = 1.3f,
            Files = new[] { Pack + "/Dragon Spit Fire 1.wav" },
            Procedural = new Func<float[]>[] { () => ProceduralFire.Burst(1), () => ProceduralFire.Burst(2), () => ProceduralFire.Burst(3) } },
        new() { Sound = SoundIds.Zap, TargetDb = -16f, MaxSeconds = 1.0f,
            Files = new[] { Pack + "/Magic Spell_Electricity Spell_1.wav" },
            Procedural = new Func<float[]>[] { () => ProceduralFire.Zap(1), () => ProceduralFire.Zap(2), () => ProceduralFire.Zap(3) } },
        new() { Sound = SoundIds.Earth, TargetDb = -16f, MaxSeconds = 2.2f,
            Files = new[] { Pack + "/Magic Spell - Turn to stone.wav", Pack + "/Earthquake 3 - Big.wav" } },
        new() { Sound = SoundIds.Break, TargetDb = -17f, MaxSeconds = 0.6f,
            Files = new[] { CC0 + "/kenney/impactWood_heavy_000.ogg", CC0 + "/kenney/impactWood_heavy_001.ogg", CC0 + "/kenney/impactWood_heavy_002.ogg", CC0 + "/kenney/impactWood_heavy_003.ogg", CC0 + "/kenney/impactWood_heavy_004.ogg", CC0 + "/rubberduck/wooden_01.ogg", CC0 + "/rubberduck/wooden_02.ogg", CC0 + "/rubberduck/wooden_03.ogg" } },
        new() { Sound = SoundIds.Frost, TargetDb = -18f, MaxSeconds = 1.5f,
            Files = new[] { CC0 + "/bart/ice.wav", CC0 + "/bart/coldsnap.wav" } },
        new() { Sound = SoundIds.Splash, TargetDb = -18f, MaxSeconds = 1.0f,
            Files = new[] { CC0 + "/rubberduck/splash_01.ogg", CC0 + "/rubberduck/splash_02.ogg", CC0 + "/rubberduck/plop_02.ogg" } },
        new() { Sound = SoundIds.Gust, TargetDb = -19f, MaxSeconds = 1.2f,
            Procedural = new Func<float[]>[] { () => Samples(ProceduralSounds.Gust) } },
        new() { Sound = SoundIds.Blink, TargetDb = -19f, MaxSeconds = 1.6f,
            Files = new[] { Pack + "/Magic Spell_Short Reverse_1.wav" } },
        new() { Sound = SoundIds.Arcane, TargetDb = -20f, MaxSeconds = 1.6f,
            Files = new[] { CC0 + "/jaggedstone/magical_1_0.ogg", CC0 + "/jaggedstone/magical_2.ogg", CC0 + "/jaggedstone/magical_3.ogg", CC0 + "/jaggedstone/magical_4.ogg", CC0 + "/jaggedstone/magical_5.ogg", CC0 + "/jaggedstone/magical_7_0.ogg" } },
        new() { Sound = SoundIds.Grease, TargetDb = -20f, MaxSeconds = 0.6f,
            Procedural = new Func<float[]>[] { () => Samples(ProceduralSounds.Squelch) } },
        new() { Sound = SoundIds.Cast, TargetDb = -22f, MaxSeconds = 1.0f,
            Files = new[] { Pack + "/Magic Spell_Simple Swoosh_6.wav" } },
        new() { Sound = SoundIds.FireLoop, TargetDb = -26f, MaxSeconds = 30f, Loop = true,
            Procedural = new Func<float[]>[] { () => ProceduralFire.Loop(1) } },
    };

    [MenuItem("Prototype/Build Sound Bank")]
    public static SoundBank Build()
    {
        if (AssetDatabase.IsValidFolder(OutFolder)) AssetDatabase.DeleteAsset(OutFolder);
        Directory.CreateDirectory(OutFolder);
        var written = new List<(string sound, string path)>();
        foreach (var recipe in Recipes) written.AddRange(Master(recipe));
        AssetDatabase.Refresh();

        var bank = AssetDatabase.LoadAssetAtPath<SoundBank>(BankPath);
        if (bank == null)
        {
            bank = ScriptableObject.CreateInstance<SoundBank>();
            AssetDatabase.CreateAsset(bank, BankPath);
        }
        bank.entries = new List<SoundBank.Entry>();
        foreach (var recipe in Recipes)
        {
            var clips = written.FindAll(w => w.sound == recipe.Sound).ConvertAll(w => Import(w.path));
            bank.entries.Add(new SoundBank.Entry { sound = recipe.Sound, clips = clips.ToArray() });
        }
        EditorUtility.SetDirty(bank);
        AssetDatabase.SaveAssets();
        Debug.Log($"Sound bank: {written.Count} clips for {Recipes.Length} sounds.");
        return bank;
    }

    // Masters each of a recipe's sources that exists and writes it as a mono WAV.
    static IEnumerable<(string, string)> Master(Recipe recipe)
    {
        // Each keeps its own sample rate (the recordings are mostly 48 kHz, the procedural ones 44.1 kHz).
        var sources = new List<(float[] data, int rate)>();
        foreach (var file in recipe.Files)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(file);
            if (clip == null) continue; // the pack isn't restored, or a file is missing
            sources.Add((SoundMastering.ToMono(Samples(clip), clip.channels), clip.frequency));
        }
        foreach (var make in recipe.Procedural) sources.Add((make(), Rate));

        for (int i = 0; i < sources.Count; i++)
        {
            var (raw, rate) = sources[i];
            var data = recipe.Loop ? raw : SoundMastering.Trim(raw, rate, recipe.MaxSeconds);
            SoundMastering.Normalize(data, rate, recipe.TargetDb, whole: recipe.Loop);
            string path = $"{OutFolder}/{recipe.Sound}-{i + 1}.wav";
            WriteWav(path, data, rate);
            yield return (recipe.Sound, path);
        }
    }

    static float[] Samples(AudioClip clip)
    {
        clip.LoadAudioData();
        var data = new float[clip.samples * clip.channels];
        clip.GetData(data, 0);
        return data;
    }

    // Short sounds decompress on load (no hitch when they play); compressed with Vorbis on disk.
    static AudioClip Import(string path)
    {
        if (AssetImporter.GetAtPath(path) is AudioImporter importer)
        {
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.7f;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    static void WriteWav(string path, float[] data, int rate)
    {
        using var w = new BinaryWriter(File.Create(path));
        int bytes = data.Length * 2;
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        w.Write(36 + bytes);
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(rate);
        w.Write(rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        w.Write(bytes);
        foreach (var s in data) w.Write((short)(Mathf.Clamp(s, -1f, 1f) * 32767f));
    }
}
