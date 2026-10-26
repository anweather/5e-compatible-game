using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

// The network ids of the yard (StableNetworkIds) must be the same on every
// machine and every rebuild, or one machine can't join another's game: the
// host refuses a client whose prefab hashes differ (ForceSamePrefabs). These
// pin each network prefab's GlobalObjectIdHash, and the grid's in the scene,
// to the values every machine works out. If one fails after a deliberate
// change (a new kind of thing, a renamed path), every machine needs that
// build, so update the value here.
public class NetworkIdTests
{
    const string Things = "Assets/Prototype/Generated/Things/";
    const string ThingList = "Assets/Prototype/Generated/ThingNetworkPrefabs.asset";
    const string NetWizard = "Assets/Prototype/Prefabs/NetWizard.prefab";
    const string Yard = "Assets/Prototype/Scenes/ChemistryYard.unity";

    // Generated per machine, so their ids come from StableNetworkIds.
    static readonly Dictionary<string, uint> GeneratedHashes = new()
    {
        [Things + "barrel.prefab"] = 3056903041,
        [Things + "crate.prefab"] = 2639139456,
        [Things + "boulder.prefab"] = 1685749016,
        [Things + "glow-ball-amber.prefab"] = 2031503279,
        [Things + "glow-ball-azure.prefab"] = 2168629188,
        [Things + "glow-ball-rose.prefab"] = 972378031,
        [Things + "bullseye-lantern.prefab"] = 2626518799,
        [Things + "spyglass.prefab"] = 1313338354,
        [Things + "dancing-lights.prefab"] = 3321017021,
        [Things + "sending-stone.prefab"] = 2606121274,
        [Things + "ball.prefab"] = 1747778105,
    };

    // In git, so its ids are too; a change here means the tracked prefab's were rewritten.
    const uint NetWizardHash = 1156921532;
    const uint ChemGridHash = 506209271;

    [Test]
    public void StableIdsComeFromThePathAlone()
    {
        // The same on every machine: worked out from the path, nothing else.
        Assert.AreEqual("27917d84d841818a65434665781b7cea", StableNetworkIds.GuidFor(Things + "barrel.prefab"));
        Assert.AreEqual(9091701210552913752L, StableNetworkIds.FileIdFor(Things + "barrel.prefab"));
        Assert.AreEqual(7881862778351988262L, StableNetworkIds.FileIdFor(Yard, "ChemGrid"));
    }

    [Test]
    public void EveryGeneratedNetworkPrefabKeepsItsHash()
    {
        var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(ThingList);
        Assert.IsNotNull(list, "build the yard first: Prototype > Build Chemistry Yard");
        var listed = list.PrefabList.Select(p => AssetDatabase.GetAssetPath(p.Prefab)).ToList();
        CollectionAssert.AreEquivalent(GeneratedHashes.Keys, listed, "a network prefab was added or removed: pin its hash here");
        foreach (var path in listed)
        {
            var networkObject = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<NetworkObject>();
            Assert.AreEqual($"GlobalObjectId_V1-1-{StableNetworkIds.GuidFor(path)}-{StableNetworkIds.FileIdFor(path)}-0",
                GlobalObjectId.GetGlobalObjectIdSlow(networkObject).ToString(), path);
            Assert.AreEqual(GeneratedHashes[path], HashOf(networkObject), path);
        }
    }

    [Test]
    public void TheTrackedNetWizardKeepsItsHash()
    {
        var networkObject = AssetDatabase.LoadAssetAtPath<GameObject>(NetWizard).GetComponent<NetworkObject>();
        Assert.AreEqual(NetWizardHash, HashOf(networkObject));
    }

    [Test]
    public void TheYardsNetworkIdsAreTheStableOnes()
    {
        CollectionAssert.IsEmpty(ChemYardBuilder.UnstableNetworkIds());
        var hashes = System.Text.RegularExpressions.Regex.Matches(System.IO.File.ReadAllText(Yard), @"\n  GlobalObjectIdHash: (\d+)")
            .Select(m => uint.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        CollectionAssert.AreEqual(new[] { ChemGridHash }, hashes);
    }

    [Test]
    public void ANetworkObjectOffItsStableIdIsCaught()
    {
        const string yaml = "%YAML 1.1\n--- !u!1 &100\nGameObject:\n  m_Name: ChemGrid\n" +
                            "--- !u!114 &12345\nMonoBehaviour:\n  m_GameObject: {fileID: 100}\n" +
                            "  m_EditorClassIdentifier: Unity.Netcode.Runtime::Unity.Netcode.NetworkObject\n";
        CollectionAssert.AreEqual(new[] { "Some.unity: ChemGrid" }, StableNetworkIds.UnstableInScene("Some.unity", yaml));
        string pinned = yaml.Replace("12345", StableNetworkIds.FileIdFor("Some.unity", "ChemGrid").ToString(System.Globalization.CultureInfo.InvariantCulture));
        CollectionAssert.IsEmpty(StableNetworkIds.UnstableInScene("Some.unity", pinned));
    }

    static uint HashOf(NetworkObject networkObject) =>
        new SerializedObject(networkObject).FindProperty("GlobalObjectIdHash").uintValue;
}
