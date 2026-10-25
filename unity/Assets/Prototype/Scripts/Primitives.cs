using UnityEngine;

// Unity's built-in shapes (cube, sphere, cylinder...) used as greybox
// visuals. CreatePrimitive always adds a collider; most visuals don't want
// one, so this removes it: deferred in Play mode (as Destroy always is),
// at once when an editor builder is making a scene.
public static class Primitives
{
    /// <summary>A primitive with no collider. <paramref name="name"/> null keeps Unity's default ("Cube", ...).</summary>
    public static GameObject Visual(PrimitiveType type, string name = null)
    {
        var go = GameObject.CreatePrimitive(type);
        RemoveCollider(go);
        if (name != null) go.name = name;
        return go;
    }

    public static void RemoveCollider(GameObject go)
    {
        var collider = go.GetComponent<Collider>();
        if (Application.isPlaying) Object.Destroy(collider);
        else Object.DestroyImmediate(collider);
    }
}
