using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;

// The probe's flare scenario (issue #16): the wizard from all round, the camera
// 30 to 60 degrees down, near and far, in a hat and in a cowl, standing and
// walking. Each view is drawn into a float target with the post effects off and
// its pixels are read, so a bad one is counted before the bloom spreads it: a NaN
// or infinite pixel, or one far over anything the wizard should glow with. The
// worst view is then drawn with each of the wizard's renderers left out in turn,
// to name the one that makes it, and saved with its bad pixels marked (--shots).
public class FlareProbe : ProbeScenario
{
    const int Width = 1600, Height = 900;
    // Brighter than anything on the wizard should be: the eyes are 2.4.
    const float TooBright = 12f;
    const string NotTheWizard = "no renderer of the wizard clears it";
    static readonly float[] Pitches = { 30f, 38f, 45f, 52f, 60f };
    static readonly float[] Distances = { 1.8f, 3.6f };

    Camera view;
    RenderTexture target;
    Texture2D read;

    struct Tally
    {
        public int bad, bright, outside;
        public float peak;
        public Vector2Int at;
    }

    // A view's place and name, and what it counted.
    struct Seen
    {
        public Vector3 from, to;
        public string name;
        public Tally tally;
    }

    Seen worstBad, worstBright;
    string blamedBad = "", blamedBright = "";
    int outsideWizard;
    int views, nanViews, brightViews, nanPixels, brightPixels;
    float peak;

    protected override IEnumerator Run()
    {
        yield return Switch(At(12f) + ", 'weather': { 'calmPeriods': 0, 'rampMinutes': 0.02, 'weights': " +
            "{ 'clear': 1, 'cloudy': 0, 'windy': 0, 'fog': 0, 'rain': 0, 'storm': 0, 'snow': 0 } }");
        var outfit = wizard.GetComponent<WizardOutfit>();
        int saved = outfit.Look.Seed;
        view = Camera.main;
        target = new RenderTexture(Width, Height, 32, GraphicsFormat.R32G32B32A32_SFloat);
        read = new Texture2D(Width, Height, TextureFormat.RGBAFloat, false, true);
        var extra = view.GetUniversalAdditionalCameraData();
        bool post = extra.renderPostProcessing;

        foreach (WizardLook.HeadwearKind kind in System.Enum.GetValues(typeof(WizardLook.HeadwearKind)))
        {
            int worn = outfit.Look.Seed;
            while (WizardLook.From(worn).Headwear != kind) worn = WizardLook.NextHeadwear(worn);
            outfit.Wear(worn);
            yield return Wait(0.3f);
            Raw(extra, post, $"{kind} still");
            var walk = StartCoroutine(puppet.WalkTo(wizard.transform.position + wizard.transform.forward * 12f));
            for (int i = 0; i < 3; i++)
            {
                yield return Wait(0.17f);
                Raw(extra, post, $"{kind} walking {i}");
                // The same pose through the real post effects, the bloom and all.
                var top = wizard.transform.position + Vector3.up * 1.6f;
                Cut(top + Quaternion.Euler(30f, 60f, 0f) * Vector3.back * 1.8f, top);
                Shot($"{kind}-walking-{i}");
                yield return null;
            }
            StopCoroutine(walk);
            puppet.Move(Vector2.zero);
            yield return Wait(0.3f);
        }

        Metric("views", views);
        Metric("nan_views", nanViews);
        Metric("nan_pixels", nanPixels);
        Metric("bright_views", brightViews);
        Metric("bright_pixels", brightPixels);
        Metric("peak", peak);
        Check("no_bad_pixels", nanPixels == 0, $"{nanPixels} NaN or infinite pixels in {nanViews} of {views} views; worst: {worstBad.name}, {worstBad.tally.bad} at {worstBad.tally.at}");
        // The sun's glint on the ground behind the wizard is the ground's, not this probe's: only the wizard's own fails it.
        bool wizardBright = brightPixels > 0 && !blamedBright.StartsWith(NotTheWizard, System.StringComparison.Ordinal);
        Check("nothing_far_too_bright", !wizardBright, $"{brightPixels} pixels over {TooBright} in {brightViews} of {views} views; worst: {worstBright.name}, {worstBright.tally.bright} at {worstBright.tally.at}; {blamedBright}");

        Metric("outside_wizard", outsideWizard);
        if (nanPixels > 0) Check("culprit_bad", false, blamedBad);
        view.targetTexture = null;
        Destroy(target);
        Destroy(read);

        // A picture from the angle the flare was seen at, through the real post effects.
        var w = wizard.transform;
        var head = w.position + Vector3.up * 1.6f;
        Cut(head - w.forward * 2.6f + Vector3.up * 2.6f, head);
        yield return Wait(0.3f);
        Shot("from-45-degrees");
        yield return null;
        outfit.Wear(saved);
    }

    void Raw(UniversalAdditionalCameraData extra, bool post, string name)
    {
        extra.renderPostProcessing = false;
        Sweep(name);
        extra.renderPostProcessing = post;
    }

    // Every yaw at every pitch and distance, as the wizard stands right now.
    void Sweep(string name)
    {
        var head = wizard.transform.position + Vector3.up * 1.6f;
        foreach (float pitch in Pitches)
            foreach (float distance in Distances)
                for (int yaw = 0; yaw < 360; yaw += 30)
                    Add(ViewFrom(head, pitch, yaw, distance, name));
    }

    Seen ViewFrom(Vector3 head, float pitch, int yaw, float distance, string name)
    {
        var from = head + Quaternion.Euler(pitch, yaw, 0f) * Vector3.back * distance;
        return new Seen { from = from, to = head, name = $"{name} pitch {pitch} yaw {yaw} at {distance} m", tally = Scan(from, head) };
    }

    void Add(Seen seen)
    {
        var tally = seen.tally;
        views++;
        if (tally.bad > 0) nanViews++;
        if (tally.bright > 0) brightViews++;
        nanPixels += tally.bad;
        brightPixels += tally.bright;
        peak = Mathf.Max(peak, tally.peak);
        outsideWizard += tally.outside;
        // Blamed here, on this very pose: a frame on, a walking wizard's pixels are gone.
        if (tally.bad > worstBad.tally.bad)
        {
            worstBad = seen;
            blamedBad = Blame("bad", seen, t => t.bad);
        }
        if (tally.bright > worstBright.tally.bright)
        {
            worstBright = seen;
            blamedBright = Blame("bright", seen, t => t.bright);
        }
    }

    // The worst view with each renderer of the wizard left out: the one that clears it is the culprit.
    string Blame(string what, Seen seen, System.Func<Tally, int> count)
    {
        var cleared = new List<string>();
        int was = count(Scan(seen.from, seen.to));
        Save(what);
        foreach (var r in wizard.GetComponentsInChildren<Renderer>())
        {
            r.forceRenderingOff = true;
            int now = count(Scan(seen.from, seen.to));
            r.forceRenderingOff = false;
            if (now < was) cleared.Add($"{r.name} [{r.sharedMaterial.shader.name}] {was}->{now}");
        }
        return cleared.Count > 0 ? string.Join("; ", cleared) : $"{NotTheWizard} ({was})";
    }

    // The view last scanned as a picture, its bad pixels magenta and its far too bright ones cyan.
    void Save(string what)
    {
        if (string.IsNullOrEmpty(ScreenshotDir)) return;
        var pixels = read.GetRawTextureData<Color>();
        var shown = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        var colours = new Color32[pixels.Length];
        for (int i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            float top = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            bool bad = float.IsNaN(c.r + c.g + c.b) || float.IsInfinity(top);
            colours[i] = bad ? new Color32(255, 0, 255, 255) : top > TooBright ? new Color32(0, 255, 255, 255) : (Color32)new Color(c.r, c.g, c.b).gamma;
        }
        shown.SetPixels32(colours);
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(ScreenshotDir, $"flare-worst-{what}.png"), shown.EncodeToPNG());
        Destroy(shown);
    }

    // One view, drawn raw into the float target, its pixels counted.
    Tally Scan(Vector3 from, Vector3 to)
    {
        view.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to - from));
        view.targetTexture = target;
        var box = WizardOnScreen();
        view.Render();
        var was = RenderTexture.active;
        RenderTexture.active = target;
        read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
        RenderTexture.active = was;
        view.targetTexture = null;
        var pixels = read.GetRawTextureData<Color>();
        var tally = default(Tally);
        for (int i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            float sum = c.r + c.g + c.b;
            float top = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            var pixel = new Vector2Int(i % Width, i / Width);
            bool bad = float.IsNaN(sum) || float.IsInfinity(sum);
            if (!box.Contains(pixel))
            {
                // Bright ground in the sun is no business of this probe; a NaN anywhere still flares.
                if (bad) tally.outside++;
                else continue;
            }
            if (bad) tally.bad++;
            else if (top > TooBright) tally.bright++;
            else
            {
                if (top > tally.peak) tally.peak = top;
                continue;
            }
            tally.at = pixel;
        }
        return tally;
    }

    // The wizard's box on the target.
    RectInt WizardOnScreen()
    {
        var bounds = new Bounds(wizard.transform.position + Vector3.up, Vector3.one);
        foreach (var r in wizard.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
        Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
        for (int i = 0; i < 8; i++)
        {
            var corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) * 2 - 1, (i & 2) - 1, (i & 4) / 2 - 1));
            Vector2 p = view.WorldToScreenPoint(corner);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        return new RectInt(Vector2Int.FloorToInt(min), Vector2Int.CeilToInt(max - min));
    }
}
