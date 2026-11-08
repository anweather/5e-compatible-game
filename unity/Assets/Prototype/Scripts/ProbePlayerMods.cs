using System.Collections;
using System.Collections.Generic;
using Chem;
using Chem.Mcp;
using Newtonsoft.Json.Linq;
using UnityEngine;

// The probe's playermods scenario (docs/RULES_MCP.md "Player stats", WizardStats): what a player asks
// of Claude ("make me faster", "make me bigger", "jump higher", "quadruple jump"), each through the
// real tool (Workbench.SetPlayer, the asker's slot from GameHere) and into the room as a mod, as apply
// does. In a meadow by the yard: the wizard walks, jumps and air-jumps as made, then with each stat
// stacked on the last it walks faster, stands twice as tall on the ground, jumps higher and takes three
// air jumps (a fourth refused); a mod setting each back to normal puts the wizard as it was. With
// --shots, a film of all of it from the follow camera.
public class PlayerModsProbe : LooksModProbe
{
    const float Settle = 0.6f, Walk = 1f, Faster = 1.6f, Higher = 1.6f, Tall = 1.9f;

    Workbench bench;
    readonly GameHere here = new();
    RuleSwitcher switcher;
    WizardStats stats;
    DoubleJump jumper;
    Vector3 spot;
    readonly List<string> on = new();

    protected override IEnumerator Run()
    {
        switcher = grid.GetComponent<RuleSwitcher>();
        stats = wizard.GetComponent<WizardStats>();
        jumper = wizard.GetComponent<DoubleJump>();
        Check("wizard_has_stats", stats != null && jumper != null, "the wizard has WizardStats and DoubleJump");
        if (stats == null || jumper == null || switcher == null) yield break;
        bench = new Workbench(new GameFolders(ChemGrid.Folders.Rules)) { Here = here };
        here.Follow();
        if (!FindMeadow(out var meadow))
        {
            Check("meadow_found", false, "no open grass near the yard");
            yield break;
        }
        spot = grid.CellCenter(meadow.x + Apart / 2, meadow.y);
        spot.y = grid.GroundAt(spot) + 0.05f;
        if (Filming) StartFilm();

        float walked = 0f, jumped = 0f, height = Height();
        int airs = 0;
        yield return Measure(d => walked = d, h => jumped = h, n => airs = n);
        Metric("normal_walk_m", walked);
        Metric("normal_jump_m", jumped);
        Metric("normal_height_m", height);
        Check("normal_double_jump", airs == 1, $"{airs} air jumps as made");

        yield return Set("speed", "2");
        float fast = 0f;
        yield return WalkMeasure(d => fast = d);
        Metric("speed2_walk_m", fast);
        Check("speed_walks_faster", fast > walked * Faster, $"walked {fast:0.00} m in {Walk} s, {walked:0.00} m as made");

        yield return Set("size", "2");
        Home();
        yield return Wait(1f);
        float tall = Height();
        float feet = wizard.transform.position.y - grid.GroundAt(wizard.transform.position);
        Metric("size2_height_m", tall);
        Metric("size2_feet_over_ground_m", feet);
        Check("size_grows", tall > height * Tall && !stats.NoRoom, $"capsule {tall:0.00} m tall, {height:0.00} m as made");
        Check("size_stands", wizard.GetComponent<CharacterController>().isGrounded && Mathf.Abs(feet) < 0.3f, $"feet {feet:0.00} m over the ground");
        float bigWalk = 0f;
        yield return WalkMeasure(d => bigWalk = d);
        Check("size_still_walks", bigWalk > walked, $"walked {bigWalk:0.00} m big and fast");

        yield return Set("jump", "2");
        float high = 0f;
        yield return Measure(null, h => high = h, null);
        Metric("jump2_m", high);
        Check("jump_higher", high > jumped * Higher, $"jumped {high:0.00} m, {jumped:0.00} m as made");

        yield return Set("jumps", "4");
        int quad = 0;
        yield return Measure(null, null, n => quad = n);
        Metric("jumps4_air_jumps", quad);
        Check("quadruple_jump", quad == 3, $"{quad} air jumps with jumps 4 (3 wanted, the 4th refused)");

        yield return Set("normal", null);
        float back = 0f, backJump = 0f;
        int backAirs = 0;
        yield return Measure(d => back = d, h => backJump = h, n => backAirs = n);
        float backHeight = Height();
        Check("normal_puts_it_back", stats.Applied.IsNormal && Mathf.Abs(wizard.transform.localScale.y - 1f) < 0.01f,
            $"{PlayerParams.Words(stats.Applied)} scale {wizard.transform.localScale.y:0.00}");
        Check("normal_moves_as_made", Mathf.Abs(back - walked) < walked * 0.25f && Mathf.Abs(backJump - jumped) < jumped * 0.25f && backAirs == 1 && Mathf.Abs(backHeight - height) < 0.05f,
            $"walk {back:0.00} m, jump {backJump:0.00} m, {backAirs} air jumps, {backHeight:0.00} m tall");

        yield return Off();
        if (Filming) yield return EndFilm(sounds: false);
    }

    float Height() => wizard.GetComponent<CharacterController>().height * wizard.transform.lossyScale.y;

    void Home()
    {
        Teleport(spot);
        puppet.Face(spot + Vector3.forward * 10f);
    }

    // One stat for the asker's slot by set_player, into the room as a mod on top of the others; "normal" sets each back.
    IEnumerator Set(string stat, string value)
    {
        var draft = bench.DraftOf(null);
        string said = "";
        if (stat == "normal")
            foreach (var s in PlayerParams.Stats) said = bench.SetPlayer(draft.Id, s, "normal", "me");
        else said = bench.SetPlayer(draft.Id, stat, value, "me");
        string id = "player-mods-" + stat;
        string mod = new JObject { ["title"] = id, ["patch"] = draft.Patch }.ToString();
        string refused = switcher.Ask(RoomCommand.Apply(switcher.Me, id, mod, id));
        on.Add(id);
        EventLog.Add(stat == "normal" ? "Claude: you're back to normal" : $"Claude: your {stat} is now {value}");
        yield return Wait(Settle);
        Check("mod_on_" + stat, refused == null && (World.Room == null || World.Room.Mods.Exists(m => m.Id == id)), refused ?? said.Split('\n')[^1]);
    }

    IEnumerator Off()
    {
        foreach (var id in on) switcher.Ask(RoomCommand.Revert(switcher.Me, id));
        on.Clear();
        yield return Wait(0.5f);
    }

    IEnumerator Measure(System.Action<float> walk, System.Action<float> jump, System.Action<int> airs)
    {
        if (walk != null) yield return WalkMeasure(walk);
        if (jump != null) yield return JumpMeasure(false, jump, null);
        if (airs != null) yield return JumpMeasure(true, null, airs);
    }

    // Walks forward for Walk seconds from the spot and gives the ground covered.
    IEnumerator WalkMeasure(System.Action<float> walked)
    {
        Home();
        yield return Wait(Settle);
        var from = wizard.transform.position;
        puppet.Move(Vector2.up);
        yield return Wait(Walk);
        puppet.Move(Vector2.zero);
        var moved = wizard.transform.position - from;
        moved.y = 0f;
        walked(moved.magnitude);
        yield return Wait(0.4f);
    }

    // A jump through the Starter Assets input, as the jump button does; with air, an air jump asked every 0.3 s near each top.
    IEnumerator JumpMeasure(bool air, System.Action<float> peak, System.Action<int> taken)
    {
        Home();
        yield return Wait(Settle);
        var input = wizard.GetComponent(WizardRig.InputsComponent);
        float from = wizard.transform.position.y, top = from;
        int took = 0;
        float next = 0.3f;
        input.SendMessage("JumpInput", true);
        for (float t = 0f; t < (air ? 4.5f : 1.8f); t += Time.deltaTime)
        {
            top = Mathf.Max(top, wizard.transform.position.y);
            if (t > 0.1f) input.SendMessage("JumpInput", false);
            if (air && t >= next && took < 6)
            {
                next += 0.3f;
                if (jumper.AirJump()) took++;
            }
            yield return null;
        }
        peak?.Invoke(top - from);
        taken?.Invoke(took);
        for (float t = 0f; t < 3f && !wizard.GetComponent<CharacterController>().isGrounded; t += Time.deltaTime) yield return null;
    }
}
