using System;
using System.Collections;
using System.Linq;
using Chem;
using UnityEngine;

// The probe's moments scenario (docs/design/the-hold.md, phase 4): each moment gone into by name, through what the list uses.
// MOMENT in the environment plays just that one. Nothing here is in the game.
public class MomentsProbe : ProbeScenario
{
    const float MostWait = 20f;
    const int NearRise = 3;

    protected override IEnumerator Run()
    {
        var world = grid.World;
        var moments = grid.GetComponent<HoldMoments>();
        Check("offered_here", moments != null && HoldMoments.Offered);
        if (moments == null || world.HoldSite == null) yield break;
        caster.enabled = true;
        string only = Environment.GetEnvironmentVariable("MOMENT");
        var site = world.HoldSite;
        foreach (var m in world.Rules.Sim.Hold.Moments.Where(m => only == null || m.Id == only))
        {
            Check(m.Id, moments.Enter(m.Id));
            for (float t = 0f; t < MostWait && world.Hold?.Moment != m.Id; t += Time.deltaTime) yield return null;
            yield return Wait(0.5f);
            string me = caster.CastPlayer;
            Check(m.Id + "_wave", world.Hold?.Wave == m.Wave, $"{world.Hold?.Wave}");
            Check(m.Id + "_hands", world.HandsOf(me).Holds.SequenceEqual(m.Holds), string.Join("+", world.HandsOf(me).Holds));
            var at = grid.CellAt(wizard.transform.position);
            Check(m.Id + "_at_the_castle", Mathf.Abs(at.x - site.Rise.x) + Mathf.Abs(at.y - site.Rise.y) <= NearRise, at.ToString());
            for (float t = 0f; t < MostWait && world.Raiders.Count == 0; t += Time.deltaTime) yield return null;
            Check(m.Id + "_raiders_come", world.Raiders.Count > 0);
            yield return Wait(0.5f);
            var drawn = FindAnyObjectByType<DefenseField>();
            Check(m.Id + "_raiders_drawn", drawn.DrawnRaiders > 0, $"{drawn.DrawnRaiders} of {world.Raiders.Count}");
            int barricades = World.BarricadesOf(world.Rules.Sim.Hold, m.Wave);
            var stakes = grid.GetComponent<HoldBarricades>();
            Check(m.Id + "_barricades_drawn", (stakes.Drawn > 0) == (barricades > 0), $"{stakes.Drawn} cells for {barricades}");
        }
        caster.enabled = false;
    }
}
