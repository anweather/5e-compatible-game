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
    /// <summary>The main page's id (Resume, the sections, Quit).</summary>
    public const string MainPage = "";

    // Pages opened on top of the main page, each with the focus to restore when it closes.
    readonly List<(string page, int focus)> stack = new();
    int counted;
    int pending = -1;

    /// <summary>The page showing now.</summary>
    public string Page => stack.Count == 0 ? MainPage : stack[stack.Count - 1].page;

    /// <summary>True on the main page, where B closes the menu.</summary>
    public bool AtTop => stack.Count == 0;

    /// <summary>The highlighted item, counted in drawing order from 0.</summary>
    public int Focus { get; private set; }

    /// <summary>How many focusable items the current page drew in its last layout pass.</summary>
    public int ItemCount { get; private set; }

    /// <summary>Back to the main page with the first item highlighted.</summary>
    public void Reset()
    {
        stack.Clear();
        Focus = 0;
        ItemCount = 0;
        pending = -1;
    }

    /// <summary>Opens a sub-page on top of the current one, its first item highlighted.</summary>
    public void Push(string page)
    {
        stack.Add((page, Focus));
        Focus = 0;
        ItemCount = 0;
        pending = -1;
    }

    /// <summary>Closes the current sub-page and highlights the item that opened it. False on the main page.</summary>
    public bool Back()
    {
        if (stack.Count == 0) return false;
        Focus = stack[stack.Count - 1].focus;
        stack.RemoveAt(stack.Count - 1);
        ItemCount = 0;
        pending = -1;
        return true;
    }

    /// <summary>Closes a page wherever it is on the stack, with any opened from it (its owner went away).</summary>
    public void Remove(string page)
    {
        int at = stack.FindIndex(s => s.page == page);
        if (at < 0) return;
        while (stack.Count > at) Back();
    }

    /// <summary>Moves the highlight down (positive) or up (negative), wrapping around the page.</summary>
    public void Move(int delta)
    {
        int n = ItemCount;
        if (n <= 0) return;
        Focus = ((Focus + delta) % n + n) % n;
    }

    /// <summary>Asks for the highlighted item to act as if clicked, the next time the menu runs its items.</summary>
    public void Activate()
    {
        if (ItemCount > 0) pending = Focus;
    }

    /// <summary>True while an activation waits for the menu to run its items.</summary>
    public bool ActivationPending => pending >= 0;

    /// <summary>Start of an OnGUI pass: items are counted from 0 again.</summary>
    public void BeginPass() => counted = 0;

    /// <summary>The next focusable item's index in this pass.</summary>
    public int NextItem() => counted++;

    /// <summary>End of a layout pass: the page's item count is known, and the highlight stays on it.</summary>
    public void EndLayout()
    {
        ItemCount = counted;
        if (Focus >= ItemCount) Focus = ItemCount > 0 ? ItemCount - 1 : 0;
    }

    /// <summary>True once for the item an activation is waiting for (which then no longer waits).</summary>
    public bool TakeActivation(int item)
    {
        if (item != pending) return false;
        pending = -1;
        return true;
    }

    /// <summary>Drops an activation no item took (its item went away).</summary>
    public void ClearActivation() => pending = -1;

    // Holding up or down repeats: once at the press, again after RepeatDelay, then every RepeatEvery.
    public const float RepeatDelay = 0.4f;
    public const float RepeatEvery = 0.09f;

    /// <summary>How many steps a direction held this long has made (1 at the press).</summary>
    public static int RepeatSteps(float heldFor)
    {
        if (heldFor < RepeatDelay) return 1;
        return 2 + (int)((heldFor - RepeatDelay) / RepeatEvery);
    }
}
