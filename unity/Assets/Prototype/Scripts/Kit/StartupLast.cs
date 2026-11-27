using UnityEngine;

// The last of the first frame's Starts (the latest execution order), for the start-up timeline: the time since the
// first Start is every Start's. Made by StartupTimeline once the scene has loaded.
[DefaultExecutionOrder(32000)]
public sealed class StartupLast : MonoBehaviour
{
    void Start()
    {
        StartupTimeline.Mark("first frame: all Starts");
        Destroy(this);
    }
}
