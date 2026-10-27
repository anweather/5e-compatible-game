using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// The solo wizard (in the yard) and the networked one (the NetWizard prefab)
// are built separately, so they could drift apart: a part added to one and
// forgotten on the other would make solo and online play differ quietly.
// Both get their gameplay parts from ChemYardBuilder.AddWizardParts; this
// checks the built results carry the same components, apart from the
// networking ones only the NetWizard has.
public class WizardPartsTests
{
    const string Yard = "Assets/Prototype/Scenes/ChemistryYard.unity";
    const string NetWizardPath = "Assets/Prototype/Prefabs/NetWizard.prefab";

    static readonly HashSet<System.Type> NetworkOnly = new()
    {
        typeof(NetworkObject), typeof(NetworkTransform), typeof(NetworkAnimator), typeof(NetWizard),
    };

    [Test]
    public void TheSoloAndNetworkedWizardsHaveTheSameParts()
    {
        var net = AssetDatabase.LoadAssetAtPath<GameObject>(NetWizardPath);
        Assert.IsNotNull(net, "build the yard first: Prototype > Build Chemistry Yard");
        var netParts = Parts(net).Where(t => !NetworkOnly.Contains(t)).ToList();

        WithYard(scene =>
        {
            var solo = scene.GetRootGameObjects().FirstOrDefault(g => g.name == WizardRig.Name);
            Assert.IsNotNull(solo, $"no {WizardRig.Name} in {Yard}");
            var soloParts = Parts(solo).ToList();
            CollectionAssert.AreEquivalent(soloParts, netParts,
                "the solo wizard and the NetWizard prefab differ: add the part in ChemYardBuilder.AddWizardParts so both get it");
        });
    }

    [Test]
    public void BothWizardsHaveEveryGameplayPart()
    {
        var net = AssetDatabase.LoadAssetAtPath<GameObject>(NetWizardPath);
        Assert.IsNotNull(net, "build the yard first: Prototype > Build Chemistry Yard");
        foreach (var part in new[] { typeof(PlayerControls), typeof(SpellCaster), typeof(CastPose), typeof(SurfaceEffects), typeof(Knockback), typeof(ItemHolder), typeof(SpellRadialHud), typeof(DodgeRoll), typeof(RollPose), typeof(OccluderFade), typeof(WizardOutfit) })
            Assert.IsNotNull(net.GetComponent(part), $"NetWizard has no {part.Name}");
    }

    // The component types on a wizard's root, one entry per component (so a part added twice shows).
    static IEnumerable<System.Type> Parts(GameObject wizard) =>
        wizard.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType());

    // The yard as it is open in the Editor, or opened alongside for the test and closed again.
    static void WithYard(System.Action<Scene> check)
    {
        var open = SceneManager.GetSceneByPath(Yard);
        if (open.IsValid() && open.isLoaded)
        {
            check(open);
            return;
        }
        Assert.IsTrue(System.IO.File.Exists(Yard), "build the yard first: Prototype > Build Chemistry Yard");
        var scene = EditorSceneManager.OpenScene(Yard, OpenSceneMode.Additive);
        try { check(scene); }
        finally { EditorSceneManager.CloseScene(scene, true); }
    }
}
