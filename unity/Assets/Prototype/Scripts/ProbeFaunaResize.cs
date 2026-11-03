using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using Chem;
using Punchy.Fauna;
using Unity.Cinemachine;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;

// The probe's faunaresize scenario (#80): set_species through the rules MCP, as Claude's apply does, in an
// Open room with the virtual player joined. Each side puts the same three deer (the same individuals) by its
// wizard; when the mod goes on, they and every other deer already out grow to their row's new size, and shrink
// back when it's reverted, alike on both peers. With --shots, a film of the three from the host's side.
public class FaunaResizeProbe : RoomProbe
{
    const string Title = "Giant deer", Slug = "giant-deer";
    const float Times = 3f, Settle = 3f, FrameSeconds = 1f / 15f;
    const int FilmWidth = 1280, FilmHeight = 720;
    static readonly uint[] Individuals = { 0x2F1Bu, 0x7A43u, 0xC1E5u };

    protected override ChangeMode Mode => ChangeMode.Open;
    protected override bool StartsWithAMod => false;

    ValleyFauna fauna;
    Vector3 middle;
    bool filming;
    int frames;

    /// <summary>Puts the probe's deer side by side ahead of a wizard, standing still; returns where the middle one is.</summary>
    public static Vector3 PlaceDeer(ValleyFauna fauna, ChemGrid grid, Vector3 wizard)
    {
        int kind = fauna.SpeciesIndex("deer");
        for (int k = 0; k < Individuals.Length; k++)
        {
            var p = wizard + new Vector3((k - 1) * 6f, 0f, 10f);
            int at = fauna.Spawn(kind, new float3(p.x, grid.GroundAt(p), p.z), math.PI, Individuals[k]);
            fauna.Puppet(at, math.PI, 0f);
        }
        var m = wizard + new Vector3(0f, 0f, 10f);
        return new Vector3(m.x, grid.GroundAt(m), m.z);
    }

    /// <summary>Each probe deer's scale, in order ("-" for one not out).</summary>
    public static string Scales(ValleyFauna fauna)
    {
        var scales = Individuals.Select(ind =>
        {
            for (int i = 0; i < fauna.Count; i++)
                if (fauna.State(i).Individual == ind) return fauna.State(i).Scale.ToString("0.000", CultureInfo.InvariantCulture);
            return "-";
        });
        return string.Join(" ", scales);
    }

    /// <summary>How many deer are out, and the most any one's scale is off the size its row gives it.</summary>
    public static (int count, float gap) Out(ValleyFauna fauna)
    {
        int kind = fauna.SpeciesIndex("deer"), count = 0;
        float gap = 0f;
        for (int i = 0; i < fauna.Count; i++)
        {
            if (fauna.SpeciesOf(i) != kind) continue;
            count++;
            gap = Mathf.Max(gap, Mathf.Abs(fauna.State(i).Scale - fauna.RowSize(i)));
        }
        return (count, gap);
    }

    protected override IEnumerator Steps()
    {
        fauna = FindAnyObjectByType<ValleyFauna>();
        Check("serving", RulesMcpServer.Url != null, RulesMcpServer.ConnectLine);
        if (fauna == null || RulesMcpServer.Url == null) yield break;
        Metric("pace_at_3", CreatureBrain.Pace(Times));
        // Small animals keep their pace one for one (a mire-spider is 0.35 of a spider); only giants are held back.
        Check("small_pace_one_for_one", Mathf.Abs(CreatureBrain.Pace(0.35f) - 0.35f) < 1e-6f && Mathf.Abs(CreatureBrain.Pace(1.25f) - 1.25f) < 1e-6f && Mathf.Abs(CreatureBrain.Pace(Times) - 1.5f) < 1e-6f,
            $"0.35 to {CreatureBrain.Pace(0.35f)}, 1.25 to {CreatureBrain.Pace(1.25f)}, {Times} to {CreatureBrain.Pace(Times)}");
        var wizard = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().gameObject;
        middle = PlaceDeer(fauna, grid, wizard.transform.position);
        yield return Ask("deer");
        if (Theirs == null) yield break;
        StartCoroutine(Film());
        yield return Wait(2f);
        yield return Ask("report");
        if (Theirs == null) yield break;
        var before = Seen("before");

        var drafted = RoomMcpProbe.Post(RoomMcpProbe.Tool("set_species", $"{{\"id\":\"deer\",\"scale\":{Times.ToString(CultureInfo.InvariantCulture)}}}"));
        yield return RoomMcpProbe.Until(drafted);
        string body = drafted.Result.body ?? "";
        int at = body.IndexOf("Draft ", StringComparison.Ordinal);
        Check("set_species_drafts", at >= 0, body);
        if (at < 0) yield break;
        string draft = body.Substring(at + 6).Split(':')[0];
        var on = RoomMcpProbe.Post(RoomMcpProbe.Tool("apply", $"{{\"mod\":\"{draft}\",\"title\":\"{Title}\"}}"));
        yield return RoomMcpProbe.Until(on);
        yield return Agree("giant", $"Player 1 switched on {Title}");
        if (Theirs == null) yield break;
        yield return Wait(Settle);
        yield return Ask("report");
        if (Theirs == null) yield break;
        var giant = Seen("giant");
        Check("grew_here", Grew(before.here, giant.here, Times), $"{before.here} to {giant.here}");
        Check("grew_there", Grew(before.there, giant.there, Times), $"{before.there} to {giant.there}");

        var off = RoomMcpProbe.Post(RoomMcpProbe.Tool("revert", $"{{\"mod\":\"{Slug}\"}}"));
        yield return RoomMcpProbe.Until(off);
        yield return Agree("reverted", $"Player 1 switched off {Title}");
        if (Theirs == null) yield break;
        yield return Wait(Settle);
        yield return Ask("report");
        if (Theirs == null) yield break;
        var back = Seen("reverted");
        Check("shrank_back_here", Grew(before.here, back.here, 1f), $"{before.here}, now {back.here}");
        Check("shrank_back_there", Grew(before.there, back.there, 1f), $"{before.there}, now {back.there}");
        filming = false;
        yield return null;
        Metric("film_frames", frames);
    }

    // Both sides' probe deer and every deer out, at one moment: the same three sizes on both, and each deer at its row's size.
    (string here, string there) Seen(string when)
    {
        string here = Scales(fauna);
        var (count, gap) = Out(fauna);
        Check($"{when}_same_on_both", here == Theirs.deer, $"here {here}, there {Theirs.deer}");
        Check($"{when}_every_deer_at_its_rows_size", gap < 0.01f && Theirs.deerGap < 0.01f,
            $"{count} deer out here, most off {gap:0.000}; {Theirs.deerOut} there, most off {Theirs.deerGap:0.000}");
        Metric($"{when}_deer_out_here", count);
        Metric($"{when}_deer_out_there", Theirs.deerOut);
        return (here, Theirs.deer);
    }

    // Each scale in `after` is `times` the one in `before`.
    static bool Grew(string before, string after, float times)
    {
        var a = before.Split(' ');
        var b = after.Split(' ');
        if (a.Length != b.Length || a.Contains("-") || b.Contains("-")) return false;
        for (int i = 0; i < a.Length; i++)
        {
            float was = float.Parse(a[i], CultureInfo.InvariantCulture), now = float.Parse(b[i], CultureInfo.InvariantCulture);
            if (Mathf.Abs(now - was * times) > 0.01f * times) return false;
        }
        return true;
    }

    // Frames of the three deer at 720p from in front, rendered off screen (no window needed), while the probe runs.
    IEnumerator Film()
    {
        if (string.IsNullOrEmpty(ScreenshotDir)) yield break;
        string folder = Path.Combine(ScreenshotDir, "film");
        Directory.CreateDirectory(folder);
        var cam = Camera.main;
        bool flown = cam.TryGetComponent<CinemachineBrain>(out var brain);
        if (flown) brain.enabled = false;
        var target = new RenderTexture(FilmWidth, FilmHeight, 24);
        var page = new Texture2D(FilmWidth, FilmHeight, TextureFormat.RGB24, false);
        filming = true;
        float next = 0f;
        while (filming)
        {
            yield return null;
            if (Time.unscaledTime < next) continue;
            next = Time.unscaledTime + FrameSeconds;
            cam.transform.position = middle + new Vector3(7f, 6f, -24f);
            cam.transform.LookAt(middle + Vector3.up * 2.5f);
            cam.targetTexture = target;
            cam.Render();
            cam.targetTexture = null;
            RenderTexture.active = target;
            page.ReadPixels(new Rect(0, 0, FilmWidth, FilmHeight), 0, 0);
            RenderTexture.active = null;
            File.WriteAllBytes(Path.Combine(folder, $"frame_{frames++:00000}.jpg"), page.EncodeToJPG(88));
        }
        if (flown) brain.enabled = true;
        Destroy(page);
        Destroy(target);
    }
}
