using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// The chunk terrains draw instanced (MapTerrains.Configure) and are made in play, so no terrain in the scene keeps the terrain
// shader's instancing variants in a player: only the material's own flag does. Without it a player drew the ground white
// (0.1.13, Andrew's playtest 2026-11-23); the Editor keeps every variant and never showed it.
public class TerrainInstancingTests
{
    const string Yard = "Assets/Prototype/Scenes/ChemistryYard.unity";

    [Test]
    public void TheYardsTerrainMaterialKeepsItsInstancingVariants()
    {
        var open = SceneManager.GetSceneByPath(Yard);
        bool opened = false;
        if (!open.IsValid() || !open.isLoaded)
        {
            Assert.IsTrue(System.IO.File.Exists(Yard), "build the yard first: Prototype > Build Chemistry Yard");
            open = EditorSceneManager.OpenScene(Yard, OpenSceneMode.Additive);
            opened = true;
        }
        try
        {
            MapTerrains terrains = null;
            foreach (var root in open.GetRootGameObjects())
                if (terrains == null) terrains = root.GetComponentInChildren<MapTerrains>(true);
            Assert.IsNotNull(terrains, "the yard has no MapTerrains");
            Assert.IsNotNull(terrains.material, "MapTerrains has no material");
            Assert.IsTrue(terrains.material.enableInstancing, $"{terrains.material.name} has GPU instancing off: a player strips the instanced terrain's variants");
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(open, true);
        }
    }
}
