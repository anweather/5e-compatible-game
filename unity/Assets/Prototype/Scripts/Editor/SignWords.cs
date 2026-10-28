using UnityEditor;
using UnityEngine;

// Words on a sign or a board, for the builders: the font's glyphs drawn with
// Prototype/SignText, so they show only from the front and stay behind the board
// (the font's own material drew them through everything, mirrored on a sign's
// back), and shrunk until they fit the board's face (a long line ran past the
// sign's edges). Every sign and label in the world is written through here.
public static class SignWords
{
    const string MaterialPath = "Assets/Prototype/Generated/SignText.mat";
    static readonly Color Cream = new(0.98f, 0.94f, 0.82f);

    /// <summary>
    /// Writes <paramref name="text"/> on <paramref name="go"/>, which stands just in front of a
    /// board's face and is turned so the reader looks along its +z. The words are made at
    /// <paramref name="size"/> (TextMesh characterSize) and shrunk until they fit
    /// <paramref name="width"/> by <paramref name="height"/> metres; <paramref name="longest"/>,
    /// if given, is fitted instead (for words that change as the game runs).
    /// </summary>
    public static TextMesh Write(GameObject go, string text, float size, float width, float height, string longest = null)
    {
        var words = go.AddComponent<TextMesh>();
        words.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        words.fontSize = 64;
        words.characterSize = size;
        words.anchor = TextAnchor.MiddleCenter;
        words.alignment = TextAlignment.Center;
        words.color = Cream;
        var renderer = go.GetComponent<MeshRenderer>();
        if (words.font != null) renderer.sharedMaterial = Material(words.font);
        words.text = longest ?? text;
        Fit(words, renderer, width, height);
        words.text = text;
        return words;
    }

    static Material Material(Font font)
    {
        EditorAssets.EnsureFolderFor(MaterialPath);
        var mat = EditorAssets.LoadOrCreate(MaterialPath, () => new Material(Shader.Find("Prototype/SignText")));
        mat.mainTexture = font.material.mainTexture;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Shrinks the words until they fit, measured in their own plane (signs turn only about the
    // vertical, so their width is along x or z). Words not yet measured are left as made.
    static void Fit(TextMesh words, Renderer renderer, float width, float height)
    {
        var right = words.transform.right;
        for (int i = 0; i < 40; i++)
        {
            var size = renderer.bounds.size;
            if (size == Vector3.zero) return;
            float wide = Mathf.Abs(right.x) * size.x + Mathf.Abs(right.z) * size.z;
            if (wide <= width && size.y <= height) return;
            words.characterSize *= 0.92f;
        }
    }
}
