using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

// A probe's count of the managed heap by owner (to find what grows with the map): from every MonoBehaviour in the
// scene and every static field of our assemblies, each managed object reached is counted once, against the first
// field that reached it, as "Type.field" (the root's field) and "Type.field > Type.field" (one hop in). Sizes are
// estimates (arrays exact, objects by their field count). Slow (it walks everything): only outside measured frames.
public static class ManagedCensus
{
    const double Mb = 1024.0 * 1024.0;
    const int Listed = 30;

    static readonly Dictionary<Type, FieldInfo[]> FieldsOf = new();
    static readonly Dictionary<Type, bool> Plain = new();

    /// <summary>The top owners, one per line, biggest first: level-one roots then level-two paths.</summary>
    public static string Report(string label)
    {
        var top = new Dictionary<string, long>();
        var deep = new Dictionary<string, long>();
        var seen = new HashSet<object>(RefEq.Instance);
        var stack = new Stack<(object o, string l1, string l2, int depth)>();

        void Walk(object root, string l1)
        {
            if (root == null || !seen.Add(root)) return;
            stack.Push((root, l1, null, 0));
            while (stack.Count > 0)
            {
                var (o, a, b, d) = stack.Pop();
                long size = Shallow(o);
                top[a] = top.TryGetValue(a, out long t) ? t + size : size;
                if (b != null) deep[b] = deep.TryGetValue(b, out long u) ? u + size : size;
                Children(o, (child, field) =>
                {
                    if (child == null || child is UnityEngine.Object || child is MemberInfo || child is Assembly || child is System.Threading.Thread) return;
                    if (!seen.Add(child)) return;
                    string next = b ?? (d == 0 ? a : $"{a} > {field}");
                    stack.Push((child, a, d >= 1 ? next : null, d + 1));
                });
            }
        }

        var behaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var mb in behaviours)
        {
            if (mb == null) continue;
            foreach (var f in Fields(mb.GetType()))
            {
                var v = f.GetValue(mb);
                if (v is UnityEngine.Object || v == null || IsSkipped(v)) continue;
                Walk(v, $"{mb.GetType().Name}.{f.Name}");
            }
        }
        var assemblies = new[] { typeof(ChemGrid).Assembly, typeof(Chem.World).Assembly };
        foreach (var asm in assemblies.Distinct())
            foreach (var type in SafeTypes(asm))
            {
                if (type.ContainsGenericParameters) continue;
                foreach (var f in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (f.IsLiteral) continue;
                    object v;
                    try { v = f.GetValue(null); } catch { continue; }
                    if (v == null || v is UnityEngine.Object || IsSkipped(v)) continue;
                    Walk(v, $"static {type.Name}.{f.Name}");
                }
            }

        var inv = CultureInfo.InvariantCulture;
        long total = top.Values.Sum();
        var s = new StringBuilder($"CENSUS at={label} counted_mb={(total / Mb).ToString("0", inv)} mono_used_mb={(UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / Mb).ToString("0", inv)}");
        foreach (var kv in top.OrderByDescending(k => k.Value).Take(Listed))
            s.Append($"\nCENSUS {label} {(kv.Value / Mb).ToString("0.0", inv)} MB {kv.Key}");
        foreach (var kv in deep.OrderByDescending(k => k.Value).Take(Listed))
            s.Append($"\nCENSUS2 {label} {(kv.Value / Mb).ToString("0.0", inv)} MB {kv.Key}");
        return s.ToString();
    }

    /// <summary>
    /// The world's unpacked chunks by how far (in chunks, the larger axis) they are from a cell, split by whether the
    /// view's drawn hint holds them: who keeps them unpacked.
    /// </summary>
    public static string Unpacked(Chem.World world, Vector2Int cell)
    {
        if (world == null) return "UNPACKED none";
        var list = typeof(Chem.World).GetField("unpackedChunks", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(world) as System.Collections.IEnumerable;
        var hint = typeof(Chem.World).GetField("drawnHint", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(world) as HashSet<long>;
        if (list == null) return "UNPACKED unreadable";
        int fx = cell.x >> Chem.Chunks.Shift, fy = cell.y >> Chem.Chunks.Shift;
        var byAway = new SortedDictionary<int, (int hinted, int not)>();
        FieldInfo fX = null, fY = null;
        foreach (object ch in list)
        {
            fX ??= ch.GetType().GetField("X");
            fY ??= ch.GetType().GetField("Y");
            int x = (int)fX.GetValue(ch), y = (int)fY.GetValue(ch);
            int away = Mathf.Max(Mathf.Abs(x - fx), Mathf.Abs(y - fy));
            bool hinted = hint != null && hint.Contains(Chem.Chunks.Key(x, y));
            byAway.TryGetValue(away, out var n);
            byAway[away] = hinted ? (n.hinted + 1, n.not) : (n.hinted, n.not + 1);
        }
        return $"UNPACKED tick={world.Tick} packs={world.Packs} unpacks={world.Unpacks} by chunks away (hinted/not): " + string.Join(" ", byAway.Select(kv => $"{kv.Key}:{kv.Value.hinted}/{kv.Value.not}"));
    }

    static IEnumerable<Type> SafeTypes(Assembly asm)
    {
        try { return asm.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
    }

    static bool IsSkipped(object v) => v is MemberInfo || v is Assembly || v is System.Threading.Thread;

    static long Shallow(object o)
    {
        if (o is string str) return 20 + 2L * str.Length;
        if (o is Array arr)
        {
            var et = o.GetType().GetElementType();
            long each = et.IsValueType ? ValueSize(et) : IntPtr.Size;
            return 32 + each * arr.LongLength;
        }
        var t = o.GetType();
        if (t.IsValueType) return ValueSize(t) + 16;
        return 16 + Fields(t).Length * 8L;
    }

    static long ValueSize(Type t)
    {
        try { return UnsafeUtility.SizeOf(t); } catch { return 8; }
    }

    static void Children(object o, Action<object, string> each)
    {
        var t = o.GetType();
        if (o is string) return;
        if (o is Array arr)
        {
            var et = t.GetElementType();
            if (et.IsPrimitive || et.IsEnum || et.IsPointer) return;
            if (et.IsValueType && IsPlain(et)) return;
            string name = $"{et.Name}[]";
            foreach (var e in arr)
            {
                if (e == null) continue;
                if (et.IsValueType) Children(e, (c, _) => each(c, name));
                else each(e, name);
            }
            return;
        }
        foreach (var f in Fields(t))
        {
            var ft = f.FieldType;
            if (ft.IsPrimitive || ft.IsEnum || ft.IsPointer || ft == typeof(IntPtr) || ft == typeof(UIntPtr)) continue;
            if (ft.IsValueType && IsPlain(ft)) continue;
            object v;
            try { v = f.GetValue(o); } catch { continue; }
            if (v == null) continue;
            string name = $"{t.Name}.{f.Name}";
            if (ft.IsValueType) Children(v, (c, _) => each(c, name));
            else each(v, name);
        }
    }

    // A struct with no references in it (nothing to follow).
    static bool IsPlain(Type t)
    {
        if (Plain.TryGetValue(t, out bool p)) return p;
        Plain[t] = true;
        bool plain = true;
        foreach (var f in Fields(t))
        {
            var ft = f.FieldType;
            if (ft.IsPrimitive || ft.IsEnum || ft.IsPointer || ft == typeof(IntPtr) || ft == typeof(UIntPtr)) continue;
            if (ft.IsValueType && IsPlain(ft)) continue;
            plain = false;
            break;
        }
        Plain[t] = plain;
        return plain;
    }

    static FieldInfo[] Fields(Type t)
    {
        if (FieldsOf.TryGetValue(t, out var fs)) return fs;
        var list = new List<FieldInfo>();
        for (var c = t; c != null && c != typeof(object) && c != typeof(MonoBehaviour) && c != typeof(Behaviour) && c != typeof(Component) && c != typeof(UnityEngine.Object); c = c.BaseType)
            list.AddRange(c.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
        fs = list.ToArray();
        FieldsOf[t] = fs;
        return fs;
    }

    sealed class RefEq : IEqualityComparer<object>
    {
        public static readonly RefEq Instance = new();
        public new bool Equals(object a, object b) => ReferenceEquals(a, b);
        public int GetHashCode(object o) => RuntimeHelpers.GetHashCode(o);
    }
}
