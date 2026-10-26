using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Tag, online (TagRules): the host starts and stops it from the Esc menu,
// watches where everyone's wizard stands, and keeps who is it in a network
// variable, so every machine agrees. Whoever is it glows red on every screen,
// and a line at the top says who is it; a tag is announced to everyone.
[RequireComponent(typeof(NetworkObject))]
public class NetTag : NetworkBehaviour
{
    const float LookEvery = 0.1f;
    const int SectionOrder = 20;
    static readonly Color ItTint = new Color(1f, 0.35f, 0.3f);

    readonly NetworkVariable<ulong> it = new(TagRules.Nobody);
    readonly TagRules rules = new();
    readonly List<(ulong player, Vector3 at)> spots = new();
    MaterialPropertyBlock tint;
    float nextLook;
    GUIStyle banner;

    public override void OnNetworkSpawn()
    {
        it.OnValueChanged += Changed;
        PauseMenu.AddSection(DrawMenu, SectionOrder);
        Show(it.Value);
    }

    public override void OnNetworkDespawn()
    {
        it.OnValueChanged -= Changed;
        PauseMenu.RemoveSection(DrawMenu);
        Show(TagRules.Nobody);
    }

    /// <summary>Who is it (a client id), or TagRules.Nobody.</summary>
    public ulong It => it.Value;

    /// <summary>Host: starts a game with this player it; false without two players.</summary>
    public bool StartWith(ulong first)
    {
        if (!IsServer) return false;
        var players = new List<ulong>(NetworkManager.ConnectedClientsIds);
        if (!rules.Start(players, players.IndexOf(first))) return false;
        it.Value = rules.It;
        StartedRpc(rules.It);
        return true;
    }

    /// <summary>Host: ends the game.</summary>
    public void Stop()
    {
        if (!IsServer || !rules.Playing) return;
        rules.Stop();
        it.Value = TagRules.Nobody;
        EndedRpc();
    }

    void Changed(ulong was, ulong now) => Show(now);

    void Update()
    {
        if (!IsServer || !rules.Playing || Time.time < nextLook) return;
        nextLook = Time.time + LookEvery;
        spots.Clear();
        foreach (var client in NetworkManager.ConnectedClientsList)
            if (client.PlayerObject != null) spots.Add((client.ClientId, client.PlayerObject.transform.position));
        ulong before = rules.It;
        ulong tagged = rules.Update(Time.time, spots);
        if (rules.It != it.Value) it.Value = rules.It;
        if (tagged != TagRules.Nobody) TaggedRpc(before, tagged);
        else if (!rules.Playing) EndedRpc();
    }

    // The host's button; everyone else sees whether a game is on.
    void DrawMenu()
    {
        if (!IsSpawned) return;
        if (!IsServer)
        {
            GUILayout.Label(it.Value == TagRules.Nobody ? "<color=#ffffff88>Tag: the host can start a game</color>" : "<color=#ffffff88>Tag is on</color>");
            return;
        }
        if (!PauseMenu.Button(rules.Playing ? "Stop tag" : "Start tag (someone is it)", PauseMenu.ButtonStyle)) return;
        if (rules.Playing)
        {
            Stop();
            return;
        }
        var players = NetworkManager.ConnectedClientsIds;
        if (players.Count < 2 || !StartWith(players[Random.Range(0, players.Count)])) EventLog.Add("Tag needs two players or more");
    }

    [Rpc(SendTo.ClientsAndHost)]
    void StartedRpc(ulong who) => EventLog.Add($"Tag! {Name(who)} {(who == NetworkManager.LocalClientId ? "are" : "is")} it");

    [Rpc(SendTo.ClientsAndHost)]
    void TaggedRpc(ulong from, ulong to) => EventLog.Add($"{Name(from)} tagged {Name(to).ToLowerInvariant()}: {Name(to).ToLowerInvariant()} {(to == NetworkManager.LocalClientId ? "are" : "is")} it");

    [Rpc(SendTo.ClientsAndHost)]
    void EndedRpc()
    {
        if (IsSpawned) EventLog.Add("Tag is over");
    }

    string Name(ulong player) => player == NetworkManager.LocalClientId ? "You" : $"Player {player + 1}";

    // Whoever is it glows red; everyone else looks as usual.
    void Show(ulong who)
    {
        foreach (var wizard in FindObjectsByType<NetWizard>(FindObjectsSortMode.None))
        {
            bool red = who != TagRules.Nobody && wizard.OwnerClientId == who;
            foreach (var r in wizard.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (red)
                {
                    tint ??= new MaterialPropertyBlock();
                    r.GetPropertyBlock(tint);
                    tint.SetColor("_BaseColor", ItTint);
                    r.SetPropertyBlock(tint);
                }
                else r.SetPropertyBlock(null);
            }
        }
    }

    void OnGUI()
    {
        ulong who = it.Value;
        if (!IsSpawned || who == TagRules.Nobody || !EventLog.HudVisible) return;
        banner ??= new GUIStyle(GUI.skin.label) { fontSize = 22, richText = true, alignment = TextAnchor.MiddleCenter };
        string text = who == NetworkManager.LocalClientId
            ? "<b><color=#ff6a5a>TAG</color></b>  You're it! Tag someone"
            : $"<b><color=#ff6a5a>TAG</color></b>  Player {who + 1} is it: run!";
        GUI.Label(new Rect(Screen.width / 2f - 300f, 64f, 600f, 34f), text, banner);
    }
}
