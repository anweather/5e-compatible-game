using System.Collections.Generic;

// Controller focus for the Esc menu (PauseMenu), kept apart from IMGUI so it
// can be tested: which page is showing (the main page, or a sub-page such as
// Mods or Credits, on a back stack), which item has the highlight, and which
// item the A button asked to activate.
//
// IMGUI has no list of its buttons, so the menu counts them as it draws:
// BeginPass at the start of every OnGUI pass, NextItem for each focusable
// button in drawing order, EndLayout after the layout pass (which fixes the
// count the highlight wraps around).
public class MenuNavigator
{
    public const string MainPage = "";

    readonly List<(string page, int focus)> stack = new();
    int counted;
    int pending = -1;

    public string Page => stack.Count == 0 ? MainPage : stack[stack.Count - 1].page;

    public bool AtTop => stack.Count == 0;

    public int Focus { get; private set; }

    public int ItemCount { get; private set; }

    public void Reset()
    {
        stack.Clear();
        Focus = 0;
        ItemCount = 0;
        pending = -1;
    }

    public void Push(string page)
    {
        stack.Add((page, Focus));
        Focus = 0;
        ItemCount = 0;
        pending = -1;
    }

    public bool Back()
    {
        if (stack.Count == 0) return false;
        Focus = stack[stack.Count - 1].focus;
        stack.RemoveAt(stack.Count - 1);
        ItemCount = 0;
        pending = -1;
        return true;
    }

    public void Remove(string page)
    {
        int at = stack.FindIndex(s => s.page == page);
        if (at < 0) return;
        while (stack.Count > at) Back();
    }

    public void Move(int delta)
    {
        int n = ItemCount;
        if (n <= 0) return;
        Focus = ((Focus + delta) % n + n) % n;
    }

    public void FocusAt(int item) => Focus = System.Math.Max(0, item);

    public void Activate()
    {
        if (ItemCount > 0) pending = Focus;
    }

    public bool ActivationPending => pending >= 0;

    public void BeginPass() => counted = 0;

    public int NextItem() => counted++;

    public void EndLayout()
    {
        ItemCount = counted;
        if (Focus >= ItemCount) Focus = ItemCount > 0 ? ItemCount - 1 : 0;
    }

    public bool TakeActivation(int item)
    {
        if (item != pending) return false;
        pending = -1;
        return true;
    }

    public void ClearActivation() => pending = -1;

    public const float RepeatDelay = 0.4f;
    public const float RepeatEvery = 0.09f;

    public static int RepeatSteps(float heldFor)
    {
        if (heldFor < RepeatDelay) return 1;
        return 2 + (int)((heldFor - RepeatDelay) / RepeatEvery);
    }
}
