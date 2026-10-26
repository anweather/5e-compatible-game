using UnityEngine;

// Lets the controller's A choose a button in the Esc menu (PauseMenu). IMGUI
// buttons only answer clicks, so when A is pressed this component draws the
// menu's current page once in its own layout pass, where PauseMenu.Button
// returns true for the highlighted button and its code runs. It lives beside
// the menu (PauseMenu adds it) so that whatever the button changes happens
// outside the menu's own layout and repaint passes, which therefore always
// agree. Nothing it draws is ever shown: it only takes part in layout passes.
[RequireComponent(typeof(PauseMenu))]
public class MenuActivator : MonoBehaviour
{
    PauseMenu menu;

    void Awake() => menu = GetComponent<PauseMenu>();

    void OnGUI()
    {
        if (Event.current.type == EventType.Layout && menu != null) menu.RunActivation();
    }
}
