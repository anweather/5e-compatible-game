using System.Collections;
using UnityEngine;

// A one-take tour of the physics, earth shaping, things to carry and live
// rules, for trailer recordings (Prototype > Record Overnight Showcase, which
// records it through DemoRecorder.RecordUntil). Four chapters in the
// Chemistry Yard, on a fixed schedule so every take plays the same:
//
//   1. Physics: Thunderwave throws a crate into the pond and a boulder in
//      after it (the crate floats, the boulder sinks), then Fire Bolt on the
//      oil spill sets off the barrels.
//   2. Earth: a trench cut from the pond's bank fills, a Wall of Stone rises
//      across it, and Move Earth heaves a hill under a crate.
//   3. Things to carry, at dusk: a glowing ball thrown across the grass, the
//      bullseye lantern's beam, and the spyglass on the cabin.
//   4. Live rules: a grass fire, then the mods panel switches Wildfire on.
//
// The HUD is hidden except while the mods panel is shown. Dusk is play-mode
// only: the sun, ambient light and fog are put back, and the sky is a copy of
// the scene's, so the generated sky material is never touched.
public class OvernightShowcase : ShowcaseScript
{
    const float Beat = 0.5f;

    Lighting daylight;

    protected override void OnDestroy()
    {
        base.OnDestroy();
        daylight?.Restore();
    }

    protected override IEnumerator Play()
    {
        daylight = Lighting.Capture();
        yield return PhysicsChapter();
        yield return EarthChapter();
        yield return ItemsChapter();
        yield return RulesChapter();
    }

    IEnumerator PhysicsChapter()
    {
        Chapter("Physics");
        Place(15, 6);
        puppet.Face(Yard(19, 6));
        Cut(V(17f, 6.5f, 3f), V(26f, 0f, 14f));
        var flight = Fly(W(V(19.5f, 4.2f, 7f), V(25.5f, 0.3f, 13.5f), 3f));
        yield return Wait(1.2f);
        yield return Cast(SpellIds.Thunderwave, 15, 6, 1, 2.6f);
        yield return new WaitUntil(() => flight.Done);

        Place(24, 4);
        puppet.Face(Yard(23, 5));
        flight = Fly(W(V(35.5f, 3.6f, 7.5f), V(34f, 0.3f, 15f), 2.2f));
        yield return new WaitUntil(() => flight.Done);
        yield return Cast(SpellIds.Thunderwave, 24, 4, 3, 2.4f);

        flight = Fly(W(V(33f, 7.5f, 5f), V(28.5f, 0.5f, 18f), 2f));
        yield return new WaitUntil(() => flight.Done);
        yield return Cast(SpellIds.FireBolt, 19, 3, 0, 0f);
        flight = Fly(W(V(31f, 6.5f, 7.5f), V(28.5f, 0.8f, 18.5f), 6f));
        yield return new WaitUntil(() => flight.Done);
    }

    IEnumerator EarthChapter()
    {
        Chapter("Earth shaping");
        PutOutFires();
        Place(14, 8);
        var flight = Fly(W(V(13f, 8f, 5f), V(20f, 0f, 15f), 3f));
        yield return new WaitUntil(() => flight.Done);
        yield return Cast(SpellIds.EarthenTrench, 15, 5, 3, 5.5f);
        yield return Cast(SpellIds.WallOfStone, 12, 7, 0, 3f);

        flight = Fly(W(V(9f, 5.5f, 14f), V(15f, 1f, 6f), 2.5f));
        yield return new WaitUntil(() => flight.Done);
        yield return Cast(SpellIds.MoveEarth, 10, 11, 2, 3.5f);
    }

    IEnumerator ItemsChapter()
    {
        Chapter("Things to carry");
        PutOutFires();
        Place(14, 13);
        puppet.Face(Yard(8, 13));
        var flight = Fly(W(V(12f, 10f, -2f), V(17f, 0.8f, 3f), 1.5f), W(V(18.5f, 2.4f, -4.5f), V(16.5f, 1f, 3f), 2f));
        yield return daylight.Fade(0f, 1f, 3f);
        var ball = Item(ItemIds.GlowBallAzure);
        hands.PickUp(ball);
        yield return new WaitUntil(() => flight.Done);
        yield return Wait(Beat);
        Throw(Yard(8, 13), 8f);
        flight = Fly(W(V(16f, 2.6f, -4.5f), V(13f, 0.6f, 3f), 3f));
        yield return new WaitUntil(() => flight.Done);

        hands.PickUp(Item(ItemIds.BullseyeLantern));
        yield return FollowCamera(Yard(8, 8), pitch: 10f);
        yield return Sweep(Yard(8, 8), Yard(21, 8), 4.5f, pitch: 10f);

        hands.Drop();
        var cabin = V(-16.75f, 2f, 15.25f);
        brain.enabled = false;
        Place(2, 8);
        puppet.Face(cabin);
        var spyglass = Item(ItemIds.Spyglass);
        hands.PickUp(spyglass);
        hands.view.PreviousStateIsValid = false;
        yield return FollowCamera(cabin, pitch: 2f);
        yield return Wait(1f);
        yield return LookThrough(spyglass.GetComponent<Spyglass>(), cabin, 3.5f);
        hands.Drop();
    }

    IEnumerator RulesChapter()
    {
        Chapter("Change the rules mid-game");
        PutOutFires();
        var fire = Yard(5, 12);
        var cam = Camera.main.transform;
        Cut(cam.position, cam.position + cam.forward * 20f);
        var dawn = StartCoroutine(daylight.Fade(1f, 0f, 1.5f));
        var flight = Fly(W(V(-2f, 9.5f, -8f), fire + V(-4.5f, 0f, 3f), 2.5f));
        yield return Cast(SpellIds.FireBolt, 5, 12, 3, 0f);
        yield return new WaitUntil(() => flight.Done);
        yield return dawn;
        yield return Wait(0.8f);

        EventLog.HudVisible = true;
        var switcher = grid.GetComponent<RuleSwitcher>();
        ModsPanel.SetOpen(true);
        yield return Wait(1f);
        switcher.Toggle("wildfire");
        yield return Wait(3f);
        ModsPanel.SetOpen(false);
        EventLog.HudVisible = false;

        flight = Fly(W(V(-6f, 16f, -12f), fire + V(1f, 0f, 6f), 6f));
        yield return new WaitUntil(() => flight.Done);
        yield return Wait(Beat);
    }

    static Carryable Item(string id)
    {
        foreach (var item in FindObjectsByType<Carryable>(FindObjectsSortMode.InstanceID))
            if (item.GetComponent<PhysicsProp>().kind == id && item.Holder == null) return item;
        return null;
    }

    void Throw(Vector3 toward, float speed)
    {
        var item = hands.Held;
        puppet.Face(toward);
        hands.Drop();
        var dir = (toward - item.transform.position).normalized + Vector3.up * 0.5f;
        item.GetComponent<Rigidbody>().linearVelocity = dir.normalized * speed;
    }

    IEnumerator FollowCamera(Vector3 toward, float pitch)
    {
        for (int i = 0; i < 3; i++)
        {
            puppet.AimCamera(toward, pitch, 1000f);
            yield return null;
        }
        brain.enabled = true;
        yield return null;
    }

    IEnumerator Sweep(Vector3 from, Vector3 to, float seconds, float pitch)
    {
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            var at = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds));
            puppet.AimCamera(at, pitch, 1000f);
            puppet.Face(at);
            yield return null;
        }
    }

    IEnumerator LookThrough(Spyglass glass, Vector3 at, float seconds)
    {
        var controls = hands.controls;
        hands.controls = null;
        for (float t = 0f; t < seconds + 1f; t += Time.deltaTime)
        {
            puppet.AimCamera(at, 2f, 1000f);
            glass.WhileHeld(hands, t < seconds);
            yield return null;
        }
        glass.Released();
        hands.controls = controls;
    }

    sealed class Lighting
    {
        const float DuskSun = 0.2f, DuskAmbient = 0.3f, DuskExposure = 0.3f;
        static readonly Color DuskSunColor = new(0.65f, 0.72f, 1f), DuskFog = new(0.13f, 0.15f, 0.24f);

        Light sun;
        float sunIntensity, exposure;
        Color sunColor, fog;
        UnityEngine.Rendering.SphericalHarmonicsL2 ambient;
        Material sky, skyCopy;
        SkyLight skyLight;

        public static Lighting Capture()
        {
            var sun = RenderSettings.sun;
            if (sun == null)
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) sun = l;
            var sky = RenderSettings.skybox;
            return new Lighting
            {
                sun = sun,
                sunIntensity = sun != null ? sun.intensity : 1f,
                sunColor = sun != null ? sun.color : Color.white,
                ambient = RenderSettings.ambientProbe,
                fog = RenderSettings.fogColor,
                sky = sky,
                exposure = sky != null && sky.HasProperty("_Exposure") ? sky.GetFloat("_Exposure") : 1f,
                skyLight = FindAnyObjectByType<SkyLight>(),
            };
        }

        public void Blend(float k)
        {
            if (skyLight != null) skyLight.Held = true;
            k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k));
            if (sun != null)
            {
                sun.intensity = sunIntensity * Mathf.Lerp(1f, DuskSun, k);
                sun.color = Color.Lerp(sunColor, DuskSunColor, k);
            }
            RenderSettings.ambientProbe = ambient * Mathf.Lerp(1f, DuskAmbient, k);
            AirInPlay.HeldHorizon = Color.Lerp(fog, DuskFog, k);
            if (sky == null || !sky.HasProperty("_Exposure")) return;
            if (skyCopy == null)
            {
                skyCopy = new Material(sky) { name = "Sky (dusk)" };
                RenderSettings.skybox = skyCopy;
            }
            skyCopy.SetFloat("_Exposure", Mathf.Lerp(exposure, exposure * DuskExposure, k));
        }

        public IEnumerator Fade(float from, float to, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                Blend(Mathf.Lerp(from, to, t / seconds));
                yield return null;
            }
            if (to <= 0f) Restore();
            else Blend(to);
        }

        public void Restore()
        {
            if (sun != null)
            {
                sun.intensity = sunIntensity;
                sun.color = sunColor;
            }
            RenderSettings.ambientProbe = ambient;
            AirInPlay.HeldHorizon = null;
            if (sky != null) RenderSettings.skybox = sky;
            if (skyCopy != null) Destroy(skyCopy);
            skyCopy = null;
            if (skyLight != null) skyLight.Held = false;
        }
    }
}
