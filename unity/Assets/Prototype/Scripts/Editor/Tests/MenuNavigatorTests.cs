using NUnit.Framework;

// The Esc menu's controller focus (MenuNavigator): wrapping up and down,
// activating the highlighted item once, sub-pages on a back stack, and the
// hold-to-repeat timing; and the join code picker (CodePicker).
public class MenuNavigatorTests
{
    // A layout pass that draws this many focusable items.
    static void Layout(MenuNavigator nav, int items)
    {
        nav.BeginPass();
        for (int i = 0; i < items; i++) nav.NextItem();
        nav.EndLayout();
    }

    [Test]
    public void MovesAndWrapsBothWays()
    {
        var nav = new MenuNavigator();
        Layout(nav, 4);
        nav.Move(-1);
        Assert.That(nav.Focus, Is.EqualTo(3), "up from the first item wraps to the last");
        nav.Move(1);
        Assert.That(nav.Focus, Is.EqualTo(0), "down from the last wraps to the first");
        nav.Move(2);
        Assert.That(nav.Focus, Is.EqualTo(2));
    }

    [Test]
    public void ActivatesTheHighlightedItemOnce()
    {
        var nav = new MenuNavigator();
        Layout(nav, 3);
        nav.Move(1);
        nav.Activate();
        Assert.That(nav.ActivationPending, Is.True);
        Assert.That(nav.TakeActivation(0), Is.False);
        Assert.That(nav.TakeActivation(1), Is.True);
        Assert.That(nav.TakeActivation(1), Is.False, "only once");
        Assert.That(nav.ActivationPending, Is.False);
    }

    [Test]
    public void NothingToActivateOnAnEmptyPage()
    {
        var nav = new MenuNavigator();
        nav.Activate();
        nav.Move(1);
        Assert.That(nav.ActivationPending, Is.False);
        Assert.That(nav.Focus, Is.EqualTo(0));
    }

    [Test]
    public void SubPagesGoBackToTheItemThatOpenedThem()
    {
        var nav = new MenuNavigator();
        Layout(nav, 6);
        nav.Move(4);
        nav.Push("mods");
        Assert.That(nav.Page, Is.EqualTo("mods"));
        Assert.That(nav.Focus, Is.EqualTo(0), "a new page starts on its first item");
        Assert.That(nav.AtTop, Is.False);
        Layout(nav, 6);
        nav.Move(3);
        Assert.That(nav.Back(), Is.True);
        Assert.That(nav.Page, Is.EqualTo(MenuNavigator.MainPage));
        Assert.That(nav.Focus, Is.EqualTo(4));
        Assert.That(nav.Back(), Is.False, "B on the main page closes the menu instead");
    }

    [Test]
    public void ARemovedPageClosesWithThePagesAboveIt()
    {
        var nav = new MenuNavigator();
        nav.Push("play");
        nav.Push("join-code");
        nav.Remove("play");
        Assert.That(nav.AtTop, Is.True);
        nav.Push("credits");
        nav.Remove("mods");
        Assert.That(nav.Page, Is.EqualTo("credits"), "removing a page that isn't open changes nothing");
    }

    [Test]
    public void FocusStaysOnAPageThatShrinks()
    {
        var nav = new MenuNavigator();
        Layout(nav, 8);
        nav.Move(7);
        Layout(nav, 5); // e.g. the Play together section went online and lost buttons
        Assert.That(nav.Focus, Is.EqualTo(4));
    }

    [Test]
    public void HoldingRepeatsAfterADelay()
    {
        Assert.That(MenuNavigator.RepeatSteps(0f), Is.EqualTo(1));
        Assert.That(MenuNavigator.RepeatSteps(MenuNavigator.RepeatDelay - 0.01f), Is.EqualTo(1));
        Assert.That(MenuNavigator.RepeatSteps(MenuNavigator.RepeatDelay), Is.EqualTo(2));
        Assert.That(MenuNavigator.RepeatSteps(MenuNavigator.RepeatDelay + 3.5f * MenuNavigator.RepeatEvery), Is.EqualTo(5));
    }

    [Test]
    public void PickerStepsLettersAndWraps()
    {
        var picker = new CodePicker();
        picker.Begin();
        Assert.That(picker.Code, Is.EqualTo("AAAAAA"));
        picker.Step(-1);
        Assert.That(picker.Code, Is.EqualTo("9AAAAA"), "back from A wraps to 9");
        picker.Step(2);
        Assert.That(picker.Code, Is.EqualTo("BAAAAA"));
        Assert.That(picker.Next(), Is.False);
        picker.Step(25);
        Assert.That(picker.Code, Is.EqualTo("BZAAAA"));
        Assert.That(picker.Back(), Is.True);
        Assert.That(picker.Slot, Is.EqualTo(0));
        Assert.That(picker.Back(), Is.False, "B on the first letter leaves the picker");
    }

    [Test]
    public void PickerIsDoneAfterTheLastLetter()
    {
        var picker = new CodePicker();
        picker.Begin();
        for (int i = 0; i < CodePicker.Length - 1; i++) Assert.That(picker.Next(), Is.False);
        Assert.That(picker.Next(), Is.True);
    }

    [Test]
    public void PickerStartsFromACopiedCode()
    {
        var picker = new CodePicker();
        picker.Begin("  k7xq2m \n");
        Assert.That(picker.Code, Is.EqualTo("K7XQ2M"));
        Assert.That(picker.Slot, Is.EqualTo(CodePicker.Length - 1), "one A joins");
        picker.Begin("hello there");
        Assert.That(picker.Code, Is.EqualTo("AAAAAA"), "the clipboard held something else");
        Assert.That(picker.Slot, Is.EqualTo(0));
    }

    [TestCase("ABC123", true)]
    [TestCase("abc123", true)]
    [TestCase("ABC12", false)]
    [TestCase("ABC-12", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void RecognisesJoinCodes(string text, bool code) => Assert.That(CodePicker.LooksLikeCode(text), Is.EqualTo(code));
}
