using System.IO;
using System.Linq;
using Chem;
using NUnit.Framework;

// The Wide World (world-12x10, "bake": false) is a map to play (docs/design/world-12x8-scope.md): it's offered with the
// others, and played from a folder of its rows alone (GeneratedMap.EnsureRows), its cells, land and things made as they're
// read (GeneratedSource), never a bake of the whole map. The nine stays the map a player starts on (SoloMapTests).
public class WideWorldTests
{
    const string Id = "world-12x10";

    [Test]
    public void TheWideWorldIsOfferedWithTheOtherMaps()
    {
        Assert.That(ChemGrid.Maps().Select(m => m.id), Has.Member(Id));
    }

    [Test]
    public void TheWideWorldIsPlayedFromItsRowsAlone()
    {
        string folder = ChemGrid.PlayFolder(Id, playing: true);
        Assert.That(GeneratedMap.IsRowsOnly(folder), Is.True, folder);
        Assert.That(File.Exists(Path.Combine(folder, MapFile.CellsName)), Is.False, "no cells written");
        Assert.That(File.Exists(Path.Combine(folder, MapFile.LandName)), Is.False, "no land written");
        Assert.That(MapCheck.Errors(folder), Is.Empty);
    }
}
