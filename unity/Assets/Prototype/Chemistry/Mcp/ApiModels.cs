// What a player's API key can do (backlog 27, "Model list", J-071): the answer to GET /v1/models in plain
// words, and the models the key can use that the loop can run. HTTP is the view's (ApiWire); this reads what
// came back. The sentences here are fixed: none repeats what the API said, so none can carry a key.
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Chem.Mcp
{
    /// <summary>How a key's check came out.</summary>
    public enum KeyAnswer
    {
        /// <summary>It works, and it can use a model the game can run.</summary>
        Good,
        /// <summary>It works, but it can use none of the models the game can run.</summary>
        NoModels,
        /// <summary>The API turned it down.</summary>
        Refused,
        /// <summary>Nothing answered: no network, or the API couldn't be reached.</summary>
        Unreached,
        /// <summary>The API answered, but with no list to read.</summary>
        Failed,
        /// <summary>It was never sent: it can't be a key, or a saved one can't be read.</summary>
        NotSent,
    }

    /// <summary>A key's check: how it came out, what to tell the player, and the models to pick from.</summary>
    public sealed class KeyCheck
    {
        public KeyAnswer Answer;
        public string Says;
        public IReadOnlyList<string> Models = System.Array.Empty<string>();
    }

    /// <summary>What asks the API which models a key can use.</summary>
    public interface IKeyLister
    {
        /// <summary>Blocks until it knows, so never on the main thread.</summary>
        KeyCheck Models(string key, System.Threading.CancellationToken token);
    }

    public static class ApiModels
    {
        const string Works = "That key works.", NoneOfOurs = "That key works, but it can't use any model this game can run.",
            Turned = "That key didn't work. Check you copied all of it.", NotAllowed = "That key isn't allowed to list models.",
            Offline = "Couldn't reach Anthropic. Check your connection and try again.", Later = "Anthropic couldn't check that key just now. Try again in a moment.",
            NotAKey = "That doesn't look like a key.";

        /// <summary>Whether this could go in a header at all: no spaces, no line breaks, nothing that isn't plain ASCII.</summary>
        public static bool LooksLikeKey(string key) =>
            !string.IsNullOrEmpty(key) && key.All(c => c > ' ' && c < 127);

        /// <summary>A key that was never sent.</summary>
        public static KeyCheck NotSent(string says = NotAKey) => new KeyCheck { Answer = KeyAnswer.NotSent, Says = says };

        /// <summary>Nothing answered.</summary>
        public static KeyCheck Unreached() => new KeyCheck { Answer = KeyAnswer.Unreached, Says = Offline };

        /// <summary>The API's answer to GET /v1/models.</summary>
        public static KeyCheck Read(int status, string body)
        {
            if (status == 401) return new KeyCheck { Answer = KeyAnswer.Refused, Says = Turned };
            if (status == 403) return new KeyCheck { Answer = KeyAnswer.Refused, Says = NotAllowed };
            if (status != 200 || !TryList(body, out var listed)) return new KeyCheck { Answer = KeyAnswer.Failed, Says = Later };
            var usable = listed.Where(id => ApiPrice.Of(id) != null).ToList();
            return usable.Count == 0
                ? new KeyCheck { Answer = KeyAnswer.NoModels, Says = NoneOfOurs }
                : new KeyCheck { Answer = KeyAnswer.Good, Says = Works, Models = usable };
        }

        // The ids in the answer, in its order; false if it isn't a list of models.
        static bool TryList(string body, out List<string> ids)
        {
            ids = new List<string>();
            try
            {
                if (!(JObject.Parse(body)["data"] is JArray data)) return false;
                ids.AddRange(data.OfType<JObject>().Select(m => (string)m["id"]).Where(id => id != null));
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
