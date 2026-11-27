// What a map's baked ground was made from (docs/design/maps-as-data.md, phase 7): Prototype > Bake Map Data works a map's
// ground/ and cells.txt out of these files, and records their hash in ground.json. A test compares it with the files as
// they are, so an edit to a stamp, a village or a row that wasn't baked fails loudly rather than drawing the old ground.
using System;
using System.IO;
using System.Linq;

namespace Chem
{
    public static class MapInputs
    {
        public static readonly string[] Files =
        {
            MapFile.FileName, MapStamps.FileName, VillageLayout.FileName, WorldBuildings.FileName, WorldPuzzles.FileName, GolfCommand.FileName,
        };

        public const string ThemesFolder = "themes";

        const ulong Offset = 14695981039346656037, Prime = 1099511628211;

        public static string Hash(string worldDir)
        {
            ulong hash = Offset;
            var themes = Path.Combine(worldDir, ThemesFolder);
            var names = Files.Concat(Directory.Exists(themes)
                ? Directory.GetFiles(themes, "*.json", SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(worldDir, f).Replace('\\', '/'))
                    .OrderBy(n => n, StringComparer.Ordinal)
                : Enumerable.Empty<string>());
            foreach (var name in names)
            {
                string path = Path.Combine(worldDir, name);
                string text = File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal) : "";
                hash = Add(hash, name + "\n" + text + "\n");
            }
            return hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
        }

        static ulong Add(ulong hash, string text)
        {
            foreach (byte b in System.Text.Encoding.UTF8.GetBytes(text)) hash = (hash ^ b) * Prime;
            return hash;
        }
    }
}
