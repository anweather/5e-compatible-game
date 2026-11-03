using System.Collections;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

// The probe's terminal scenario (ClaudeTerminal, J-056): with Claude switched off, what the terminal
// needs turns it back on; the folder's .mcp.json is valid and names this game's address, its
// CLAUDE.md and first screen say to press Win+H, nothing in it is a credential, and a client using
// that address lists the tools. Opening the terminal window itself is left to a real player.
public class TerminalProbe : ProbeScenario
{
    protected override IEnumerator Run()
    {
        var server = RulesMcpServer.Instance;
        Check("serving_here", server != null);
        if (server == null) yield break;
        bool was = server.Serving;
        server.Serving = false;
        Check("off_closes_the_port", RulesMcpServer.Url == null);
        server.Serving = true;
        Check("on_again_has_an_address", RulesMcpServer.Url != null, RulesMcpServer.Url);
        string folder = Path.Combine(Application.temporaryCachePath, "terminal-probe");
        ClaudeTerminal.WriteFolder(folder, RulesMcpServer.Url);
        var config = JObject.Parse(File.ReadAllText(Path.Combine(folder, ".mcp.json")));
        string url = (string)config["mcpServers"]?["spell-valley"]?["url"];
        Check("mcp_json_names_this_game", url == RulesMcpServer.Url && (string)config["mcpServers"]["spell-valley"]["type"] == "http", config.ToString());
        Check("no_secret_in_it", !config.ToString().Contains("token", System.StringComparison.OrdinalIgnoreCase) && !config.ToString().Contains("key", System.StringComparison.OrdinalIgnoreCase));
        Check("guide_says_win_h", File.ReadAllText(Path.Combine(folder, "CLAUDE.md")).Contains(ClaudeTerminal.TalkLine));
        Check("first_screen_says_win_h", File.ReadAllText(Path.Combine(folder, "welcome.txt")).Contains(ClaudeTerminal.TalkLine));
        Check("folder_is_the_games", ChemGrid.Folders.Claude.Path.StartsWith(Application.persistentDataPath, System.StringComparison.Ordinal), ChemGrid.Folders.Claude.Path);
        var list = ListTools(url);
        while (!list.IsCompleted) yield return null;
        string answer = list.Status == TaskStatus.RanToCompletion ? list.Result : list.Exception?.GetBaseException().Message;
        Check("a_client_lists_the_tools", answer != null && answer.Contains("set_look") && answer.Contains("here"), answer?.Substring(0, Mathf.Min(160, answer.Length)));
        Directory.Delete(folder, true);
        server.Serving = was;
    }

    static async Task<string> ListTools(string url)
    {
        using var client = new HttpClient();
        var request = new StringContent("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}", Encoding.UTF8, "application/json");
        var reply = await client.PostAsync(url, request);
        return await reply.Content.ReadAsStringAsync();
    }
}
