// Entering a join code without a keyboard, arcade high-score style: one
// letter at a time, up and down change it, A moves on to the next letter, B
// goes back one. The Join by code page of the Esc menu (NetSession) uses it.
// Only up, down, A and B are needed, so it stays clear of the d-pad's left
// and right (the spell radial's) and the shoulder buttons.
public class CodePicker
{
    /// <summary>How long a Multiplayer Services session code is.</summary>
    public const int Length = 6;

    /// <summary>The characters a code can hold (NetSession upper-cases codes before joining).</summary>
    public const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    readonly int[] chars = new int[Length];

    /// <summary>The letter being changed, 0 to Length - 1.</summary>
    public int Slot { get; private set; }

    /// <summary>The code as entered so far (every slot holds a character).</summary>
    public string Code
    {
        get
        {
            var text = new char[Length];
            for (int i = 0; i < Length; i++) text[i] = Alphabet[chars[i]];
            return new string(text);
        }
    }

    /// <summary>
    /// Starts again. A code to begin from (one that was copied or typed) fills the slots and
    /// puts the cursor on the last letter, so A joins at once; anything else starts from AAAAAA.
    /// </summary>
    public void Begin(string start = null)
    {
        Slot = 0;
        for (int i = 0; i < Length; i++) chars[i] = 0;
        if (!LooksLikeCode(start)) return;
        string code = start.Trim().ToUpperInvariant();
        for (int i = 0; i < Length; i++) chars[i] = Alphabet.IndexOf(code[i]);
        Slot = Length - 1;
    }

    /// <summary>Changes the current letter: positive goes forward through A-Z then 0-9, wrapping.</summary>
    public void Step(int delta)
    {
        int n = Alphabet.Length;
        chars[Slot] = ((chars[Slot] + delta) % n + n) % n;
    }

    /// <summary>Moves on to the next letter. True when the last letter was already current (the code is done).</summary>
    public bool Next()
    {
        if (Slot == Length - 1) return true;
        Slot++;
        return false;
    }

    /// <summary>Goes back one letter. False on the first letter (leave the picker).</summary>
    public bool Back()
    {
        if (Slot == 0) return false;
        Slot--;
        return true;
    }

    /// <summary>True for text that could be a join code: six letters or digits, ignoring case and spaces around it.</summary>
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
