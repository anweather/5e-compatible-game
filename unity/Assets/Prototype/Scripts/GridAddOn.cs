using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Grid add-ons: the components a feature puts on the grid's GameObject, so the grid never names the feature.
// A MonoBehaviour marks itself [GridAddOn(stage, order)]; the grid adds every marked type at its stage
// (Grid at the end of ChemGrid.Awake, Rules in RuleSwitcher.Awake; Hud goes on the event log's GameObject, in
// EventLog.Awake, so the log never names the wizard's HUD), lowest order first, skipping one already
// there. The types are found once by reflection over the Prototype.* assemblies, so a feature in its own
// assembly joins by the mark alone, in Edit mode tests as in Play. A DevOnly add-on, and anything else offered only in
// the Editor and development builds (the Hold's moments), asks DevBuild.
public enum GridStage
{
    Rules,
    Grid,
    Hud,
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GridAddOnAttribute : Attribute
{
    public GridStage Stage { get; }
    public int Order { get; }
    public bool DevOnly { get; set; }

    public GridAddOnAttribute(GridStage stage, int order)
    {
        Stage = stage;
        Order = order;
    }
}

public static class GridAddOns
{
    static List<(Type type, GridAddOnAttribute mark)> found;

    public static bool DevBuild => Application.isEditor || Debug.isDebugBuild;

    public static IReadOnlyList<Type> At(GridStage stage) =>
        All().Where(a => a.mark.Stage == stage).Select(a => a.type).ToList();

    public static void Add(GameObject on, GridStage stage)
    {
        bool dev = DevBuild;
        foreach (var (type, mark) in All())
        {
            if (mark.Stage != stage || (mark.DevOnly && !dev)) continue;
            if (!on.TryGetComponent(type, out _)) on.AddComponent(type);
        }
    }

    static List<(Type type, GridAddOnAttribute mark)> All()
    {
        if (found != null) return found;
        var list = new List<(Type type, GridAddOnAttribute mark)>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!assembly.GetName().Name.StartsWith("Prototype.", StringComparison.Ordinal)) continue;
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (System.Reflection.ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).ToArray();
            }
            foreach (var type in types)
            {
                var mark = (GridAddOnAttribute)Attribute.GetCustomAttribute(type, typeof(GridAddOnAttribute), false);
                if (mark == null) continue;
                if (!typeof(MonoBehaviour).IsAssignableFrom(type) || type.IsAbstract)
                    throw new InvalidOperationException($"[GridAddOn] on {type.Name}, which isn't a concrete MonoBehaviour");
                list.Add((type, mark));
            }
        }
        list.Sort((a, b) =>
        {
            int c = a.Item2.Stage.CompareTo(b.Item2.Stage);
            if (c == 0) c = a.Item2.Order.CompareTo(b.Item2.Order);
            return c != 0 ? c : string.CompareOrdinal(a.Item1.FullName, b.Item1.FullName);
        });
        found = list;
        return found;
    }
}
