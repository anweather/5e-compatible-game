using System;
using System.IO;
using System.Linq;
using System.Collections;
using Chem;
using UnityEngine;

// What the Editor can't prove about a player build (UX pass 4): that shaders made
// by reference reach it unstripped. Started with -player-look <dir>: on the meadow
// by the wizard it lights a row each of thatch, log wall and oil, shows a Fireball's
// aim marker, reads the camera through a RenderTexture (which works with no input
// desktop, as ScreenCapture doesn't), saves player-look.png, writes one PLAYER-LOOK
// line to the log with what it counted, and quits.
public class PlayerLook : MonoBehaviour
{
    const int Wide = 1280, High = 720, Across = 6;
    string dir;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "-player-look");
        if (at < 0 || at + 1 >= args.Length) return;
        var go = new GameObject("Player look");
        DontDestroyOnLoad(go);
        go.AddComponent<PlayerLook>().dir = args[at + 1];
    }

    IEnumerator Start()
    {
        ChemGrid grid = null;
        GameObject wizard = null;
        for (int i = 0; i < 3000 && (wizard == null || grid == null || grid.World == null); i++)
        {
            yield return null;
            wizard = WizardRig.FindLocal();
            grid = FindAnyObjectByType<ChemGrid>();
        }
        if (wizard == null || grid == null)
        {
            Debug.Log("PLAYER-LOOK no wizard or world");
            Application.Quit(1);
            yield break;
        }
        yield return new WaitForSeconds(4f);
        var world = grid.World;
        var cam = Camera.main;
        var o = grid.CellAt(wizard.transform.position + wizard.transform.forward * 10f);
        foreach (var (terrain, cover, row) in new (string, string, int)[] { ("thatch", null, 0), (TerrainIds.LogWall, null, 2), (TerrainIds.Dirt, CoverIds.Oil, 4) })
            for (int x = 0; x < Across; x++)
            {
                world.Paint(o.x + x, o.y + row, terrain);
                if (cover != null) world.SetCover(o.x + x, o.y + row, cover);
                world.Apply(o.x + x, o.y + row, "fire", 3, -1, false);
            }
        var spell = world.Rules.Spell(SpellIds.Fireball);
        var aim = new Vector2Int(o.x + 2, o.y - 7);
        var marker = new AimMarker(grid, grid.aimShader);
        marker.Show(spell, world.SpellCells(spell, aim.x, aim.y, 1, 1).ToList(), new CastCommand(spell.Id, aim.x, aim.y, 1), new Color(0.1f, 1f, 1f));
        if (cam.TryGetComponent<Unity.Cinemachine.CinemachineBrain>(out var brain)) brain.enabled = false;
        var mid = grid.CellCenter(o.x + Across / 2, o.y - 1);
        mid.y = grid.GroundAt(mid);
        cam.transform.SetPositionAndRotation(mid + new Vector3(-4f, 14f, -14f), Quaternion.LookRotation(new Vector3(4f, -14f, 14f)));
        yield return new WaitForSeconds(1.5f);

        var rt = new RenderTexture(Wide, High, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        yield return null;
        yield return null;
        yield return new WaitForEndOfFrame();
        var was = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(Wide, High, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, Wide, High), 0, 0);
        tex.Apply();
        RenderTexture.active = was;
        cam.targetTexture = null;
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "player-look.png"), tex.EncodeToPNG());
        var pixels = tex.GetPixels32();
        int flames = pixels.Count(c => c.r > 200 && c.g > 70 && c.g < 235 && c.b < 150);
        int ring = pixels.Count(c => c.b > 170 && c.g > 170 && c.r < 140);
        var fx = grid.Flames;
        Debug.Log($"PLAYER-LOOK flamePixels={flames} markerPixels={ring} burning={(fx != null ? fx.CellsBurning : -1)} " +
            $"flameShader={(grid.flameShader != null && grid.flameShader.isSupported)} sheets={grid.flameSheets?.Count(s => s != null) ?? 0} " +
            $"aimShader={(grid.aimShader != null && grid.aimShader.isSupported)} markerVertices={marker.Vertices} " +
            $"magenta={pixels.Count(c => c.r > 240 && c.g < 16 && c.b > 240)}");
        Application.Quit(0);
    }
}
