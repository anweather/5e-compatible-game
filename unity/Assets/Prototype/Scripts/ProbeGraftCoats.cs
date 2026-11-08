using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Chem;
using Punchy.Fauna;
using Unity.Cinemachine;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

// The probe's graftcoats scenario (graft-coats, bodies-and-surfaces.md section 2, J-067): a centaur, a faun and a
// six-legged bear made by add_species with from, each with a donor whose coat is "own", put on through the rules MCP
// in an Open room with the virtual player joined. Each side puts the same three (the same individuals) by its
// wizard. Every segment must draw in the colours its donor's own animal of that individual wears: the deer's coat on
// the centaur's legs, the human's tunic, skin and hair on its torso; the human's on the faun, the deer's on its hoofed
// legs; the black bear's on the bear, the grizzly's on its middle pair. With --shots, a film of the three walking and
// a picture of each.
public class GraftCoatsProbe : RoomProbe
{
    const string Title = "Grafted beasts", Slug = "grafted-beasts";
    const float FilmSeconds = 22f, FrameSeconds = 1f / 15f, Apart = 5f, Pace = 0.9f, Heading = math.PI / 2f;
    const int FilmWidth = 1280, FilmHeight = 720;

    /// <summary>The three grafts: id, name, donors as add_species takes them, the donor the row's own slots wear, and the individual each side puts out.</summary>
    public static readonly (string id, string name, string from, string first, uint individual)[] Grafts =
    {
        ("centaur", "Centaur", "[{\"species\":\"deer\",\"drop\":[\"neck\",\"head\",\"ears\",\"eyes\"]},{\"species\":\"human\",\"take\":[\"human-torso\"],\"at\":[0,0.45,0.8],\"scale\":1.1,\"coat\":\"own\"}]", "deer", 0x2F1Bu),
        ("faun", "Faun", "[{\"species\":\"human\",\"drop\":[\"legs\"]},{\"species\":\"deer\",\"take\":[\"hind\"],\"at\":[0,-0.3,0],\"coat\":\"own\"}]", "human", 0x7A47u),
        ("six-legged-bear", "Six-legged bear", "[{\"species\":\"black-bear\"},{\"species\":\"grizzly-bear\",\"take\":[\"fore\"],\"at\":[0,-0.45,0],\"coat\":\"own\"}]", "black-bear", 0xC1E5u),
    };

    protected override ChangeMode Mode => ChangeMode.Open;
    protected override bool StartsWithAMod => false;

    ValleyFauna fauna;

    /// <summary>The animal out with this individual, or -1.</summary>
    public static int Find(ValleyFauna fauna, uint individual)
    {
        for (int i = 0; i < fauna.Count; i++)
            if (fauna.State(i).Individual == individual) return i;
        return -1;
    }

    /// <summary>Puts the three grafts in a line out on the open meadow by a wizard, facing +x (along the line) at this speed.</summary>
    public static void PlaceGrafts(ValleyFauna fauna, ChemGrid grid, Vector3 wizard, float speed)
    {
        for (int k = 0; k < Grafts.Length; k++)
        {
            int kind = fauna.SpeciesIndex(Grafts[k].id);
            if (kind < 0 || fauna.Body(kind) == null) continue;
            var p = wizard + new Vector3((k - 1) * Apart, 0f, -14f);
            int at = fauna.Spawn(kind, new float3(p.x, grid.GroundAt(p), p.z), Heading, Grafts[k].individual);
            fauna.Puppet(at, Heading, speed);
        }
    }

    /// <summary>What one graft's segments wear, against what its donors' own animals of the same individual wear.</summary>
    public sealed class Worn
    {
        public string Id;
        public int Good, Total, Drawn, DrawnGood, OwnSegments, OwnDiffers;
        /// <summary>Each slot as "slot:donor:#rrggbb" (its first segment's colour), a mismatch marked "!".</summary>
        public List<string> Slots { get; } = new();
        public uint Hash = 2166136261u;
        public bool Out => Total > 0;
        public bool Right => Out && Good == Total && Drawn > 0 && DrawnGood == Drawn && OwnSegments > 0 && OwnDiffers > 0;
    }

    /// <summary>Every graft out on this side, read as the animator draws it.</summary>
    public static List<Worn> Coats(ValleyFauna fauna, FaunaTables tables)
    {
        var all = new List<Worn>();
        foreach (var g in Grafts)
        {
            var worn = new Worn { Id = g.id };
            all.Add(worn);
            int at = Find(fauna, g.individual), kind = fauna.SpeciesIndex(g.id);
            if (at < 0 || kind < 0 || fauna.SpeciesOf(at) != kind || fauna.Body(kind) == null) continue;
            Read(worn, fauna, fauna.Body(kind), at, g.first, Labels(tables, g.id));
        }
        return all;
    }

    static void Read(Worn worn, ValleyFauna fauna, CreatureBody b, int at, string first, List<string> labels)
    {
        var traits = fauna.Traits(at);
        uint individual = fauna.State(at).Individual;
        var actual = new float3[b.Segments.Length];
        for (int s = 0; s < b.Segments.Length; s++)
        {
            string donor = b.Segments[s].Palette > 0 ? b.Own[b.Segments[s].Palette - 1].Id : first;
            actual[s] = b.ColourOf(s, in traits).xyz;
            bool good = math.cmax(math.abs(actual[s] - Expected(b.Segments[s], fauna, donor, individual))) < 1e-4f;
            Count(worn, b, s, in traits, actual[s], good);
            string slot = SlotAt(b, s, labels);
            if (slot != null) worn.Slots.Add($"{slot}:{donor}:{Hex(actual[s])}{(good ? "" : "!")}");
        }
        // What the animator writes for this animal: every colour it draws is one of its segments'.
        var drawn = new SegmentInstance[b.Segments.Length];
        int shown = new CreatureAnimator().Evaluate(b, fauna.State(at), in traits, fauna, drawn, 0);
        worn.Drawn = shown;
        for (int i = 0; i < shown; i++)
            if (actual.Any(a => math.cmax(math.abs(a - drawn[i].Color.xyz)) < 1e-5f)) worn.DrawnGood++;
    }

    // Tallies one segment: whether it matched, whether it's an own-coat one unlike the row's coat, and its colour into the hash.
    static void Count(Worn worn, CreatureBody b, int s, in CreatureTraits traits, float3 colour, bool good)
    {
        worn.Total++;
        if (good) worn.Good++;
        if (b.Segments[s].Palette > 0 && b.Segments[s].Fixed == 0)
        {
            worn.OwnSegments++;
            if (math.cmax(math.abs(colour - b.SegmentColor(s, in traits.Worn).xyz)) > 0.01f) worn.OwnDiffers++;
        }
        worn.Hash = (worn.Hash ^ (uint)Mathf.RoundToInt(colour.x * 1000f)) * 16777619u;
        worn.Hash = (worn.Hash ^ (uint)Mathf.RoundToInt(colour.y * 1000f)) * 16777619u;
        worn.Hash = (worn.Hash ^ (uint)Mathf.RoundToInt(colour.z * 1000f)) * 16777619u;
    }

    // The slot a segment starts ("body" for the body), or null for a segment further down a part.
    static string SlotAt(CreatureBody b, int segment, List<string> labels)
    {
        if (segment == 0) return "body";
        for (int i = 0; i < b.Parts.Length; i++)
            if (b.Parts[i].FirstSegment == segment) return i < labels.Count ? labels[i] : "?";
        return null;
    }

    // A segment's colour on the donor's own animal of this individual: its hair (over any colour of its own), its fixed colour, or its coat's mix.
    static float3 Expected(CreatureSegment seg, ValleyFauna fauna, string donor, uint individual)
    {
        var d = fauna.Body(fauna.SpeciesIndex(donor));
        var t = d.TraitsOf(d.PickCoat(CreatureBody.Unit(individual, 0)), individual);
        if (seg.Hair != 0 && t.Hair.x >= 0f) return t.Hair.xyz;
        if (seg.Fixed != 0) return seg.FixedColor.xyz;
        return math.lerp(t.Worn.Base.xyz, t.Worn.Accent.xyz, seg.Accent);
    }

    // Each part's slot id in the body's order (CreatureBody.Build): a mirrored slot's two copies, children after every copy of their parent.
    static List<string> Labels(FaunaTables tables, string id)
    {
        var labels = new List<string>();
        var copies = new Dictionary<string, List<int>>();
        foreach (var slot in tables.BodyOf(id).Slots)
        {
            var mine = new List<int>();
            copies[slot.Id] = mine;
            int parents = slot.On != null && copies.TryGetValue(slot.On, out var up) ? up.Count : 1;
            for (int p = 0; p < parents; p++)
            {
                mine.Add(labels.Count);
                labels.Add(slot.Mirror ? slot.Id + "(R)" : slot.Id);
                if (!slot.Mirror) continue;
                mine.Add(labels.Count);
                labels.Add(slot.Id + "(L)");
            }
        }
        return labels;
    }

    static string Hex(float3 linear)
    {
        var c = new Color(linear.x, linear.y, linear.z).gamma;
        return "#" + ColorUtility.ToHtmlStringRGB(c).ToLowerInvariant();
    }

    /// <summary>One side's grafts in a line: each one's matches out of its segments and its colours' hash, for the other side to compare.</summary>
    public static string Fingerprint(List<Worn> all) =>
        string.Join(" ", all.Select(w => $"{w.Id}:{w.Good}/{w.Total}:{w.DrawnGood}/{w.Drawn}:{w.OwnDiffers}/{w.OwnSegments}:{w.Hash:x8}"));

    protected override IEnumerator Steps()
    {
        fauna = FindAnyObjectByType<ValleyFauna>();
        Check("serving", RulesMcpServer.Url != null, RulesMcpServer.ConnectLine);
        if (fauna == null || RulesMcpServer.Url == null) yield break;

        // One draft for the three, as Claude would make them, and one apply.
        draft = null;
        foreach (var g in Grafts)
        {
            yield return AddSpecies(g.id, g.name, g.from);
            if (draft == null) yield break;
        }
        var on = RoomMcpProbe.Post(RoomMcpProbe.Tool("apply", $"{{\"mod\":\"{draft}\",\"title\":\"{Title}\"}}"));
        yield return RoomMcpProbe.Until(on);
        yield return Agree("grafted", $"Player 1 switched on {Title}");
        if (Theirs == null) yield break;
        yield return Wait(1f);
        foreach (var g in Grafts)
        {
            var b = fauna.Body(fauna.SpeciesIndex(g.id));
            Check($"{g.id}_built_with_one_own_palette", b != null && b.Own.Length == 1, b == null ? "no body" : string.Join(", ", b.Own.Select(p => p.Id)));
        }

        // Noon and clear for everyone, through the room, so the film is in daylight and both peers keep in step.
        yield return Say("set_time", "{\"time\":\"noon\"}");
        yield return Say("set_weather", "{\"weather\":\"clear\",\"minutes\":10}");
        yield return Wait(2f);
        var wizard = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject().gameObject;
        var start = wizard.transform.position;
        PlaceGrafts(fauna, grid, start, 0f);
        yield return Ask("grafts");
        if (Theirs == null) yield break;
        yield return Wait(1f);
        yield return Ask("report");
        if (Theirs == null) yield break;

        CheckCoats(Coats(fauna, grid.CurrentRules.Effective.Fauna));

        if (!string.IsNullOrEmpty(ScreenshotDir)) yield return Film(start, wizard.GetComponent<SpellCaster>());

        var off = RoomMcpProbe.Post(RoomMcpProbe.Tool("revert", $"{{\"mod\":\"{Slug}\"}}"));
        yield return RoomMcpProbe.Until(off);
        yield return Agree("reverted", $"Player 1 switched off {Title}");
        if (Theirs == null) yield break;
        yield return Wait(1f);
        Check("reverted_takes_them_away", Grafts.All(g => Find(fauna, g.individual) < 0), string.Join(" ", Grafts.Select(g => Find(fauna, g.individual))));
        CompareHashes();
    }

    string draft;

    IEnumerator Say(string tool, string args)
    {
        var said = RoomMcpProbe.Post(RoomMcpProbe.Tool(tool, args));
        yield return RoomMcpProbe.Until(said);
        string body = said.Result.body ?? "";
        Check(tool, said.Result.status == 200 && !body.Contains("\"isError\":true"), body.Length > 300 ? body.Substring(0, 300) : body);
    }

    // add_species with from into the probe's draft (a new one the first time); draft is null if it refused.
    IEnumerator AddSpecies(string id, string name, string from)
    {
        string into = draft != null ? $",\"draft\":\"{draft}\"" : "";
        var added = RoomMcpProbe.Post(RoomMcpProbe.Tool("add_species", $"{{\"id\":\"{id}\",\"name\":\"{name}\",\"density\":0,\"from\":{Quoted(from)}{into}}}"));
        yield return RoomMcpProbe.Until(added);
        string body = added.Result.body ?? "";
        draft = RoomMcpProbe.DraftIn(body);
        Check($"add_species_{id}", draft != null, body.Length > 400 ? body.Substring(0, 400) : body);
    }

    void CheckCoats(List<Worn> mine)
    {
        foreach (var w in mine)
        {
            Check($"{w.Id}_is_out", w.Out, w.Id);
            Check($"{w.Id}_every_segment_wears_its_donors_colours", w.Out && w.Good == w.Total, $"{w.Good}/{w.Total}: {string.Join(" ", w.Slots)}");
            Check($"{w.Id}_drawn_as_its_segments", w.Drawn > 0 && w.DrawnGood == w.Drawn, $"{w.DrawnGood}/{w.Drawn} drawn segments");
            Check($"{w.Id}_own_coat_slots_differ_from_the_rows", w.OwnDiffers > 0, $"{w.OwnDiffers} of {w.OwnSegments} own-coat segments unlike the row's coat");
        }
        string here = Fingerprint(mine);
        Check("same_coats_on_both", here == Theirs.coats, $"here {here}; there {Theirs.coats}");
        Check("right_here_and_there", Theirs.coats != null && Grafts.All(g => Theirs.coats.Contains($"{g.id}:")) && mine.All(w => w.Right), Theirs.coats);
    }

    static string Quoted(string json) => "\"" + json.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    // The three walking, the camera alongside, then a picture of each standing, all at 720p off screen, the wizard's aim marker hidden.
    IEnumerator Film(Vector3 start, SpellCaster caster)
    {
        var cam = Camera.main;
        bool flown = cam.TryGetComponent<CinemachineBrain>(out var brain);
        if (flown) brain.enabled = false;
        var target = new RenderTexture(FilmWidth, FilmHeight, GraphicsFormat.B10G11R11_UFloatPack32, GraphicsFormat.D32_SFloat_S8_UInt);
        var page = new Texture2D(FilmWidth, FilmHeight, TextureFormat.RGB24, false);
        Pacing(Pace);
        yield return Walk(start, caster, cam, target, page);
        Pacing(0f);
        yield return Wait(1.5f);
        yield return Stills(caster, cam, target, page);
        if (flown) brain.enabled = true;
        Destroy(page);
        Destroy(target);
    }

    void Pacing(float speed)
    {
        foreach (var g in Grafts)
        {
            int at = Find(fauna, g.individual);
            if (at >= 0) fauna.Puppet(at, Heading, speed);
        }
    }

    IEnumerator Walk(Vector3 start, SpellCaster caster, Camera cam, RenderTexture target, Texture2D page)
    {
        string folder = Path.Combine(ScreenshotDir, "film");
        Directory.CreateDirectory(folder);
        int frames = 0;
        float next = 0f;
        for (float t = 0f; t < FilmSeconds; t += Time.deltaTime)
        {
            yield return null;
            if (Time.unscaledTime < next) continue;
            next = Time.unscaledTime + FrameSeconds;
            var middle = Middle(start);
            cam.transform.position = middle + new Vector3(2f, 2.2f, -9.5f);
            cam.transform.LookAt(middle + Vector3.up * 1.2f);
            if (caster != null) caster.Marker?.Hide();
            Shoot(cam, target, page, Path.Combine(folder, $"frame_{frames++:00000}.jpg"));
        }
        Metric("film_frames", frames);
    }

    // Each one standing, from in front and to the side, close enough to see whose colours each part wears.
    IEnumerator Stills(SpellCaster caster, Camera cam, RenderTexture target, Texture2D page)
    {
        foreach (var g in Grafts)
        {
            int at = Find(fauna, g.individual);
            if (at < 0) continue;
            var s = fauna.State(at);
            var p = new Vector3(s.Position.x, s.Position.y, s.Position.z);
            float tall = fauna.Body(fauna.SpeciesOf(at)).StandHeight * s.Scale;
            cam.transform.position = p + new Vector3(1.6f, 0.7f + tall, -5.2f);
            cam.transform.LookAt(p + Vector3.up * (tall + 0.3f));
            yield return null;
            if (caster != null) caster.Marker?.Hide();
            Shoot(cam, target, page, Path.Combine(ScreenshotDir, $"still_{g.id}.jpg"));
        }
    }

    Vector3 Middle(Vector3 start)
    {
        int at = Find(fauna, Grafts[1].individual);
        if (at < 0) return start;
        var s = fauna.State(at).Position;
        return new Vector3(s.x, grid.GroundAt(new Vector3(s.x, 0f, s.z)), s.z);
    }

    static void Shoot(Camera cam, RenderTexture target, Texture2D page, string path)
    {
        cam.targetTexture = target;
        cam.Render();
        cam.targetTexture = null;
        var was = RenderTexture.active;
        RenderTexture.active = target;
        page.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        RenderTexture.active = was;
        File.WriteAllBytes(path, page.EncodeToJPG(90));
    }
}
