// Entering a join code without a keyboard, arcade high-score style: one
// letter at a time, up and down change it, A moves on to the next letter, B
// goes back one. The Join by code page of the Esc menu (NetSession) uses it.
// Only up, down, A and B are needed, so it stays clear of the d-pad's left
// and right (the spell radial's) and the shoulder buttons.
public class CodePicker
{
    public const int Length = 6;

    public const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    readonly int[] chars = new int[Length];

    public int Slot { get; private set; }

    public string Code
    {
        get
        {
            var text = new char[Length];
            for (int i = 0; i < Length; i++) text[i] = Alphabet[chars[i]];
            return new string(text);
        }
    }

    public void Begin(string start = null)
    {
        Slot = 0;
        for (int i = 0; i < Length; i++) chars[i] = 0;
        if (!LooksLikeCode(start)) return;
        string code = start.Trim().ToUpperInvariant();
        for (int i = 0; i < Length; i++) chars[i] = Alphabet.IndexOf(code[i]);
        Slot = Length - 1;
    }

    public void Step(int delta)
    {
        int n = Alphabet.Length;
        chars[Slot] = ((chars[Slot] + delta) % n + n) % n;
    }

    public bool Next()
    {
        if (Slot == Length - 1) return true;
        Slot++;
        return false;
    }

    public bool Back()
    {
        if (Slot == 0) return false;
        Slot--;
        return true;
    }

    public static bool LooksLikeCode(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string code = text.Trim().ToUpperInvariant();
        if (code.Length != Length) return false;
        foreach (char c in code)
            if (Alphabet.IndexOf(c) < 0) return false;
        return true;
    }
}
