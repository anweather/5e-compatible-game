using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

// Host / Join / Leave, top right. Solo play uses the scene's own wizard;
// hosting or joining removes it and the network spawns one wizard per
// player (up to MaxPlayers). Joining is by address for now (same machine
// or same network); internet invites via Unity Relay come next.
public class NetSession : MonoBehaviour
{
    public const int MaxPlayers = 4;
    public const ushort Port = 7777;

    string address = "127.0.0.1";
    GUIStyle box;

    NetworkManager Net => NetworkManager.Singleton;

    void Start()
    {
        if (Net != null) Net.ConnectionApprovalCallback = Approve;
    }

    public void Host()
    {
        PrepareForNetwork();
        Transport.SetConnectionData("0.0.0.0", Port, "0.0.0.0");
        Net.StartHost();
    }

    public void Join(string host)
    {
        PrepareForNetwork();
        Transport.SetConnectionData(host, Port);
        Net.StartClient();
    }

    public void Leave()
    {
        Net.Shutdown();
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    UnityTransport Transport => Net.GetComponent<UnityTransport>();

    // The solo wizard isn't networked; the network spawns everyone's.
    static void PrepareForNetwork()
    {
        var solo = GameObject.Find("Wizard");
        if (solo != null && solo.GetComponent<NetworkObject>() == null) Destroy(solo);
    }

    void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
    {
        bool room = Net.ConnectedClientsIds.Count < MaxPlayers;
        response.Approved = room;
        response.CreatePlayerObject = room;
        response.Reason = room ? null : $"The yard is full ({MaxPlayers} players).";
    }

    void OnGUI()
    {
        if (Net == null) return;
        box ??= new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.UpperLeft, padding = new RectOffset(10, 10, 8, 8) };
        GUI.skin.button.fontSize = 16;
        GUI.skin.textField.fontSize = 16;
        GUI.skin.label.fontSize = 16;
        var area = new Rect(Screen.width - 296, 12, 280, 132);
        GUILayout.BeginArea(area, box);
        if (!Net.IsListening)
        {
            GUILayout.Label("Play together (up to 4)");
            if (GUILayout.Button("Host a yard")) Host();
            GUILayout.BeginHorizontal();
            address = GUILayout.TextField(address, GUILayout.Width(150));
            if (GUILayout.Button("Join")) Join(address);
            GUILayout.EndHorizontal();
        }
        else
        {
            string role = Net.IsHost ? "Hosting" : Net.IsConnectedClient ? "Connected" : "Connecting…";
            int players = Net.IsServer ? Net.ConnectedClientsIds.Count : -1;
            GUILayout.Label(players >= 0 ? $"{role} · {players}/{MaxPlayers} players" : role);
            GUILayout.Label($"Port {Port}");
            if (GUILayout.Button("Leave")) Leave();
        }
        GUILayout.EndArea();
    }
}
