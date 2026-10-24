using System.Collections;
using UnityEngine;

// A scripted tour of the Chemistry Yard for demo clips: walks the wizard
// and casts spells on a fixed schedule, so every recording plays the same
// chain reactions. It steers through the character's own input component,
// so the walk and run animations play as they would for a player. It
// reaches that component by name, so the project still compiles before
// the Asset Store packs are restored.
// Prototype > Record Demo Clip adds it and records.
public class DemoDirector : MonoBehaviour
{
    public bool Done { get; private set; }

    ChemGrid grid;
    Transform wizard;
    Component input;

    IEnumerator Start()
    {
        grid = FindFirstObjectByType<ChemGrid>();
        wizard = GameObject.Find("Wizard").transform;
        input = wizard.GetComponent("StarterAssetsInputs");
        input.GetType().GetField("cursorInputForLook").SetValue(input, false);
        wizard.GetComponent<SpellCaster>().enabled = false;
        yield return new WaitForSeconds(1f);

        // East to the oil spill and the pond.
        yield return Walk(20, 11);
        yield return Cast("fire-bolt", 19, 3, 0, 1.2f);
        yield return Cast("create-water", 19, 3, 0, 2.5f);   // water splashes burning oil
        yield return Cast("ray-of-frost", 20, 10, 0, 2f);     // a beam north across the pond
        yield return Cast("shocking-grasp", 24, 9, 1, 2f);    // down the metal strip into the water

        // West to the meadow: grease, then light it, then fan it.
        yield return Walk(14, 12);
        yield return Cast("grease", 10, 12, 3, 1.2f);
        yield return Cast("fire-bolt", 10, 12, 3, 2.5f);
        yield return Cast("gust-of-wind", 13, 12, 3, 4f);
        Done = true;
    }

    // Push the move stick toward the target, relative to the camera, until we arrive.
    IEnumerator Walk(int x, int y)
    {
        var target = grid.CellCenter(x, y);
        var cam = Camera.main.transform;
        while (true)
        {
            var to = target - wizard.position;
            to.y = 0f;
            if (to.magnitude < 0.3f) break;
            var forward = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(cam.right, Vector3.up).normalized;
            var dir = to.normalized;
            Move(new Vector2(Vector3.Dot(dir, right), Vector3.Dot(dir, forward)));
            yield return null;
        }
        Move(Vector2.zero);
    }

    void Move(Vector2 stick) => input.SendMessage("MoveInput", stick);

    IEnumerator Cast(string spell, int x, int y, int dir, float wait)
    {
        grid.World.Cast(spell, x, y, dir);
        yield return new WaitForSeconds(wait);
    }
}
