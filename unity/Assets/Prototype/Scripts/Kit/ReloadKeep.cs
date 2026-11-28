// Lets an [ExecuteAlways] view that makes its own meshes when it's enabled (GeneratedProp, VillageHouse) keep what it
// made through an Editor domain reload, instead of throwing it away in OnDisable and making the same again in OnEnable a
// moment later. That happens on every script edit, for every such view in the open scene, and is a part of each edit's
// wait. A view keeps its objects (DontSave ones, which outlive the reload) only when a reload is what disables it, in edit
// mode; it takes them back when it's enabled again only if it is planned the same (every serialized field) and the code
// that made them is the same (its own assembly and each of ours it uses, by module version id, so an edit to its mesher
// makes them again). Anything not taken back is destroyed on the first update after the reload. In Play mode and in a
// built player nothing is kept, and the view makes its meshes as it always did.
using System.Collections.Generic;
using UnityEngine;

public static class ReloadKeep
{
#if UNITY_EDITOR
    static bool reloading;
    static readonly Dictionary<System.Reflection.Assembly, string> codes = new();

    [UnityEditor.InitializeOnLoadMethod]
    static void Hook()
    {
        UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += () => reloading = true;
        UnityEditor.EditorApplication.delayCall += () => ReloadKeepStash.instance.DropUnclaimed();
    }

    public static bool Keeping => reloading && !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode;

    public static void Keep(Object owner, params Object[] made) => ReloadKeepStash.instance.Put(owner, KeyOf(owner), made);

    public static Object[] Take(Object owner, int least) =>
        UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode || !ReloadKeepStash.instance.Holds(owner)
            ? null
            : ReloadKeepStash.instance.Take(owner, KeyOf(owner), least);

    static string KeyOf(Object owner) =>
        EnabledField.Replace(UnityEditor.EditorJsonUtility.ToJson(owner), "") + "\n" + CodeOf(owner.GetType().Assembly);

    static readonly System.Text.RegularExpressions.Regex EnabledField = new("\"m_Enabled\":(true|false),?", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    static string CodeOf(System.Reflection.Assembly assembly)
    {
        if (codes.TryGetValue(assembly, out var code)) return code;
        var loaded = new Dictionary<string, System.Reflection.Assembly>(System.StringComparer.Ordinal);
        foreach (var a in System.AppDomain.CurrentDomain.GetAssemblies()) loaded[a.GetName().Name] = a;
        var versions = new SortedDictionary<string, string>(System.StringComparer.Ordinal);
        var next = new Stack<System.Reflection.Assembly>();
        next.Push(assembly);
        while (next.Count > 0)
        {
            var a = next.Pop();
            string name = a.GetName().Name;
            if (versions.ContainsKey(name)) continue;
            versions[name] = a.ManifestModule.ModuleVersionId.ToString();
            foreach (var r in a.GetReferencedAssemblies())
                if (Ours(r.Name) && loaded.TryGetValue(r.Name, out var used)) next.Push(used);
        }
        var parts = new List<string>();
        foreach (var v in versions) parts.Add(v.Key + ":" + v.Value);
        code = string.Join(",", parts);
        codes[assembly] = code;
        return code;
    }

    static bool Ours(string name) =>
        name.StartsWith("Prototype.", System.StringComparison.Ordinal) || name.StartsWith("Chem", System.StringComparison.Ordinal);
#else
    public static bool Keeping => false;

    public static void Keep(Object owner, params Object[] made)
    {
    }

    public static Object[] Take(Object owner, int least) => null;
#endif
}
