Needs: core, unity
Area: unity/Assets/Prototype/Chemistry (LandmarkIntent, MapStamps, WorldBuildings' pad grading), Scripts/ChemGrid.cs (the
order map stamps and rows are laid), Scripts/RowHouses.cs (the great tree), the yard bake and ValleyChemistry (trees on
bare cells), world/landmarks/, rules/buildings.json (the great-tree detail's fields)
Branch: landmark-look-pass
Taken by: the Mac

# The landmarks' look pass (world squares phase 1b follow-ups)

From the 1b clips (landmark-valley-look) and the lead's calls:

1. Pads and the cone: lay map stamps first, then rows, with a row's pad graded on the stamped land. Put the climb's pads
   back; a test that a stamp under a row's site is laid.
2. The great tree: scale 3-4 and a broad-crowned prefab, as fields on the great-tree detail.
3. Bare stamps: drawn terrain trees drop on a map stamp's bare cells, in the yard bake and at runtime. The crater reads
   from eye level (a raised rim or a higher view).
4. Andrew (2026-11-08): the volcano's village isn't on the peak. It winds round the mountain's lower slopes, a road at a
   gentle grade with houses on its uphill and its downhill side.
5. Re-film all three (the fishing village framed on the real lake) and send them to Andrew.

Gates: `dotnet build core -c Release -warnaserror -m:1`, `dotnet test core` twice, `dotnet format`, jscpd. Push the branch
only; the lead lands it. Remove this claim in the last commit.
